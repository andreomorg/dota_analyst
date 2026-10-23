using static DotaAnalyst.Application.Matches.MatchAnalysisMapper;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
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
    IBaselineProvider baselineProvider,
    TimeProvider timeProvider,
    ILogger<GetMatchAnalysisHandler> logger,
    IOpenDotaParseCoordinator? openDotaParsing = null,
    IGameBuildCatalog? gameBuilds = null)
{
    public const string UnknownHeroName = GetRecentMatchesHandler.UnknownHeroName;
    public const string UnknownItemName = "Unknown item";

    // How long a saved unparsed match is used before OpenDota is asked again (its replay may get parsed).
    public static readonly TimeSpan UnparsedRefreshInterval = TimeSpan.FromMinutes(15);

    // How long a replay we could not read (or download, or that was missing too soon after the match to call it
    // expired) is shown as failed before it is queued again.
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
        var historical = gameBuilds is null ? null : await gameBuilds.ForMatchAsync(match.StartTime, cancellationToken);
        IReadOnlyDictionary<string, ItemDetails>? itemRules = historical is { } rulesBuild
            ? rulesBuild.Catalog.Items ?? new Dictionary<string, ItemDetails>() : null;
        var calculationItems = historical is { } itemBuild
            ? itemsTask.Result.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                Details = pair.Value.Details is { } details ? itemBuild.Catalog.Items?.GetValueOrDefault(details.Key) : null,
            }) : itemsTask.Result;
        var calculationHeroes = historical is { } heroBuild ? heroBuild.Catalog.Heroes
            : heroesTask.Result.Values.Where(hero => hero.Details is not null).ToDictionary(hero => hero.Id, hero => hero.Details!);
        var currentPlayer = match.FindPlayer(accountId);
        var openDotaProcessing = openDotaParsing is null ? null
            : await openDotaParsing.EnsureAsync(match, cancellationToken);

        IReadOnlyList<MatchPlayerSummary> TeamOf(Team team) => match.Players
            .Where(player => player.Team == team)
            .Select(player => ToSummary(player, ReferenceEquals(player, currentPlayer), names))
            .ToList();

        var (replayStatus, replay) = await GetReplayAsync(match, cancellationToken);
        var playerDamage = replay?.Damage is { } damageRows && currentPlayer is not null
            ? ToDamage(damageRows, match, currentPlayer, names) : null;
        var replayMetrics = replay?.Metrics is { } metrics
            ? FirstBigItemMetric.AddTo(metrics, replay.Purchases, calculationItems, itemRules)
            : replay?.Metrics;

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
            Laning: replay is null || currentPlayer is null ? null : ToLaningPhase(replay, currentPlayer, names),
            Timeline: replay?.Timeline is { } timeline ? ToTimelineChart(replay, timeline, currentPlayer, names) : null,
            Deaths: replay?.Deaths is { } deaths && currentPlayer is not null ? ToDeaths(deaths, currentPlayer, names) : null,
            Wards: replay?.Wards is { } wards ? ToWards(wards, currentPlayer, names) : null,
            Damage: playerDamage,
            // Deaths read before the damage have no assists: no fights until the replay is read again.
            Fights: replay?.Deaths is { } fightDeaths && replay.Damage is not null ? ToFights(fightDeaths, match, currentPlayer) : null,
            ItemBuild: replay?.Purchases is { } itemPurchases && currentPlayer is not null
                ? ToItemBuild(itemPurchases, currentPlayer, calculationItems, itemRules)
                : null,
            Objectives: replay?.Objectives is { } objectives ? ToObjectives(objectives, currentPlayer, names) : null,
            SmartStats: replayMetrics is { } metricsForAnalysis && currentPlayer is not null
                ? await WithBaselinesAsync(ToSmartStats(metricsForAnalysis, currentPlayer, names), metricsForAnalysis, currentPlayer, match, names, replay, cancellationToken)
                : null,
            Heatmaps: replay?.Heatmaps is { } heatmaps ? ToHeatmaps(heatmaps, match, currentPlayer, names) : null,
            AbilityBuild: replay?.AbilityUpgrades is { } upgrades && currentPlayer is not null
                ? new PlayerAbilityBuild(upgrades
                    .Where(upgrade => upgrade.HeroId == currentPlayer.HeroId)
                    .Select(upgrade => new AbilityBuildUpgrade(upgrade.Seconds, upgrade.AbilityName, upgrade.Level))
                    .ToList())
                : null,
            Stats: replay is not null && MatchStatTimeline.From(replay) is { } stats
                ? ToStats(stats, match, currentPlayer, names)
                : null,
            OpenDotaProcessing: openDotaProcessing,
            OpenDotaExtras: currentPlayer?.SupplementalStats is { } supplemental
                ? ToSupplemental(supplemental, itemsTask.Result) : null,
            Draft: currentPlayer is null ? null : ToDraft(match, currentPlayer, calculationHeroes, names, replay, playerDamage),
            Neutrals: replay?.NeutralCrafts is { } crafts
                ? ToNeutrals(crafts, replay, match, currentPlayer, calculationItems, names, historical?.Catalog.Neutrals)
                : null,
            Catalog: new CatalogProvenance(historical is null ? "currentFallback" : "matchBuild", historical?.Build.Id,
                (itemRules?.Count ?? calculationItems.Values.Count(item => item.Details is not null)) > 0,
                calculationHeroes?.Count > 0));
    }

    // The player's metrics against other players of the same hero and role (pro and high-rank matches), from the
    // comparison base (IBaselineProvider): an extra, the analysis comes without it when it can't be reached.
    private async Task<SmartStats?> WithBaselinesAsync(
        SmartStats? stats, IReadOnlyList<HeroMetrics> metrics, MatchPlayer player, MatchDetails match,
        NameLookup names, ReplayAnalysis? replay, CancellationToken cancellationToken)
    {
        if (stats is null || metrics.FirstOrDefault(hero => hero.HeroId == player.HeroId) is not { } mine)
        {
            return stats;
        }

        var history = replay is null ? null : MatchStatTimeline.From(replay)?.HistoryOf(mine.HeroId);
        var baselines = await baselineProvider.CompareAsync(mine, (int)match.Duration.TotalSeconds, history, cancellationToken);
        var opponentRole = LaneMatchup.OpposingRole(mine.Role);
        var laneOpponent = opponentRole is { } role
            ? metrics.FirstOrDefault(hero => hero.Team != mine.Team && hero.Role == role)
            : null;
        var matchupBaselines = laneOpponent is null
            ? []
            : await baselineProvider.CompareLaneMatchupAsync(
                mine, laneOpponent.HeroId, (int)match.Duration.TotalSeconds, history, cancellationToken);
        return stats with
        {
            Baselines = baselines,
            LaneMatchupHeroId = laneOpponent?.HeroId,
            LaneMatchupHeroName = laneOpponent is null ? null : names.Hero(laneOpponent.HeroId),
            LaneMatchupBaselines = matchupBaselines,
        };
    }

    // Our own reading of the replay (laning phase, timeline, deaths), for every match with a replay: OpenDota's
    // parse has none of them in the form we use. It never breaks the analysis: anything that goes wrong here only
    // changes the status. The analysis is returned only when the status is Ready.
    private async Task<(ReplayStatus Status, ReplayAnalysis? Analysis)> GetReplayAsync(
        MatchDetails match, CancellationToken cancellationToken)
    {
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

        // "Expired" saved too soon after the match ended may only mean Valve hadn't uploaded the replay yet
        // (ReplayAvailability): it is treated as a failure, tried again after FailedReplayRetryInterval.
        var state = saved is { State: SavedReplayState.Expired } && !ReplayAvailability.IsExpired(match, saved.SavedAt)
            ? SavedReplayState.Failed
            : saved?.State;

        switch (state)
        {
            case SavedReplayState.Ready when saved!.Analysis is { IsOutdated: false } analysis:
                return (ReplayStatus.Ready, analysis);
            case SavedReplayState.Ready when saved!.Analysis is { } outdated:
                // Read before everything the page shows existed (timeline, deaths): parsed again when possible;
                // until then, what we have.
                return match.ReplayUrl is not null
                    && replayParseQueue.TryEnqueue(match.MatchId, match.ReplayUrl)
                        is ReplayEnqueueResult.Enqueued or ReplayEnqueueResult.AlreadyQueued
                    ? (ReplayStatus.Processing, null)
                    : (ReplayStatus.Ready, outdated);
            case SavedReplayState.Expired:
                return (ReplayStatus.Expired, null);
            case SavedReplayState.Failed when timeProvider.GetUtcNow() - saved!.SavedAt < FailedReplayRetryInterval:
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
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("The replay parse queue is full; match {MatchId} was not queued", match.MatchId);
            }
        }

        return (notQueued, null);
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

}
