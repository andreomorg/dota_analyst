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
                : null);
    }

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
                .Select(group => new InflictorAmount(group.Key, group.Sum(row => row.Amount)))
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
