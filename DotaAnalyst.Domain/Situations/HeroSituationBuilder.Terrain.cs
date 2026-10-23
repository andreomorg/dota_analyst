using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

public sealed partial class HeroSituationBuilder
{
    private readonly GameMap? terrainMap;
    private readonly ReplayAdditionalFacts? mapFacts;

    private SituationTerrain TerrainOf(Team team, PositionSample self, int seconds)
    {
        if (terrainMap is not { } map)
        {
            return new SituationTerrain();
        }

        var down = activity?.Trees?.DownAt(seconds);
        var zone = TerrainZones.Classify(map, self.X, self.Y, down);
        var nearestSeen = Living(seconds, hero => teamOf[hero] != team)
            .Where(enemy => enemy.Vitals?.VisibleToEnemies == true)
            .OrderBy(enemy => Distance(self, enemy)).Select(enemy => (PositionSample?)enemy).FirstOrDefault();
        var enemyLevel = nearestSeen is { } seen ? map.LevelAt(seen.X, seen.Y) : null;
        var boss = mapFacts?.BossTracks?.Where(track => track.Kind == "roshan")
            .Select(track => track.LastAt(seconds)).OfType<BossSample>()
            .OrderByDescending(sample => sample.Seconds).FirstOrDefault();
        var pit = boss is null ? null : map.PitNearest(boss.X, boss.Y);
        var observers = replay.Wards!.Where(ward => ward.Team != team && ward.Kind == WardKind.Observer
            && ward.PlacedAt <= seconds && (ward.RemovedAt ?? int.MaxValue) > seconds);
        var enemySources = HeroVisionSources(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds);
        return new SituationTerrain(
            Level: zone.Level,
            NearestSeenEnemyLevel: enemyLevel,
            HeightAdvantage: zone.Level - enemyLevel,
            OnEnemyHighGround: zone.Level is null ? null : zone.HighGround && zone.Side != team,
            TreesWithin300: down is null ? null : map.TreesWithin(self.X, self.Y, 300, down).Count,
            TreesWithin600: down is null ? null : map.TreesWithin(self.X, self.Y, 600, down).Count,
            TreeLineDistance: down is null ? null : map.Trees.Where((_, index) => !down.Contains(index))
                .Select(tree => (double?)tree.DistanceTo(new MapPoint(self.X, self.Y))).Min(),
            Openness: down is null ? null : zone.Openness,
            InNoWardZone: map.InNoWardZone(self.X, self.Y),
            ActiveRoshanPitDistance: pit is null ? null : new MapPoint(pit.Center.X, pit.Center.Y).DistanceTo(new MapPoint(self.X, self.Y)),
            RoshanAlive: RoshanKnownAlive(seconds),
            NearObjective: zone.NearObjective is not null,
            NearCurrent: map.Currents.Any(point => point.IsWithin(new MapPoint(self.X, self.Y), TerrainZones.ObjectiveRadius)),
            // LanternUse records activation only, not capture/expiry: it cannot establish active ownership.
            TeamWatchersActive: null,
            HiddenInEnemyHeroVision: down is null || enemySources is null ? null
                : enemySources.Any(source => VisionModel.Sees(map, source, self.X, self.Y, down, VisionRules.Measured)),
            HiddenEnemyObserversCovering: down is null ? null : observers.Count(ward =>
                VisionModel.Sees(map, new VisionSource(ward.X, ward.Y, ObserverVisionRadius), self.X, self.Y, down, VisionRules.Measured)),
            HiddenRoshanAlive: boss?.Alive);
    }

    // Roshan respawns at a random time between 8 and 11 minutes after a kill (7.3x); everyone sees the kill.
    private const int RoshanMinRespawn = 480;
    private const int RoshanMaxRespawn = 660;

    private bool? RoshanKnownAlive(int seconds)
    {
        var lastKill = replay.Objectives!.Where(objective => objective.Kind == ObjectiveKind.Roshan && objective.Seconds <= seconds)
            .Select(objective => (int?)objective.Seconds).Max();
        if (lastKill is not { } killed)
        {
            return true;
        }

        var since = seconds - killed;
        return since < RoshanMinRespawn ? false : since >= RoshanMaxRespawn ? true : null;
    }

    // Catalog ranges only: absent day/night or any living hero's profile means unknown, not a default radius.
    private List<VisionSource>? HeroVisionSources(Team owner, int seconds)
    {
        if (IsNightAt(seconds) is not { } night)
        {
            return null;
        }

        var sources = new List<VisionSource>();
        foreach (var hero in teamOf.Where(hero => hero.Value == owner))
        {
            if (PositionAt(hero.Key, seconds) is not { } position)
            {
                continue;
            }

            var movement = heroDetails?.GetValueOrDefault(hero.Key)?.Profile?.Movement;
            var radius = night ? movement?.NightVision?.Value : movement?.DayVision?.Value;
            if (radius is null)
            {
                return null;
            }

            sources.Add(new VisionSource(position.X, position.Y, radius.Value));
        }

        return sources;
    }
}
