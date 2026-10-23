using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

public sealed partial class HeroSituationBuilder
{
    // Deliberately not called by At(): model sample exports pay no pathfinding cost.
    public SituationEscape? EscapeAt(int heroId, int seconds)
    {
        if (!teamOf.TryGetValue(heroId, out var team) || PositionAt(heroId, seconds) is not { } self) return null;
        if (terrainMap is not { } map || activity?.Trees is not { } trees || map.Trees.Count == 0) return new();
        var start = new MapPoint(self.X, self.Y);
        var down = trees.DownAt(seconds);
        var ally = Living(seconds, hero => hero != heroId && teamOf[hero] == team)
            .Select(p => new MapPoint(p.X, p.Y)).OrderBy(p => p.DistanceSquaredTo(start)).Cast<MapPoint?>().FirstOrDefault();
        MapPoint? shelter = null;
        // Missing health data cannot establish that all towers fell.
        if (activity.Buildings is { } buildings)
        {
            shelter = buildings.Where(b => b.Team == team && b.Kind == BuildingKind.Tower && b.StandingAt(seconds))
                .Select(b => new MapPoint(b.X, b.Y)).OrderBy(p => p.DistanceSquaredTo(start)).Cast<MapPoint?>().FirstOrDefault();
            if (shelter is null && !buildings.Any(b => b.Team == team && b.Kind == BuildingKind.Tower && b.At(seconds) is null))
                shelter = map.Landmarks.Where(l => l.Team == team && l.Kind == LandmarkKind.Fountain)
                    .Select(l => new MapPoint(l.X, l.Y)).OrderBy(p => p.DistanceSquaredTo(start)).Cast<MapPoint?>().FirstOrDefault();
        }
        var enemies = Living(seconds, hero => teamOf[hero] != team);
        List<VisionSource>? sources = null;
        var frame = activity.VisionUnits?.LastOrDefault(f => f.Seconds <= seconds);
        if (frame is not null && seconds - frame.Seconds <= SampleTolerance
            && IsNightAt(seconds) is { } night && HeroVisionSources(team, seconds) is { } heroes)
        {
            var own = team == DotaAnalyst.Domain.Matches.Team.Radiant ? frame.Radiant : frame.Dire;
            sources = heroes.Concat(own.Select(c => new VisionSource(c.X, c.Y, night ? c.Night : c.Day)))
                .Concat(replay.Wards!.Where(w => w.Team == team && w.Kind == WardKind.Observer && w.PlacedAt <= seconds
                    && (w.RemovedAt ?? int.MaxValue) > seconds).Select(w => new VisionSource(w.X, w.Y, ObserverVisionRadius))).ToList();
        }

        EscapeRoute? Route(MapPoint? target)
        {
            if (target is not { } end || MapPathfinder.Find(map, start, end, down) is not { } path) return null;
            var length = path.Zip(path.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
            var straight = start.DistanceTo(end);
            double? DistanceToPath(IEnumerable<PositionSample> candidates) => candidates
                .Select(e => (double?)new CreepPath(default, default, path).DistanceTo(e.X, e.Y)).Min();
            return new EscapeRoute(end, length, straight, straight == 0 ? null : length / straight,
                sources is null ? null : (double)path.Count(p => sources.Any(s => VisionModel.Sees(map, s, p.X, p.Y, down, VisionRules.Measured))) / path.Count,
                DistanceToPath(enemies.Where(e => e.Vitals?.VisibleToEnemies == true)), DistanceToPath(enemies));
        }
        return new(Route(shelter), Route(ally));
    }
}
