using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// Where the hero stands against the creep waves (ReplayActivity.Waves: each team's front per lane, x + y).
public sealed partial class HeroSituationBuilder
{
    // Waves are sampled every 2 s, only when the lane has creeps: the reading at this second or the one before.
    private WaveFront? FrontAt(Lane lane, int seconds) =>
        wavesAt is null ? null
            : wavesAt.GetValueOrDefault((lane, seconds)) ?? wavesAt.GetValueOrDefault((lane, seconds - 1))
              ?? wavesAt.GetValueOrDefault((lane, seconds - 2));

    private static int? OwnFront(Team team, WaveFront? front) =>
        team == Team.Radiant ? front?.Radiant : front?.Dire is { } dire ? -dire : null;

    private static int? EnemyFront(Team team, WaveFront? front) =>
        team == Team.Radiant ? front?.Dire : front?.Radiant is { } radiant ? -radiant : null;

    private static Lane? LaneOf(Team team, PositionSample at) => MapRegions.RegionOf(team, at.X, at.Y) switch
    {
        MapRegion.TopLane => Lane.Top,
        MapRegion.MiddleLane => Lane.Middle,
        MapRegion.BottomLane => Lane.Bottom,
        _ => null,
    };

    private WavePosition WavesFor(int heroId, Team team, PositionSample self, int seconds)
    {
        if (wavesAt is null)
        {
            return WavePosition.Unknown;
        }

        var lane = LaneOf(team, self);
        var here = lane is { } current ? FrontAt(current, seconds) : null;
        var ownFront = OwnFront(team, here);
        var counts = here?.Counts;
        var (ownWave, enemyWave) = here?.Composition is { } composition
            ? team == Team.Radiant ? (composition.Radiant, composition.Dire) : (composition.Dire, composition.Radiant)
            : ((TeamWave?)null, (TeamWave?)null);
        int? clash = ownFront is { } own && EnemyFront(team, here) is { } enemy ? (own + enemy) / 2 : null;
        int? Change(int ago) => lane is { } inLane && ownFront is { } now && OwnFront(team, FrontAt(inLane, seconds - ago)) is { } before
            ? now - before
            : null;
        var (ownTower, enemyTower) = lane is { } towersLane ? LaneTowers(team, towersLane, seconds) : (null, null);
        return new WavePosition(
            AheadOfOwnWave: ownFront is { } front ? FromOwnSide(team, self.X + self.Y) - front : null,
            OwnFrontTop: OwnFront(team, FrontAt(Lane.Top, seconds)),
            OwnFrontMiddle: OwnFront(team, FrontAt(Lane.Middle, seconds)),
            OwnFrontBottom: OwnFront(team, FrontAt(Lane.Bottom, seconds)),
            OwnCreepsInLane: counts is null ? null : team == Team.Radiant ? counts.RadiantInLane : counts.DireInLane,
            OwnCreepsAtFront: counts is null ? null : team == Team.Radiant ? counts.RadiantAtFront : counts.DireAtFront,
            ClashPoint: clash,
            ClashPastOwnTower: clash is { } c1 && ownTower is { } mine ? c1 - mine : null,
            ClashShortOfEnemyTower: clash is { } c2 && enemyTower is { } theirs ? theirs - c2 : null,
            OwnFrontChangeLast10: Change(10),
            OwnFrontChangeLast30: Change(30),
            OwnHealthAtFront: here?.Strength is { } s1 ? (team == Team.Radiant ? s1.RadiantHealthAtFront : s1.DireHealthAtFront) : null,
            OwnSiegeInLane: here?.Strength is { } s2 ? (team == Team.Radiant ? s2.RadiantSiege : s2.DireSiege) : null,
            OwnCreepsOffLane: OwnCreepsOffLane(team, seconds),
            EnemyCreepsKilledByTeam: CreepDeaths(lane, seconds, death => death.CreepTeam != team && death.Killer == CreepKiller.Hero
                && death.KillerHeroId is { } killer && teamOf.GetValueOrDefault(killer) == team),
            OwnCreepsKilledByEnemyHeroes: CreepDeaths(lane, seconds, death => death.CreepTeam == team && death.Killer == CreepKiller.Hero
                && death.KillerHeroId is { } killer && teamOf.TryGetValue(killer, out var killerTeam) && killerTeam != team),
            OwnCreepsKilledByEnemyTower: CreepDeaths(lane, seconds, death => death.CreepTeam == team && death.Killer == CreepKiller.Tower),
            EnemyCreepsKilledByOwnTower: CreepDeaths(lane, seconds, death => death.CreepTeam != team && death.Killer == CreepKiller.Tower),
            OwnWaveStacked: counts is null ? null : Stacked(team == Team.Radiant ? counts.RadiantInLane : counts.DireInLane, seconds),
            LastHitsAvailable: ownWave is null ? null : enemyWave!.LowAtFront,
            DeniesAvailable: ownWave?.LowAtFront,
            OwnSuperCreeps: ownWave is null ? null : ownWave.Super + ownWave.Mega > 0,
            CreepHitsOnMeLast10: activity?.CreepHeroHits is { } creepHits
                ? creepHits.Where(hits => hits.TargetHeroId == heroId && hits.Seconds <= seconds && hits.Seconds > seconds - 10)
                    .Sum(hits => hits.Hits)
                : null);
    }

