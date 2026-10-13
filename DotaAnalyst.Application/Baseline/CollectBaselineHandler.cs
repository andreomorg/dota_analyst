using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Baseline;

// What one collection step did; the background service waits more or less depending on it.
public enum CollectResult
{
    // A match was collected (or tried): go on soon.
    Progressed,
    // Nothing new in the sources: wait for new matches.
    NothingNew,
    // The daily limit was reached.
    LimitReached,
    // The replay parser (or OpenDota) is not answering: wait and try again.
    SourceUnavailable,
}

// The collector's settings (Infrastructure binds them from "Collector"). TargetMatchesPerHero: high-rank matches the
// base should have of each hero the focus accounts played recently, before pro matches come first again.
public record BaselineCollectorSettings(int DailyLimit, IReadOnlyList<AccountId> FocusAccounts, int TargetMatchesPerHero = 10);

// Use case: adds one match to the comparison base. While a hero of the focus accounts' recent matches has fewer
// than TargetMatchesPerHero high-rank matches in the base, high-rank matches with those heroes come first (the
// match of the hero with the fewest); otherwise pro matches first, then high-rank matches with any focus hero. Each
// collected match gives the metrics of its ten heroes.
public class CollectBaselineHandler(
    IBaselineMatchSource source,
    IBaselineRepository repository,
    IReplayAnalyzer replayAnalyzer,
    IReplayParser replayParser,
    IMatchProvider matchProvider,
    IItemCatalog itemCatalog,
    BaselineCollectorSettings settings,
    TimeProvider timeProvider,
    ILogger<CollectBaselineHandler> logger)
{
    public async Task<CollectResult> CollectNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await repository.CountCollectedSinceAsync(now.AddDays(-1), cancellationToken) >= settings.DailyLimit)
        {
            return CollectResult.LimitReached;
        }

        var focusHeroes = await FocusHeroesAsync(cancellationToken);
        // The same window the published base uses: older matches don't count.
        var counts = focusHeroes.Count == 0
            ? new Dictionary<int, int>()
            : await repository.CountMatchesByHeroAsync(BaselineSource.HighRank, now - PublishBaselineHandler.Window, cancellationToken);
        var needy = focusHeroes.Where(hero => counts.GetValueOrDefault(hero) < settings.TargetMatchesPerHero).ToHashSet();
        (BaselineSource Source, IReadOnlySet<int> Heroes)[] plan = needy.Count > 0
            ? [(BaselineSource.HighRank, needy), (BaselineSource.Pro, focusHeroes)]
            : [(BaselineSource.Pro, focusHeroes), (BaselineSource.HighRank, focusHeroes)];

        foreach (var (baselineSource, heroes) in plan)
        {
            IReadOnlyList<BaselineCandidate> candidates;
            try
            {
                candidates = await source.GetCandidatesAsync(baselineSource, heroes, cancellationToken);
            }
            catch (DataSourceUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not list {Source} matches", baselineSource);
                return CollectResult.SourceUnavailable;
            }

            var known = await repository.FindKnownAsync(candidates.Select(candidate => candidate.MatchId).ToList(), cancellationToken);
            var fresh = candidates.Where(candidate => !known.Contains(candidate.MatchId.Value));
            if (baselineSource == BaselineSource.HighRank && needy.Count > 0)
            {
                // The match of the needy hero with the fewest matches (OrderBy is stable: newest first on a tie).
                fresh = fresh.OrderBy(candidate => (candidate.HeroIds ?? [])
                    .Where(needy.Contains)
                    .Select(hero => counts.GetValueOrDefault(hero))
                    .DefaultIfEmpty(int.MaxValue)
                    .Min());
            }

            if (fresh.FirstOrDefault() is { } next)
            {
                return await CollectAsync(next, now, cancellationToken);
            }
        }

        return CollectResult.NothingNew;
    }

    private async Task<CollectResult> CollectAsync(BaselineCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        MatchDetails? details;
        try
        {
            details = await replayAnalyzer.GetMatchDetailsAsync(candidate.MatchId, cancellationToken);
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not load match {MatchId} for the comparison base", candidate.MatchId.Value);
            return CollectResult.SourceUnavailable;
        }

        if (details?.ReplayUrl is not { } replayUrl)
        {
            await SaveAsync(candidate, details, BaselineMatchStatus.NoReplay, [], now, cancellationToken);
            return CollectResult.Progressed;
        }

        try
        {
            var analysis = await replayParser.ParseAsync(candidate.MatchId, replayUrl, cancellationToken);
            var metrics = analysis.Metrics is { } replayMetrics && analysis.Purchases is { } purchases
                ? FirstBigItemMetric.AddTo(replayMetrics, purchases, await itemCatalog.GetItemsAsync(cancellationToken))
                : analysis.Metrics ?? [];
            await SaveAsync(candidate, details, BaselineMatchStatus.Collected, metrics, now, cancellationToken);
            logger.LogInformation(
                "Collected {Source} match {MatchId} for the comparison base", candidate.Source, candidate.MatchId.Value);
        }
        catch (ReplayExpiredException)
        {
            await SaveAsync(candidate, details, BaselineMatchStatus.NoReplay, [], now, cancellationToken);
        }
        catch (ReplayUnreadableException ex)
        {
            logger.LogWarning(ex, "Replay of match {MatchId} could not be read", candidate.MatchId.Value);
            await SaveAsync(candidate, details, BaselineMatchStatus.Failed, [], now, cancellationToken);
        }
        catch (Exception ex) when (ex is ReplayParserBusyException or ReplayParserUnavailableException)
        {
            // Not saved: tried again when the parser is back.
            logger.LogInformation("Replay parser unavailable for the comparison base: {Reason}", ex.Message);
            return CollectResult.SourceUnavailable;
        }

        return CollectResult.Progressed;
    }

    private Task SaveAsync(
        BaselineCandidate candidate, MatchDetails? details, BaselineMatchStatus status, IReadOnlyList<HeroMetrics> metrics,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        repository.SaveAsync(
            new BaselineMatch(
                candidate.MatchId,
                candidate.Source,
                details?.StartTime ?? now,
                (int)(details?.Duration.TotalSeconds ?? 0),
                status,
                metrics,
                now),
            cancellationToken);

    // The heroes of the focus accounts' recent matches (none when OpenDota can't say: then high-rank matches wait).
    private async Task<IReadOnlySet<int>> FocusHeroesAsync(CancellationToken cancellationToken)
    {
        var heroes = new HashSet<int>();
        foreach (var account in settings.FocusAccounts)
        {
            try
            {
                foreach (var match in await matchProvider.GetRecentMatchesAsync(account, cancellationToken))
                {
                    heroes.Add(match.HeroId);
                }
            }
            catch (DataSourceUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not load the recent matches of account {AccountId}", account.Value);
            }
        }

        return heroes;
    }
}
