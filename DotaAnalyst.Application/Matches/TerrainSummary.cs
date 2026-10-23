namespace DotaAnalyst.Application.Matches;

/// <summary>Where the hero stood and died by the patch's real terrain, from 0:00.</summary>
/// <param name="OwnHighGroundShare">Share (0..1) of the time alive on the own side's high ground (bases included); null without samples.</param>
/// <param name="EnemyHighGroundShare">Share (0..1) of the time alive on the enemy's high ground; null without samples.</param>
/// <param name="RiverLevelShare">Share (0..1) of the time alive at river level (not the pits); null without samples.</param>
/// <param name="NarrowGroundShare">Share (0..1) of the time alive in narrow ground (less than 70% walkable around without standing trees); null without the trees of the match.</param>
/// <param name="DeepEnemyJungleShare">Share (0..1) of the time alive in the enemy's jungle among many standing trees; null without the trees of the match.</param>
/// <param name="DeathsOnEnemyHighGround">Deaths on the enemy's high ground.</param>
/// <param name="DeathsBelowEnemyHighGround">Deaths on lower ground next to an enemy high ground (walking up a ramp, under a cliff).</param>
/// <param name="DeathsInNarrowGround">Deaths in narrow ground; null without the trees of the match.</param>
/// <param name="DeathsInDeepEnemyJungle">Deaths deep in the enemy's jungle; null without the trees of the match.</param>
public record TerrainSummary(
    double? OwnHighGroundShare, double? EnemyHighGroundShare, double? RiverLevelShare, double? NarrowGroundShare,
    double? DeepEnemyJungleShare, int DeathsOnEnemyHighGround, int DeathsBelowEnemyHighGround, int? DeathsInNarrowGround,
    int? DeathsInDeepEnemyJungle);