    public const double StackedWaveFactor = 1.5;

    // Lane creeps of one wave: 3 melee + 1 ranged, one more melee every 15 minutes (siege and flag creeps left out).
    private static double OneWave(int seconds) => 4 + Math.Max(0, seconds) / 900;

    private static bool Stacked(int creeps, int seconds) => creeps >= StackedWaveFactor * OneWave(seconds);

    private bool? EnemyWaveStacked(Team team, PositionSample self, int seconds) =>
        LaneOf(team, self) is { } lane && FrontAt(lane, seconds)?.Counts is { } counts
            ? Stacked(team == Team.Radiant ? counts.DireInLane : counts.RadiantInLane, seconds)
            : null;

    public const int CreepDeathWindowSeconds = 30;

    // Deaths in the hero's lane in the last CreepDeathWindowSeconds (up to this second); null outside a lane or without
    // the data. A tower only kills the other team's creeps, so the creep's team says whose tower it was.
    private int? CreepDeaths(Lane? lane, int seconds, Func<LaneCreepDeath, bool> which) =>
        lane is { } inLane && activity?.LaneCreepDeaths is { } deaths
            ? deaths.Count(death => death.Lane == inLane && death.Seconds <= seconds && death.Seconds > seconds - CreepDeathWindowSeconds
                && which(death))
            : null;

    // The latest off-lane reading at this second or up to 2 s before (the samples are every 2 s and only written when
    // some creep is off the lanes, so no reading means none).
    private int? OwnCreepsOffLane(Team team, int seconds)
    {
        if (activity?.OffLaneCreeps is not { } samples)
        {
            return null;
        }

        var sample = samples.LastOrDefault(entry => entry.Seconds <= seconds && entry.Seconds >= seconds - 2);
        return sample is null ? 0 : team == Team.Radiant ? sample.RadiantCount : sample.DireCount;
    }

    private int? WaveHealthBalance(Team team, PositionSample self, int seconds) =>
        LaneOf(team, self) is { } lane && FrontAt(lane, seconds)?.Strength is { } strength
            ? team == Team.Radiant
                ? strength.RadiantHealthAtFront - strength.DireHealthAtFront
                : strength.DireHealthAtFront - strength.RadiantHealthAtFront
            : null;

    // Lane creeps spawn every WaveSpawnSeconds from 0:00; a wave this close to a tower (or past it) is hitting it.
    public const int WaveSpawnSeconds = 30;
    public const int WaveAtTowerRange = 800;
    private static readonly Lane[] Lanes = [Lane.Top, Lane.Middle, Lane.Bottom];

