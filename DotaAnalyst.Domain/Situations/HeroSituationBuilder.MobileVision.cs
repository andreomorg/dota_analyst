using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

public sealed partial class HeroSituationBuilder
{
    private const int LocalVisionSpacing = 256;

    private SituationMobileVision MobileVisionOf(Team team, PositionSample self, int seconds)
    {
        var frame = activity?.VisionUnits?.LastOrDefault(sample => sample.Seconds <= seconds);
        if (frame is null || seconds - frame.Seconds > SampleTolerance)
        {
            return new SituationMobileVision();
        }

        var own = team == Team.Radiant ? frame.Radiant : frame.Dire;
        var enemy = team == Team.Radiant ? frame.Dire : frame.Radiant;
        var at = new MapPoint(self.X, self.Y);
        var near = own.Count(circle => new MapPoint(circle.X, circle.Y).IsWithin(at, FightRange));
        if (IsNightAt(seconds) is not { } night)
        {
            return new SituationMobileVision(TeamSourcesNear: near);
        }

        var inEnemyRange = enemy.Any(circle => new MapPoint(circle.X, circle.Y)
            .IsWithin(at, night ? circle.Night : circle.Day));
        var heroes = HeroVisionSources(team, seconds);
        if (terrainMap is not { } map || activity?.Trees is not { } trees || heroes is null)
        {
            return new SituationMobileVision(TeamSourcesNear: near, HiddenInEnemyMobileRange: inEnemyRange);
        }

        var sources = heroes.Concat(own.Select(circle => new VisionSource(circle.X, circle.Y, night ? circle.Night : circle.Day)))
            .Concat(replay.Wards!.Where(ward => ward.Team == team && ward.Kind == WardKind.Observer
                && ward.PlacedAt <= seconds && (ward.RemovedAt ?? int.MaxValue) > seconds)
                .Select(ward => new VisionSource(ward.X, ward.Y, ObserverVisionRadius)))
            .Where(source => new MapPoint(source.X, source.Y).IsWithin(at, FightRange + source.Radius)).ToList();
        var down = trees.DownAt(seconds);
        int ground = 0, seen = 0;
        for (var dx = -FightRange / LocalVisionSpacing; dx <= FightRange / LocalVisionSpacing; dx++)
        {
            for (var dy = -FightRange / LocalVisionSpacing; dy <= FightRange / LocalVisionSpacing; dy++)
            {
                var x = self.X + dx * LocalVisionSpacing;
                var y = self.Y + dy * LocalVisionSpacing;
                if (!new MapPoint(x, y).IsWithin(at, FightRange) || !map.IsWalkable(x, y))
                {
                    continue;
                }

                ground++;
                if (sources.Any(source => VisionModel.Sees(map, source, x, y, down, VisionRules.Measured)))
                {
                    seen++;
                }
            }
        }

        return new SituationMobileVision(ground == 0 ? null : (double)seen / ground, near, inEnemyRange);
    }
}
