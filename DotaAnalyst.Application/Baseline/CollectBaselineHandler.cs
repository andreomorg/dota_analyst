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

// The collector's settings (Infrastructure binds them from "Collector").
public record BaselineCollectorSettings(int DailyLimit, IReadOnlyList<AccountId> FocusAccounts);

// Use case: adds one match to the comparison base — pro matches first, then high-rank matches with the heroes the
// focus accounts play. Each collected match gives the metrics of its ten heroes.
public class CollectBaselineHandler(
    IBaselineMatchSource source,
    IBaselineRepository repository,
    IReplayAnalyzer replayAnalyzer,
    IReplayParser replayParser,
    IMatchProvider matchProvider,
    BaselineCollectorSettings settings,
    TimeProvider timeProvider,
    ILogger<CollectBaselineHandler> logger)
{
    private static readonly BaselineSource[] Sources = [BaselineSource.Pro, BaselineSource.HighRank];

    public async Task<CollectResult> CollectNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await repository.CountCollectedSinceAsync(now.AddDays(-1), cancellationToken) >= settings.DailyLimit)
        {
            return CollectResult.LimitReached;
        }

        var focusHeroes = await FocusHeroesAsync(cancellationToken);
        foreach (var baselineSource in Sources)
        {
            IReadOnlyList<BaselineCandidate> candidates;
            try
            {
                candidates = await source.GetCandidatesAsync(baselineSource, focusHeroes, cancellationToken);
            }
            catch (DataSourceUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not list {Source} matches", baselineSource);
                return CollectResult.SourceUnavailable;
            }

            var known = await repository.FindKnownAsync(candidates.Select(candidate => candidate.MatchId).ToList(), cancellationToken);
            if (candidates.FirstOrDefault(candidate => !known.Contains(candidate.MatchId.Value)) is { } next)
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
            await SaveAsync(candidate, details, BaselineMatchStatus.Collected, analysis.Metrics ?? [], now, cancellationToken);
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