    private MapWaves MapWavesFor(Team team, int seconds)
    {
        var secondsToNext = seconds < 0 ? -seconds : (WaveSpawnSeconds - seconds % WaveSpawnSeconds) % WaveSpawnSeconds;
        if (wavesAt is null)
        {
            return new MapWaves(null, null, null, null, null, null, null, null, secondsToNext);
        }

        var lanes = Lanes.Select(lane =>
        {
            var front = FrontAt(lane, seconds);
            var (own, enemy) = (OwnFront(team, front), EnemyFront(team, front));
            var before = OwnFront(team, FrontAt(lane, seconds - 30));
            var (ownTower, enemyTower) = LaneTowers(team, lane, seconds);
            return (
                Clash: own is { } o && enemy is { } e ? (o + e) / 2 : (int?)null,
                Change: own is { } now && before is { } then ? now - then : (int?)null,
                // The enemy front is the enemy wave's nearest point to us; the own front the farthest towards them.
                EnemyAtOwnTower: enemy is { } theirs && ownTower is { } mine && theirs <= mine + WaveAtTowerRange,
                OwnAtEnemyTower: own is { } ours && enemyTower is { } their && ours >= their - WaveAtTowerRange);
        }).ToList();
        var withTowers = activity?.Buildings is not null;
        return new MapWaves(
            lanes[0].Clash, lanes[1].Clash, lanes[2].Clash, lanes[0].Change, lanes[1].Change, lanes[2].Change,
            withTowers ? lanes.Count(lane => lane.EnemyAtOwnTower) : null,
            withTowers ? lanes.Count(lane => lane.OwnAtEnemyTower) : null,
            secondsToNext);
    }

    // The most advanced standing tower of each team in the lane, as progress from the team's own side (so the own
    // tower is the furthest forward one and the enemy's the nearest to us). Null without building health.
    private (int? Own, int? Enemy) LaneTowers(Team team, Lane lane, int seconds)
    {
        if (activity?.Buildings is not { } buildings)
        {
            return (null, null);
        }

        var standing = buildings.Where(building => building.Kind == BuildingKind.Tower && building.Lane == lane && building.StandingAt(seconds))
            .Select(building => (building.Team, Progress: FromOwnSide(team, building.X + building.Y))).ToList();
        var own = standing.Where(tower => tower.Team == team).Select(tower => (int?)tower.Progress).Max();
        var enemy = standing.Where(tower => tower.Team != team).Select(tower => (int?)tower.Progress).Min();
        return (own, enemy);
    }

    // The living enemies in the hero's lane at this second (the region of each one's position now: no lane guess from
    // the first minutes, no future); with seenOnly, only those the team sees. Null outside a lane, or without
    // visibility when seenOnly.
    private static int? EnemiesInLane(Team team, PositionSample self, List<PositionSample> enemies, bool seenOnly)
    {
        if (LaneOf(team, self) is not { } lane || (seenOnly && enemies.Any(enemy => enemy.Vitals?.VisibleToEnemies is null)))
        {
            return null;
        }

        return enemies.Count(enemy => LaneOf(team, enemy) == lane && (!seenOnly || enemy.Vitals!.VisibleToEnemies == true));
    }

    private int? EnemyCreepsAtFront(Team team, PositionSample self, int seconds) =>
        LaneOf(team, self) is { } lane && FrontAt(lane, seconds)?.Counts is { } counts
            ? team == Team.Radiant ? counts.DireAtFront : counts.RadiantAtFront
            : null;

    private int? EnemyWaveAhead(Team team, PositionSample self, int seconds) =>
        wavesAt is not null && LaneOf(team, self) is { } lane && EnemyFront(team, FrontAt(lane, seconds)) is { } front
            ? front - FromOwnSide(team, self.X + self.Y)
            : null;
}
