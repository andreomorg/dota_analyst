using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One value per region of the map, seen from the hero's team (bases and jungles are its own or the enemy's; lanes: a band around each lane's path; river: the water of the game's minimap).</summary>
/// <param name="OwnBase">The team's base (behind its tier-3 towers).</param>
/// <param name="EnemyBase">The enemy's base.</param>
/// <param name="TopLane">The top lane.</param>
/// <param name="MiddleLane">The middle lane.</param>
/// <param name="BottomLane">The bottom lane.</param>
/// <param name="River">The river.</param>
/// <param name="OwnJungle">The team's jungle (its side of the river, off the lanes).</param>
/// <param name="EnemyJungle">The enemy's jungle.</param>
public record RegionValues(
    double OwnBase, double EnemyBase, double TopLane, double MiddleLane, double BottomLane, double River, double OwnJungle,
    double EnemyJungle);
