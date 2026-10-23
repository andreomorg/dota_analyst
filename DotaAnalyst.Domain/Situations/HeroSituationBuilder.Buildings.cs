using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Situations;

// The towers around the hero and the buildings losing health (ReplayActivity.Buildings: sparse health readings).
public sealed partial class HeroSituationBuilder
{
    // A building "under attack" lost health between this window's start and now.
    public const int BuildingAttackWindowSeconds = 10;

    private BuildingPressure BuildingsAround(Team team, PositionSample self, int seconds)
    {
        if (activity?.Buildings is not { } buildings)
        {
            return BuildingPressure.Unknown;
        }

        var (ownDistance, ownShare) = NearestTower(buildings, team, self, seconds);
        var (enemyDistance, enemyShare) = NearestTower(buildings, team == Team.Radiant ? Team.Dire : Team.Radiant, self, seconds);
        return new BuildingPressure(ownDistance, ownShare, enemyDistance, enemyShare, BuildingsUnderAttack(team, seconds));
    }

    private static (int? Distance, double? HealthShare) NearestTower(
        IEnumerable<BuildingHealth> buildings, Team team, PositionSample self, int seconds)
    {
        var nearest = buildings.Where(building => building.Team == team && building.Kind == BuildingKind.Tower && building.StandingAt(seconds))
            .Select(building => (Building: building, Distance: (int)Math.Round(new MapPoint(building.X, building.Y).DistanceTo(new MapPoint(self.X, self.Y)))))
            .OrderBy(tower => tower.Distance)
            .FirstOrDefault();
        return nearest.Building?.At(seconds) is { } reading
            ? (nearest.Distance, Math.Round((double)reading.Health / reading.MaxHealth, 3))
            : (null, null);
    }

    // Standing buildings of the team whose health now is below their health at the window's start (a building first
    // read inside the window has nothing to compare with). Null without building health.
    private int? BuildingsUnderAttack(Team team, int seconds) =>
        activity?.Buildings?.Count(building => building.Team == team && building.StandingAt(seconds)
            && building.At(seconds - BuildingAttackWindowSeconds) is { } before && building.At(seconds)!.Health < before.Health);
}
