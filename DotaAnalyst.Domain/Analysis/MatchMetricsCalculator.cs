using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Domain service: the HeroMetrics of every hero of a match, from what the replay has (positions every few seconds,
// deaths, wards, the timeline and the laning numbers).
public static class MatchMetricsCalculator
{
    // How far past the river (the diagonal x + y = 0, in map units) the enemy's side of the map starts.
    public const int RiverMargin = 1000;
    public const int EarlyGameEndsAtSeconds = 600;
    public const int MidGameEndsAtSeconds = 1200;

    // An ally hero this close (map units) counts as nearby.
    public const int AllyRadius = 1500;

    // Positions come every 2 s, so any second has a sample at most 1 s away while the hero is alive; a wider window
    // would count an ally who had just died as still there.
    private const int SampleTolerance = 1;
    private const int MaxMovementSampleGapSeconds = 2;

    // Max movement speed is 550 units/s: more than this between two samples 2 s apart is a teleport, Blink or
    // respawn jump, not walking, and is left out of the distance.
    public const int MaxWalkPerSample = 1200;

    // roles: role-queue position (1-5) of each hero; null when the match has no role queue (Turbo, unranked) or a
    // hero lacks it: then there are no metrics (they are compared by role).
    public static IReadOnlyList<HeroMetrics>? Calculate(
        IReadOnlyList<LaningStats> players,
        MatchTimeline timeline,
        IReadOnlyList<HeroDeath> deaths,
        IReadOnlyList<WardPlacement> wards,
        IReadOnlyList<HeroPositions> positions,
        IReadOnlyDictionary<int, int>? roles,
        bool rolesInferred = false,
        IReadOnlyList<HeroDamageMinutes>? damageMinutes = null,
        Maps.GameMap? map = null,
        TreeTimeline? trees = null,
        IReadOnlyList<MapObjective>? objectives = null,
        IReadOnlyList<ObjectivePlace>? objectivePlaces = null,
        IReadOnlyDictionary<int, MapObjectiveCounts>? objectiveCounts = null,
        IReadOnlyList<(int HeroId, int Seconds)>? wisdomPickups = null,
        IReadOnlyList<Buff>? buffs = null,
        ReplayActivity? activity = null,
        IReadOnlyDictionary<int, IReadOnlyCollection<string>>? kits = null)
    {
        if (roles is null || players.Any(player => roles.GetValueOrDefault(player.HeroId) is < 1 or > 5))
        {
            return null;
        }

        var positionsOf = positions.DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
        var matchEnd = timeline.Seconds[^1];
        var teamOf = players.DistinctBy(player => player.HeroId).ToDictionary(player => player.HeroId, player => player.Team);
        var fights = TeamfightDetector.Detect(deaths, teamOf);
        var damageOf = (damageMinutes ?? []).DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
        // Each Wisdom Shrine taken, with the side of the shrine by where the hero stood (needs the patch's map).
        var wisdom = map is null || wisdomPickups is null
            ? null
            : wisdomPickups.Where(pickup => teamOf.ContainsKey(pickup.HeroId)).Select(pickup => new WisdomTake(
                pickup.HeroId, teamOf[pickup.HeroId], pickup.Seconds,
                positionsOf.GetValueOrDefault(pickup.HeroId)?.At(pickup.Seconds, MapObjectiveMetricsCalculator.SampleTolerance) is { } at
                    ? MapObjectiveMetricsCalculator.ShrineSideAt(map, at.X, at.Y)
                    : null)).ToList();

        return players.Select(player =>
        {
            var allies = players
                .Where(other => other.Team == player.Team && other.HeroId != player.HeroId)
                .Select(other => positionsOf.GetValueOrDefault(other.HeroId))
                .OfType<HeroPositions>()
                .ToList();
            bool AllyNear(int seconds, int x, int y) => allies.Any(ally =>
                ally.At(seconds, SampleTolerance) is { } sample && Distance(sample.X, sample.Y, x, y) <= AllyRadius);

            var heroDeaths = deaths.Where(death => death.HeroId == player.HeroId).ToList();
            var samples = positionsOf.GetValueOrDefault(player.HeroId)?.Samples.Where(sample => sample.Seconds >= 0).ToList() ?? [];
            var onEnemySide = samples.Where(sample => IsEnemySide(player.Team, sample.X, sample.Y)).ToList();
            var alone = onEnemySide.Count(sample => !AllyNear(sample.Seconds, sample.X, sample.Y));
            // Each point classified once, then counted per region.
            var timeRegions = samples
                .GroupBy(sample => MapRegions.RegionOf(player.Team, sample.X, sample.Y))
                .ToDictionary(group => group.Key, group => group.Count());
            var deathRegions = heroDeaths
                .GroupBy(death => MapRegions.RegionOf(player.Team, death.X, death.Y))
                .ToDictionary(group => group.Key, group => group.Count());
            var phaseTimeCounts = CountRegionsByPhase(samples.Select(sample =>
                (sample.Seconds, sample.X, sample.Y)), player.Team);
            var phaseDeathCounts = CountRegionsByPhase(heroDeaths.Select(death =>
                (death.Seconds, death.X, death.Y)), player.Team);
            // Samples alive in each phase, counted once (not once per region).
            var phaseTotals = Enumerable.Range(0, 3).Select(phase => samples.Count(sample => IsInPhase(sample.Seconds, phase))).ToArray();
            var phaseTimeShares = new PerGamePhase<PerRegion<double>>(
                TimeShares(phaseTimeCounts.EarlyGame, 0),
                TimeShares(phaseTimeCounts.MidGame, 1),
                TimeShares(phaseTimeCounts.LateGame, 2));

            PerRegion<double> TimeShares(PerRegion<int> counts, int phase) =>
                PerRegion<double>.Of(region => Share(counts[region], phaseTotals[phase]));

            var movement = EstimateMovement(player.Team, samples);
            var heroPositions = positionsOf.GetValueOrDefault(player.HeroId);
            var farm = timeline.FindHero(player.HeroId) is { } heroSeries ? FarmByRegion(heroSeries, timeline, samples, player.Team) : ((PerRegion<double> Gain, PerRegion<int> Minutes)?)null;
            var fightsNearby = fights.Count(fight => WasNear(heroPositions, fight));
            var enemyDeaths = deaths.Where(death => teamOf.GetValueOrDefault(death.HeroId) != player.Team).ToList();
            var kills = enemyDeaths.Count(death => death.KillerHeroId == player.HeroId || death.AssistHeroIds.Contains(player.HeroId));

            var series = timeline.FindHero(player.HeroId);
            var teamNetWorth = timeline.Heroes.Where(hero => hero.Team == player.Team).Sum(hero => hero.NetWorth[^1]);

            var placed = wards.Where(ward => ward.PlacedByHeroId == player.HeroId).ToList();
            var observers = placed.Where(ward => ward.Kind == WardKind.Observer).ToList();

            return new HeroMetrics(
                HeroId: player.HeroId,
                Team: player.Team,
                Role: roles[player.HeroId],
                Deaths: heroDeaths.Count,
                IsolatedDeaths: heroDeaths.Count(death => !AllyNear(death.Seconds, death.X, death.Y)),
                DeathsOnEnemySide: heroDeaths.Count(death => IsEnemySide(player.Team, death.X, death.Y)),
                EnemySideShare: Share(onEnemySide.Count, samples.Count),
                AloneOnEnemySideShare: Share(alone, samples.Count),
                NetWorthAt10: series is null ? null : ValueAt(timeline, series.NetWorth, 600),
                NetWorthAt20: series is null ? null : ValueAt(timeline, series.NetWorth, 1200),
                LastHitsAt10: player.LastHits,
                NetWorthShare: series is null ? 0 : Share(series.NetWorth[^1], teamNetWorth),
                ObserversPlaced: observers.Count,
                SentriesPlaced: placed.Count(ward => ward.Kind == WardKind.Sentry),
                ObserverAverageLifetimeSeconds: observers.Count == 0
                    ? null
                    : (int)Math.Round(observers.Average(ward => (ward.RemovedAt ?? matchEnd) - ward.PlacedAt)),
                WardsLostToEnemy: placed.Count(ward => ward.Destroyed),
                EnemyWardsDestroyed: wards.Count(ward => ward.Team != player.Team && ward.DestroyedByHeroId == player.HeroId),
                TimeByRegion: PerRegion<double>.Of(region => Share(timeRegions.GetValueOrDefault(region), samples.Count)),
                DeathsByRegion: PerRegion<int>.Of(region => deathRegions.GetValueOrDefault(region)),
                RoleInferred: rolesInferred,
                EstimatedMovementDistance: movement.Distance,
                EstimatedMovementByRegion: movement.ByRegion,
                MovementSampleSegments: movement.SampleSegments,
                TimeByRegionByPhase: phaseTimeShares,
                DeathsByRegionByPhase: phaseDeathCounts,
                TeamFights: fights.Count,
                FightsNearby: fightsNearby,
                KillParticipation: enemyDeaths.Count == 0 ? null : (double)kills / enemyDeaths.Count,
                QuickRedeaths: QuickRedeaths(heroDeaths, heroPositions),
                LaneDamageDealt: damageOf.TryGetValue(player.HeroId, out var dealtMinutes) ? dealtMinutes.ToHeroes.Take(LaningMinutes).Sum() : null,
                LaneDamageTaken: damageOf.TryGetValue(player.HeroId, out var takenMinutes) ? takenMinutes.FromHeroes.Take(LaningMinutes).Sum() : null,
                TeamObserverUptime: ObserverUptime(wards, player.Team, matchEnd),
                LeadLostPerDeath: LeadLostPerDeath(heroDeaths, timeline, player.Team),
                NetWorthGainByRegion: farm?.Gain,
                MinutesByRegion: farm?.Minutes,
                Terrain: map is null ? null : TerrainMetricsCalculator.Calculate(map, player.Team, samples, heroDeaths, trees),
                Vision: VisionMetricsCalculator.Calculate(player.HeroId, positionsOf, deaths, wards, map, trees),
                Objectives: objectives is null ? null : MapObjectiveMetricsCalculator.Calculate(player.Team, heroPositions,
                    objectives, objectivePlaces, objectiveCounts?.GetValueOrDefault(player.HeroId), wisdom, heroDeaths),
                Teleports: TeleportMetricsCalculator.Calculate(player.HeroId, buffs, heroPositions, fights, heroDeaths),
                Plays: activity is null ? null : PlayQualityMetricsCalculator.Calculate(player.HeroId, activity, teamOf, positionsOf,
                    deaths, matchEnd, kits?.GetValueOrDefault(player.HeroId)));
        }).ToList();
    }

    // How close to a fight's center (average of its deaths) counts as being in it, and how long before the first death
    // the hero may arrive.
    public const int FightPresenceRadius = 2500;
    public const int FightLeadInSeconds = 5;

    // A death this soon after coming back from the previous one (the first position after it) is a quick re-death.
    public const int QuickRedeathSeconds = 90;

    // Minutes 0-9 of the game clock: the laning phase (ReplayAnalysis.LaningPhaseEndsAtSeconds).
    private const int LaningMinutes = ReplayAnalysis.LaningPhaseEndsAtSeconds / 60;

    private static bool WasNear(HeroPositions? hero, Teamfight fight)
    {
        if (hero is null)
        {
            return false;
        }

        var centerX = fight.Deaths.Average(death => death.X);
        var centerY = fight.Deaths.Average(death => death.Y);
        return hero.Samples.Any(sample =>
            sample.Seconds >= fight.StartSeconds - FightLeadInSeconds && sample.Seconds <= fight.EndSeconds
            && Math.Sqrt(Math.Pow(sample.X - centerX, 2) + Math.Pow(sample.Y - centerY, 2)) <= FightPresenceRadius);
    }

    // Back from a death = the first position sample after it (a dead hero has none); Aegis deaths come back at once.
    private static int QuickRedeaths(IReadOnlyList<HeroDeath> heroDeaths, HeroPositions? hero)
    {
        var ordered = heroDeaths.OrderBy(death => death.Seconds).ToList();
        var quick = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var died = ordered[i - 1].Seconds;
            // PositionSample is a struct: FirstOrDefault would give a sample at 0:00, not "none".
            int? back = ordered[i - 1].Reincarnated
                ? died
                : hero?.Samples.Where(sample => sample.Seconds > died + 2).Select(sample => (int?)sample.Seconds).FirstOrDefault();
            if (back is { } seconds && ordered[i].Seconds - seconds <= QuickRedeathSeconds)
            {
                quick++;
            }
        }

        return quick;
    }

    // Each full minute (timeline points i and i + 1) goes to the region the hero spent most of it in.
    private static (PerRegion<double> Gain, PerRegion<int> Minutes) FarmByRegion(
        HeroTimeline series, MatchTimeline timeline, IReadOnlyList<PositionSample> samples, Team team)
    {
        var gain = new Dictionary<MapRegion, double>();
        var minutes = new Dictionary<MapRegion, int>();
        var bySecond = samples.ToLookup(sample => sample.Seconds / 60);
        // The last point is the end of the match, not a full minute after the one before.
        for (var minute = 0; minute + 1 < timeline.Seconds.Count - 1; minute++)
        {
            var main = bySecond[minute]
                .GroupBy(sample => MapRegions.RegionOf(team, sample.X, sample.Y))
                .OrderByDescending(group => group.Count())
                .Select(group => (MapRegion?)group.Key)
                .FirstOrDefault();
            if (main is not { } region)
            {
                continue;
            }

            gain[region] = gain.GetValueOrDefault(region) + series.NetWorth[minute + 1] - series.NetWorth[minute];
            minutes[region] = minutes.GetValueOrDefault(region) + 1;
        }

        return (PerRegion<double>.Of(region => gain.GetValueOrDefault(region)), PerRegion<int>.Of(region => minutes.GetValueOrDefault(region)));
    }

    // Union of the team's observers' lifetimes (placed .. removed, or the end), from 0:00 to the end, over the match.
    private static double ObserverUptime(IReadOnlyList<WardPlacement> wards, Team team, int matchEnd)
    {
        if (matchEnd <= 0)
        {
            return 0;
        }

        var intervals = wards
            .Where(ward => ward.Team == team && ward.Kind == WardKind.Observer)
            .Select(ward => (Start: Math.Max(0, ward.PlacedAt), End: Math.Min(matchEnd, ward.RemovedAt ?? matchEnd)))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ToList();

        var covered = 0;
        var reached = 0;
        foreach (var (start, end) in intervals)
        {
            var from = Math.Max(start, reached);
            if (end > from)
            {
                covered += end - from;
            }

            reached = Math.Max(reached, end);
        }

        return (double)covered / matchEnd;
    }

    // The team's lead (its net worth minus the other team's) at the minute of each death and at the next minute.
    private static double? LeadLostPerDeath(IReadOnlyList<HeroDeath> heroDeaths, MatchTimeline timeline, Team team)
    {
        int Lead(int point) => timeline.Heroes.Sum(hero => hero.Team == team ? hero.NetWorth[point] : -hero.NetWorth[point]);

        var losses = heroDeaths
            .Where(death => !death.Reincarnated && death.Seconds >= 0)
            .Select(death => death.Seconds / 60)
            // Timeline point i is minute i (the last point is the end of the match, not a full minute later).
            .Where(minute => minute + 1 < timeline.Seconds.Count - 1)
            .Select(minute => (double)(Lead(minute) - Lead(minute + 1)))
            .ToList();
        return losses.Count == 0 ? null : losses.Average();
    }

    private static PerGamePhase<PerRegion<int>> CountRegionsByPhase(IEnumerable<(int Seconds, int X, int Y)> positions, Team team)
    {
        var counts = positions
            .Where(position => position.Seconds >= 0)
            .GroupBy(position => (Phase: PhaseOf(position.Seconds), Region: MapRegions.RegionOf(team, position.X, position.Y)))
            .ToDictionary(group => group.Key, group => group.Count());

        PerRegion<int> ForPhase(int phase) =>
            PerRegion<int>.Of(region => counts.GetValueOrDefault((phase, region)));

        return new(ForPhase(0), ForPhase(1), ForPhase(2));
    }

    private static bool IsInPhase(int seconds, int phase) => seconds >= 0 && PhaseOf(seconds) == phase;

    private static int PhaseOf(int seconds) => seconds < EarlyGameEndsAtSeconds
        ? 0
        : seconds < MidGameEndsAtSeconds ? 1 : 2;

    private static (double? Distance, PerRegion<double>? ByRegion, int SampleSegments) EstimateMovement(
        Team team, IReadOnlyList<PositionSample> samples)
    {
        var distinctSamples = samples.DistinctBy(sample => sample.Seconds).ToList();
        var distances = new Dictionary<MapRegion, double>();
        var total = 0d;
        var segments = 0;

        for (var index = 1; index < distinctSamples.Count; index++)
        {
            var previous = distinctSamples[index - 1];
            var current = distinctSamples[index];
            if (current.Seconds - previous.Seconds is <= 0 or > MaxMovementSampleGapSeconds)
            {
                continue;
            }

            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (distance > MaxWalkPerSample)
            {
                continue;
            }

            var midpointX = previous.X + dx / 2;
            var midpointY = previous.Y + dy / 2;
            var region = MapRegions.RegionOf(team, midpointX, midpointY);

            total += distance;
            distances[region] = distances.GetValueOrDefault(region) + distance;
            segments++;
        }

        return segments == 0
            ? (null, null, 0)
            : (total, PerRegion<double>.Of(region => distances.GetValueOrDefault(region)), segments);
    }

    // More than RiverMargin past the river, on the other team's side (Radiant's side is x + y < 0).
    public static bool IsEnemySide(Team team, int x, int y)
    {
        var pastRiver = (x + y) / Math.Sqrt(2);
        return team == Team.Radiant ? pastRiver > RiverMargin : pastRiver < -RiverMargin;
    }

    private static double Distance(int x1, int y1, int x2, int y2) => Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private static double Share(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

    private static int? ValueAt(MatchTimeline timeline, IReadOnlyList<int> values, int seconds)
    {
        var index = timeline.Seconds.ToList().IndexOf(seconds);
        return index >= 0 ? values[index] : null;
    }
}
