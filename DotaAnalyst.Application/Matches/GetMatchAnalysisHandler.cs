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
    IBaselineProvider baselineProvider,
    TimeProvider timeProvider,
    ILogger<GetMatchAnalysisHandler> logger)
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
        var currentPlayer = match.FindPlayer(accountId);

        IReadOnlyList<MatchPlayerSummary> TeamOf(Team team) => match.Players
            .Where(player => player.Team == team)
            .Select(player => ToSummary(player, ReferenceEquals(player, currentPlayer), names))
            .ToList();

        var (replayStatus, replay) = await GetReplayAsync(match, cancellationToken);

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
            Damage: replay?.Damage is { } damage && currentPlayer is not null ? ToDamage(damage, match, currentPlayer, names) : null,
            // Deaths read before the damage have no assists: no fights until the replay is read again.
            Fights: replay?.Deaths is { } fightDeaths && replay.Damage is not null ? ToFights(fightDeaths, match, currentPlayer) : null,
            ItemBuild: replay?.Purchases is { } purchases && currentPlayer is not null
                ? ToItemBuild(purchases, currentPlayer, itemsTask.Result)
                : null,
            Objectives: replay?.Objectives is { } objectives ? ToObjectives(objectives, currentPlayer, names) : null,
            SmartStats: replay?.Metrics is { } metrics && currentPlayer is not null
                ? await WithBaselinesAsync(ToSmartStats(metrics, currentPlayer, names), metrics, currentPlayer, match, cancellationToken)
                : null,
            Heatmaps: replay?.Heatmaps is { } heatmaps ? ToHeatmaps(heatmaps, match, currentPlayer, names) : null);
    }

    private static HeatmapSet ToHeatmaps(
        IReadOnlyList<HeroHeatmap> heatmaps, MatchDetails match, MatchPlayer? player, NameLookup names)
    {
        static IReadOnlyList<int> Flat(IReadOnlyList<HeatCell> cells) =>
            cells.SelectMany(cell => new[] { cell.Column, cell.Row, cell.Samples }).ToList();

        static PositionIndicatorsView? View(PositionIndicators? indicators) => indicators is null
            ? null
            : new PositionIndicatorsView(indicators.AliveSeconds, indicators.Advance, indicators.Coverage,
                indicators.Concentration, indicators.EnemySide, indicators.NearAlly, indicators.Grouped);

        static PhaseIndicators IndicatorsOf(HeroHeatmap heatmap, Team team) => new(
            View(HeatmapIndicators.Calculate(heatmap, team)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.EarlyGame)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.MidGame)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.LateGame)));

        var heroes = heatmaps
            .Select(heatmap => (heatmap, hero: match.Players.FirstOrDefault(candidate => candidate.HeroId == heatmap.HeroId)))
            .Where(entry => entry.hero is not null)
            .OrderBy(entry => entry.hero!.Team == Team.Radiant ? 0 : 1)
            .Select(entry => new HeroHeatmapView(
                HeroId: entry.heatmap.HeroId,
                HeroName: names.Hero(entry.heatmap.HeroId),
                Team: entry.hero!.Team,
                IsCurrentPlayer: player is not null && entry.heatmap.HeroId == player.HeroId,
                EarlyGame: Flat(entry.heatmap.Phases.EarlyGame),
                MidGame: Flat(entry.heatmap.Phases.MidGame),
                LateGame: Flat(entry.heatmap.Phases.LateGame),
                Indicators: IndicatorsOf(entry.heatmap, entry.hero.Team)))
            .ToList();

        // The average of the heroes that have the period (each indicator over the heroes that have it).
        static PositionIndicatorsView? Average(IReadOnlyList<PositionIndicatorsView?> all)
        {
            var present = all.OfType<PositionIndicatorsView>().ToList();
            if (present.Count == 0)
            {
                return null;
            }

            double? Mean(Func<PositionIndicatorsView, double?> value) =>
                present.Select(value).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var mean && double.IsNaN(mean)
                    ? null
                    : mean;
            return new PositionIndicatorsView(
                (int)Math.Round(present.Average(view => view.AliveSeconds)),
                Mean(view => view.Advance)!.Value,
                Mean(view => view.Coverage)!.Value,
                Mean(view => view.Concentration)!.Value,
                Mean(view => view.EnemySide)!.Value,
                Mean(view => view.NearAlly),
                Mean(view => view.Grouped));
        }

        var indicators = heroes.Select(hero => hero.Indicators!).ToList();
        var matchAverage = new PhaseIndicators(
            Average(indicators.Select(phases => phases.All).ToList()),
            Average(indicators.Select(phases => phases.EarlyGame).ToList()),
            Average(indicators.Select(phases => phases.MidGame).ToList()),
            Average(indicators.Select(phases => phases.LateGame).ToList()));
        return new HeatmapSet(
            HeroHeatmap.GridSize, HeroHeatmap.MapMin, HeroHeatmap.MapMax, HeatmapIndicators.SecondsPerSample, heroes, matchAverage);
    }

    // The player's metrics against other players of the same hero and role (pro and high-rank matches), from the
    // comparison base (IBaselineProvider): an extra, the analysis comes without it when it can't be reached.
    private async Task<SmartStats?> WithBaselinesAsync(
        SmartStats? stats, IReadOnlyList<HeroMetrics> metrics, MatchPlayer player, MatchDetails match,
        CancellationToken cancellationToken)
    {
        if (stats is null || metrics.FirstOrDefault(hero => hero.HeroId == player.HeroId) is not { } mine)
        {
            return stats;
        }

        var baselines = await baselineProvider.CompareAsync(mine, (int)match.Duration.TotalSeconds, cancellationToken);
        return baselines is null ? stats : stats with { Baselines = baselines };
    }

    // Null without metrics for the player or an enemy of the same role, and for a match without role queue (its
    // roles are only inferred: good for the comparison base, not to judge the player's own match — Turbo is left out).
    private static SmartStats? ToSmartStats(IReadOnlyList<HeroMetrics> metrics, MatchPlayer player, NameLookup names)
    {
        if (metrics.FirstOrDefault(hero => hero.HeroId == player.HeroId) is not { RoleInferred: false } mine
            || metrics.FirstOrDefault(hero => hero.Team != mine.Team && hero.Role == mine.Role) is not { } counterpart)
        {
            return null;
        }

        // Over the heroes that have the number (no net worth at 20:00 in a shorter match).
        static double? Average(IEnumerable<int?> values)
        {
            var known = values.OfType<int>().ToList();
            return known.Count == 0 ? null : known.Average();
        }
        static double? AverageKnown(IEnumerable<double?> values)
        {
            var known = values.OfType<double>().ToList();
            return known.Count == 0 ? null : known.Average();
        }

        var matchAverage = new MetricsSummary(
            Deaths: metrics.Average(hero => hero.Deaths),
            IsolatedDeaths: metrics.Average(hero => hero.IsolatedDeaths),
            DeathsOnEnemySide: metrics.Average(hero => hero.DeathsOnEnemySide),
            EnemySideShare: metrics.Average(hero => hero.EnemySideShare),
            AloneOnEnemySideShare: metrics.Average(hero => hero.AloneOnEnemySideShare),
            NetWorthAt10: Average(metrics.Select(hero => hero.NetWorthAt10)),
            NetWorthAt20: Average(metrics.Select(hero => hero.NetWorthAt20)),
            LastHitsAt10: metrics.Average(hero => hero.LastHitsAt10),
            NetWorthShare: metrics.Average(hero => hero.NetWorthShare),
            ObserversPlaced: metrics.Average(hero => hero.ObserversPlaced),
            SentriesPlaced: metrics.Average(hero => hero.SentriesPlaced),
            ObserverAverageLifetimeSeconds: Average(metrics.Select(hero => hero.ObserverAverageLifetimeSeconds)),
            WardsLostToEnemy: metrics.Average(hero => hero.WardsLostToEnemy),
            EnemyWardsDestroyed: metrics.Average(hero => hero.EnemyWardsDestroyed),
            // Each hero's own/enemy regions: "own jungle" averages every hero's own jungle.
            TimeByRegion: AverageByRegion(metrics.Select(hero => hero.TimeByRegion)),
            DeathsByRegion: AverageByRegion(metrics.Select(hero => hero.DeathsByRegion?.Select(count => (double)count))),
            EstimatedMovementDistance: AverageKnown(metrics.Select(hero => hero.EstimatedMovementDistance)),
            MovementSampleSegments: AverageKnown(metrics.Select(hero =>
                hero.MovementSampleSegments is { } count ? (double?)count : null)),
            EstimatedMovementByRegion: AverageKnownByRegion(metrics.Select(hero => hero.EstimatedMovementByRegion)),
            TimeByRegionByPhase: AverageByPhase(metrics.Select(hero => hero.TimeByRegionByPhase)),
            DeathsByRegionByPhase: AverageByPhase(metrics.Select(hero =>
                hero.DeathsByRegionByPhase?.Select(regionCounts => regionCounts.Select(count => (double)count)))),
            TeamFights: AverageKnown(metrics.Select(hero => (double?)hero.TeamFights)),
            FightsNearby: AverageKnown(metrics.Select(hero => (double?)hero.FightsNearby)),
            KillParticipation: AverageKnown(metrics.Select(hero => hero.KillParticipation)),
            QuickRedeaths: AverageKnown(metrics.Select(hero => (double?)hero.QuickRedeaths)),
            LaneDamageDealt: AverageKnown(metrics.Select(hero => (double?)hero.LaneDamageDealt)),
            LaneDamageTaken: AverageKnown(metrics.Select(hero => (double?)hero.LaneDamageTaken)),
            TeamObserverUptime: AverageKnown(metrics.Select(hero => hero.TeamObserverUptime)),
            LeadLostPerDeath: AverageKnown(metrics.Select(hero => hero.LeadLostPerDeath)),
            NetWorthGainByRegion: AverageKnownByRegion(metrics.Select(hero => hero.NetWorthGainByRegion)),
            MinutesByRegion: AverageKnownByRegion(metrics.Select(hero => hero.MinutesByRegion?.Select(count => (double)count))));

        return new SmartStats(
            Role: mine.Role,
            CounterpartHeroId: counterpart.HeroId,
            CounterpartHeroName: names.Hero(counterpart.HeroId),
            Player: ToSummary(mine),
            Counterpart: ToSummary(counterpart),
            MatchAverage: matchAverage,
            Insights: InsightGenerator.Generate(mine, counterpart)
                .Select(insight => new InsightSummary(insight.Kind, insight.Value, insight.Counterpart))
                .ToList());
    }

    private static MetricsSummary ToSummary(HeroMetrics hero) =>
        new(hero.Deaths, hero.IsolatedDeaths, hero.DeathsOnEnemySide, hero.EnemySideShare, hero.AloneOnEnemySideShare,
            hero.NetWorthAt10, hero.NetWorthAt20, hero.LastHitsAt10, hero.NetWorthShare, hero.ObserversPlaced,
            hero.SentriesPlaced, hero.ObserverAverageLifetimeSeconds, hero.WardsLostToEnemy, hero.EnemyWardsDestroyed,
            hero.TimeByRegion is { } time ? ToRegionValues(time) : null,
            hero.DeathsByRegion is { } deaths ? ToRegionValues(deaths.Select(count => (double)count)) : null,
            hero.EstimatedMovementDistance,
            hero.MovementSampleSegments,
            hero.EstimatedMovementByRegion is { } movement ? ToRegionValues(movement) : null,
            hero.TimeByRegionByPhase is { } timeByPhase ? ToRegionPhaseValues(timeByPhase) : null,
            hero.DeathsByRegionByPhase is { } deathsByPhase
                ? ToRegionPhaseValues(deathsByPhase.Select(regionCounts => regionCounts.Select(count => (double)count)))
                : null,
            hero.TeamFights, hero.FightsNearby, hero.KillParticipation, hero.QuickRedeaths, hero.LaneDamageDealt,
            hero.LaneDamageTaken, hero.TeamObserverUptime, hero.LeadLostPerDeath,
            hero.NetWorthGainByRegion is { } gain ? ToRegionValues(gain) : null,
            hero.MinutesByRegion is { } minutes ? ToRegionValues(minutes.Select(count => (double)count)) : null);

    private static RegionValues ToRegionValues(PerRegion<double> values) =>
        new(values.OwnBase, values.EnemyBase, values.TopLane, values.MiddleLane, values.BottomLane, values.River,
            values.OwnJungle, values.EnemyJungle);

    // Null when any hero lacks the regions (metrics computed before them).
    private static RegionValues? AverageByRegion(IEnumerable<PerRegion<double>?> heroes)
    {
        var all = heroes.ToList();
        if (all.Count == 0 || all.Any(hero => hero is null))
        {
            return null;
        }

        return ToRegionValues(PerRegion<double>.Of(region => all.Average(hero => hero![region])));
    }

    private static RegionValues? AverageKnownByRegion(IEnumerable<PerRegion<double>?> heroes)
    {
        var known = heroes.OfType<PerRegion<double>>().ToList();
        return known.Count == 0
            ? null
            : ToRegionValues(PerRegion<double>.Of(region => known.Average(hero => hero[region])));
    }

    private static RegionPhaseValues ToRegionPhaseValues(PerGamePhase<PerRegion<double>> values) =>
        new(ToRegionValues(values.EarlyGame), ToRegionValues(values.MidGame), ToRegionValues(values.LateGame));

    private static RegionPhaseValues? AverageByPhase(IEnumerable<PerGamePhase<PerRegion<double>>?> heroes)
    {
        var all = heroes.ToList();
        if (all.Count == 0 || all.Any(hero => hero is null))
        {
            return null;
        }

        return new(
            AverageByRegion(all.Select(hero => hero!.EarlyGame))!,
            AverageByRegion(all.Select(hero => hero!.MidGame))!,
            AverageByRegion(all.Select(hero => hero!.LateGame))!);
    }

    private static IReadOnlyList<MapObjectiveSummary> ToObjectives(
        IReadOnlyList<MapObjective> objectives, MatchPlayer? player, NameLookup names) =>
        objectives
            .Select(objective => new MapObjectiveSummary(
                Kind: objective.Kind,
                Seconds: objective.Seconds,
                TakenBy: objective.TakenBy,
                IsPlayersTeam: player is null ? null : objective.TakenBy == player.Team,
                Owner: objective.Owner,
                TowerTier: objective.TowerTier,
                Lane: objective.Lane,
                MeleeBarracks: objective.MeleeBarracks,
                Denied: objective.Denied,
                KillerHeroName: objective.KillerHeroId is { } killer ? names.Hero(killer) : null,
                AegisHeroName: objective.AegisHeroId is { } aegis ? names.Hero(aegis) : null))
            .ToList();

    // Null when the catalog has no details (an old saved copy with OpenDota down): without components there is no
    // assembly to redo.
    private static PlayerItemBuild? ToItemBuild(
        IReadOnlyList<ItemPurchase> purchases, MatchPlayer player, IReadOnlyDictionary<int, Item> items)
    {
        var byKey = items.Values
            .Where(item => item.Details is not null)
            .DistinctBy(item => item.Details!.Key)
            .ToDictionary(item => item.Details!.Key);
        if (byKey.Count == 0)
        {
            return null;
        }

        var build = ItemBuildEvaluator.Evaluate(
            purchases.Where(purchase => purchase.HeroId == player.HeroId),
            byKey.ToDictionary(pair => pair.Key, pair => pair.Value.Details!));

        return new PlayerItemBuild(
            // An item from a newer patch than the catalog has no name to show: left out.
            StartingItems: build.StartingItems
                .Where(item => byKey.ContainsKey(item.ItemKey))
                .Select(item => new StartingItemCount(byKey[item.ItemKey].Id, byKey[item.ItemKey].Name, item.Count))
                .ToList(),
            Items: build.Items
                .Select(item => new TimedItem(byKey[item.ItemKey].Id, byKey[item.ItemKey].Name, item.Seconds, item.Assembled))
                .ToList());
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
            logger.LogInformation("The replay parse queue is full; match {MatchId} was not queued", match.MatchId);
        }

        return (notQueued, null);
    }

    private static PlayerDamage ToDamage(
        IReadOnlyList<DamageRow> damage, MatchDetails match, MatchPlayer player, NameLookup names)
    {
        var enemies = match.Players.Where(other => other.Team != player.Team).Select(other => other.HeroId).ToHashSet();

        IReadOnlyList<HeroAmount> ByHero(IEnumerable<(int HeroId, int Amount)> rows) => rows
            .GroupBy(row => row.HeroId)
            .Select(group => new HeroAmount(group.Key, names.Hero(group.Key), group.Sum(row => row.Amount)))
            .OrderByDescending(hero => hero.Amount)
            .ToList();

        // Only damage taken by the heroes themselves; the source may be the hero, its illusions or its summons.
        var dealt = damage
            .Where(row => row.SourceHeroId == player.HeroId && row.TargetIsHero && enemies.Contains(row.TargetHeroId!.Value))
            .ToList();
        return new PlayerDamage(
            ToHeroes: ByHero(dealt.Select(row => (row.TargetHeroId!.Value, row.Amount))),
            FromHeroes: ByHero(damage
                .Where(row => row.TargetHeroId == player.HeroId && row.TargetIsHero
                    && row.SourceHeroId is { } source && enemies.Contains(source))
                .Select(row => (row.SourceHeroId!.Value, row.Amount))),
            ByInflictor: dealt
                .GroupBy(row => row.Inflictor)
                .Select(group => names.ItemOfInflictor(group.Key) is { } item
                    ? new InflictorAmount(group.Key, group.Sum(row => row.Amount), item.Id, item.Name)
                    : new InflictorAmount(group.Key, group.Sum(row => row.Amount)))
                .OrderByDescending(inflictor => inflictor.Amount)
                .ToList());
    }

    private static IReadOnlyList<FightSummary> ToFights(
        IReadOnlyList<HeroDeath> deaths, MatchDetails match, MatchPlayer? player)
    {
        // DistinctBy: a custom game with the same hero twice must not break the analysis.
        var teamOf = match.Players.DistinctBy(other => other.HeroId).ToDictionary(other => other.HeroId, other => other.Team);
        return TeamfightDetector.Detect(deaths, teamOf)
            .Select(fight => new FightSummary(
                StartSeconds: fight.StartSeconds,
                EndSeconds: fight.EndSeconds,
                RadiantDeaths: fight.RadiantDeaths,
                DireDeaths: fight.DireDeaths,
                Winner: fight.Winner,
                PlayersTeamWon: player is null || fight.Winner is null ? null : fight.Winner == player.Team,
                Died: player is not null && fight.Deaths.Any(death => death.HeroId == player.HeroId),
                Kills: player is null ? 0 : fight.Deaths.Count(death => death.KillerHeroId == player.HeroId),
                Assists: player is null ? 0 : fight.Deaths.Count(death => death.AssistHeroIds.Contains(player.HeroId)),
                X: (int)Math.Round(fight.Deaths.Average(death => death.X)),
                Y: (int)Math.Round(fight.Deaths.Average(death => death.Y))))
            .ToList();
    }

    private static IReadOnlyList<MapWard> ToWards(
        IReadOnlyList<WardPlacement> wards, MatchPlayer? player, NameLookup names) =>
        wards
            .Select(ward => new MapWard(
                Kind: ward.Kind,
                Team: ward.Team,
                IsPlayersTeam: player is null ? null : ward.Team == player.Team,
                PlacedByHeroName: ward.PlacedByHeroId is { } placedBy ? names.Hero(placedBy) : null,
                X: ward.X,
                Y: ward.Y,
                PlacedAt: ward.PlacedAt,
                RemovedAt: ward.RemovedAt,
                Destroyed: ward.Destroyed,
                DestroyedByHeroName: ward.DestroyedByHeroId is { } destroyedBy ? names.Hero(destroyedBy) : null,
                InLaningPhase: ward.InLaningPhase))
            .ToList();

    // The point-of-view player's deaths, in time order.
    private static IReadOnlyList<PlayerDeath> ToDeaths(
        IReadOnlyList<HeroDeath> deaths, MatchPlayer player, NameLookup names) =>
        deaths
            .Where(death => death.HeroId == player.HeroId)
            .Select(death => new PlayerDeath(
                Seconds: death.Seconds,
                X: death.X,
                Y: death.Y,
                Killer: death.Killer,
                KillerHeroId: death.KillerHeroId,
                KillerHeroName: death.KillerHeroId is { } heroId ? names.Hero(heroId) : null,
                Reincarnated: death.Reincarnated,
                InLaningPhase: death.InLaningPhase))
            .ToList();

    private static MatchTimelineChart ToTimelineChart(
        ReplayAnalysis replay, MatchTimeline timeline, MatchPlayer? player, NameLookup names)
    {
        var perspective = player?.Team ?? Team.Radiant;
        var lead = TimelineEvaluator.LeadOf(timeline, perspective);

        // The lane chart shows the same heroes as the laning phase: the player, allies and opponents of their lane.
        var lane = new List<TimelineHero>();
        if (player is not null && LaneOutcomeEvaluator.Evaluate(replay, player.HeroId) is { } evaluation)
        {
            void Add(LaningStats stats, LaneSide side)
            {
                if (timeline.FindHero(stats.HeroId) is { } hero)
                {
                    lane.Add(new TimelineHero(hero.HeroId, names.Hero(hero.HeroId), side, hero.NetWorth, hero.Experience));
                }
            }

            Add(evaluation.Player, LaneSide.Player);
            evaluation.Allies.ToList().ForEach(ally => Add(ally, LaneSide.Ally));
            evaluation.Opponents.ToList().ForEach(opponent => Add(opponent, LaneSide.Opponent));
        }

        return new MatchTimelineChart(timeline.Seconds, perspective, lead.NetWorth, lead.Experience, lane);
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
            NeutralItem: player.NeutralItemId is { } neutralItemId ? names.Item(neutralItemId) : null,
            ItemIds: player.ItemIds,
            NeutralItemId: player.NeutralItemId);

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
        // Items by internal key (blink, dagon_5), built on first use: only the damage section needs it.
        private Dictionary<string, Item>? byKey;

        public string Hero(int heroId) => heroes.GetValueOrDefault(heroId)?.Name ?? UnknownHeroName;

        public string Item(int itemId) => items.GetValueOrDefault(itemId)?.Name ?? UnknownItemName;

        // The item behind a damage inflictor ("item_dagon_5"); null for abilities, basic attacks and items the catalog
        // doesn't know (or has no details for).
        public Item? ItemOfInflictor(string? inflictor)
        {
            const string Prefix = "item_";
            if (inflictor is null || !inflictor.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            byKey ??= items.Values
                .Where(item => item.Details is not null)
                .DistinctBy(item => item.Details!.Key)
                .ToDictionary(item => item.Details!.Key);
            return byKey.GetValueOrDefault(inflictor[Prefix.Length..]);
        }
    }
}
