using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

// Use case: analyze a match from the point of view of one player.
public class GetMatchAnalysisHandler(
    IReplayAnalyzer replayAnalyzer,
    IMatchRepository matchRepository,
    IReplayAnalysisRepository replayAnalysisRepository,
    IReplayParseQueue replayParseQueue,
    IHeroCatalog heroCatalog,
    IItemCatalog itemCatalog,
    TimeProvider timeProvider,
    ILogger<GetMatchAnalysisHandler> logger)
{
    public const string UnknownHeroName = GetRecentMatchesHandler.UnknownHeroName;
    public const string UnknownItemName = "Unknown item";

    // How long a saved unparsed match is used before OpenDota is asked again (its replay may get parsed).
    public static readonly TimeSpan UnparsedRefreshInterval = TimeSpan.FromMinutes(15);

    // How long a replay we could not read is shown as failed before it is queued again.
    public static readonly TimeSpan FailedReplayRetryInterval = TimeSpan.FromHours(1);

    /// <returns>The analysis, or null when the match does not exist.</returns>
    /// <exception cref="DataSourceUnavailableException">The match could not be loaded.</exception>
    public async Task<MatchAnalysis?> HandleAsync(
        MatchId matchId, AccountId accountId, CancellationToken cancellationToken)
    {
        // All three run at the same time; heroes and items usually come from the cache.
        var matchTask = LoadMatchAsync(matchId, cancellationToken);
        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        var itemsTask = itemCatalog.GetItemsAsync(cancellationToken);
        await Task.WhenAll(matchTask, heroesTask, itemsTask);

        if (matchTask.Result is not { } match)
        {
            return null;
        }

        var names = new NameLookup(heroesTask.Result, itemsTask.Result);
        var currentPlayer = match.FindPlayer(accountId);

        IReadOnlyList<MatchPlayerSummary> TeamOf(Team team) => match.Players
            .Where(player => player.Team == team)
            .Select(player => ToSummary(player, ReferenceEquals(player, currentPlayer), names))
            .ToList();

        var (replayStatus, laning) = await GetLaningAsync(match, currentPlayer, names, cancellationToken);

        return new MatchAnalysis(
            MatchId: match.MatchId.Value,
            DurationSeconds: (int)match.Duration.TotalSeconds,
            StartTime: match.StartTime,
            RadiantWon: MatchResult.Won(Team.Radiant, match.WinningTeam),
            RadiantScore: match.RadiantScore,
            DireScore: match.DireScore,
            IsParsed: match.IsParsed,
            Radiant: TeamOf(Team.Radiant),
            Dire: TeamOf(Team.Dire),
            Performance: currentPlayer is null ? null : ToPerformance(match, currentPlayer, names),
            ReplayStatus: replayStatus,
            Laning: laning);
    }

    // Our own reading of the replay, for matches OpenDota didn't parse. It never breaks the analysis: anything
    // that goes wrong here only changes the status.
    private async Task<(ReplayStatus Status, LaningPhase? Laning)> GetLaningAsync(
        MatchDetails match, MatchPlayer? currentPlayer, NameLookup names, CancellationToken cancellationToken)
    {
        // OpenDota's data is already complete: the database isn't even asked.
        if (match.IsParsed)
        {
            return (ReplayStatus.NotNeeded, null);
        }

        // Checked in memory first: while the page polls a replay being parsed, the database is left alone.
        if (replayParseQueue.IsQueued(match.MatchId))
        {
            return (ReplayStatus.Processing, null);
        }

        SavedReplayAnalysis? saved;
        try
        {
            saved = await replayAnalysisRepository.FindAsync(match.MatchId, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            // Without the database a result could not be saved either, so nothing is queued.
            logger.LogWarning(ex, "Could not read the replay result of match {MatchId} from the database", match.MatchId);
            return (ReplayStatus.Unavailable, null);
        }

        switch (saved)
        {
            case { State: SavedReplayState.Ready, Analysis: { } analysis }:
                return (ReplayStatus.Ready, currentPlayer is null ? null : ToLaningPhase(analysis, currentPlayer, names));
            case { State: SavedReplayState.Expired }:
                return (ReplayStatus.Expired, null);
            case { State: SavedReplayState.Failed } when timeProvider.GetUtcNow() - saved.SavedAt < FailedReplayRetryInterval:
                return (ReplayStatus.Failed, null);
        }

        // Never parsed, or failed long enough ago to try again. When it can't be queued now, a failure stays a
        // failure; otherwise the feature is just unavailable at the moment (the next visit tries again).
        var notQueued = saved is null ? ReplayStatus.Unavailable : ReplayStatus.Failed;

        // Without the replay's address there is nothing to parse. Not "expired": OpenDota may learn the address
        // later (and matches saved before the address was stored get it back when refreshed from OpenDota).
        if (match.ReplayUrl is null)
        {
            return (notQueued, null);
        }

        var enqueued = replayParseQueue.TryEnqueue(match.MatchId, match.ReplayUrl);
        if (enqueued is ReplayEnqueueResult.Enqueued or ReplayEnqueueResult.AlreadyQueued)
        {
            return (ReplayStatus.Processing, null);
        }

        // Unavailable is not logged: with the feature off it would be every visit.
        if (enqueued == ReplayEnqueueResult.Full)
        {
            logger.LogInformation("The replay parse queue is full; match {MatchId} was not queued", match.MatchId);
        }

        return (notQueued, null);
    }

    // Null when the player's hero is not in the replay.
    private static LaningPhase? ToLaningPhase(ReplayAnalysis replay, MatchPlayer player, NameLookup names)
    {
        if (LaneOutcomeEvaluator.Evaluate(replay, player.HeroId) is not { } lane)
        {
            return null;
        }

        LaningPlayer ToLaningPlayer(LaningStats stats) => new(
            HeroId: stats.HeroId,
            HeroName: names.Hero(stats.HeroId),
            LastHits: stats.LastHits,
            Denies: stats.Denies,
            NetWorth: stats.NetWorth,
            Experience: stats.Experience,
            Deaths: stats.Deaths);

        return new LaningPhase(
            EndsAtSeconds: replay.EndsAtSeconds,
            Lane: lane.Player.Lane,
            Outcome: lane.Outcome,
            NetWorthDifference: lane.NetWorthDifference,
            Player: ToLaningPlayer(lane.Player),
            Allies: lane.Allies.Select(ToLaningPlayer).ToList(),
            Opponents: lane.Opponents.Select(ToLaningPlayer).ToList());
    }

    // Our database first, then OpenDota:
    // - a saved parsed match is final: OpenDota is never asked again;
    // - a saved unparsed match is used for a while, then OpenDota is asked again (the replay may have been
    //   parsed since), and it is still used if OpenDota is down;
    // - a database failure only costs the shortcut: OpenDota still answers.
    private async Task<MatchDetails?> LoadMatchAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        var saved = await FindSavedAsync(matchId, cancellationToken);
        if (saved is not null
            && (saved.Match.IsParsed || timeProvider.GetUtcNow() - saved.SavedAt < UnparsedRefreshInterval))
        {
            return saved.Match;
        }

        MatchDetails? match;
        try
        {
            match = await replayAnalyzer.GetMatchDetailsAsync(matchId, cancellationToken);
        }
        catch (DataSourceUnavailableException ex) when (saved is not null)
        {
            logger.LogWarning(ex, "OpenDota is unavailable; using the saved copy of match {MatchId}", matchId);
            return saved.Match;
        }

        // Only finished matches (known result) are saved.
        if (match is { WinningTeam: not null })
        {
            await TrySaveAsync(match, cancellationToken);
        }

        return match ?? saved?.Match;
    }

    private async Task<SavedMatch?> FindSavedAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        try
        {
            return await matchRepository.FindAsync(matchId, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not read match {MatchId} from the database", matchId);
            return null;
        }
    }

    private async Task TrySaveAsync(MatchDetails match, CancellationToken cancellationToken)
    {
        try
        {
            await matchRepository.SaveAsync(match, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not save match {MatchId} to the database", match.MatchId);
        }
    }

    private static MatchPlayerSummary ToSummary(MatchPlayer player, bool isCurrentPlayer, NameLookup names) =>
        new(
            AccountId: player.AccountId?.Value,
            IsCurrentPlayer: isCurrentPlayer,
            HeroId: player.HeroId,
            HeroName: names.Hero(player.HeroId),
            Kills: player.Kda.Kills,
            Deaths: player.Kda.Deaths,
            Assists: player.Kda.Assists,
            LastHits: player.LastHits,
            Denies: player.Denies,
            GoldPerMinute: player.GoldPerMinute,
            ExperiencePerMinute: player.ExperiencePerMinute,
            NetWorth: player.NetWorth,
            HeroDamage: player.HeroDamage,
            TowerDamage: player.TowerDamage,
            HeroHealing: player.HeroHealing,
            Level: player.Level,
            Items: player.ItemIds.Select(names.Item).ToList(),
            NeutralItem: player.NeutralItemId is { } neutralItemId ? names.Item(neutralItemId) : null);

    private static PlayerPerformance ToPerformance(MatchDetails match, MatchPlayer player, NameLookup names)
    {
        var evaluation = PerformanceEvaluator.Evaluate(player);
        return new PlayerPerformance(
            HeroId: player.HeroId,
            HeroName: names.Hero(player.HeroId),
            IsRadiant: player.Team == Team.Radiant,
            Won: match.Won(player),
            Metrics: evaluation.Metrics
                .Select(m => new MetricPerformance(m.Metric, m.Value, m.Percentile, m.Rating))
                .ToList(),
            Strengths: evaluation.Strengths.Select(m => m.Metric).ToList(),
            Weaknesses: evaluation.Weaknesses.Select(m => m.Metric).ToList());
    }

    private sealed class NameLookup(IReadOnlyDictionary<int, Hero> heroes, IReadOnlyDictionary<int, Item> items)
    {
        public string Hero(int heroId) => heroes.GetValueOrDefault(heroId)?.Name ?? UnknownHeroName;

        public string Item(int itemId) => items.GetValueOrDefault(itemId)?.Name ?? UnknownItemName;
    }
}
