namespace DotaAnalyst.Application.Matches;

/// <summary>The map's objectives for the hero: its counters and how many of its team's objectives it was there for (within 1500 map units).</summary>
/// <param name="Counts">The hero's own counters; null when the replay lacks them.</param>
/// <param name="RoshanKills">Roshan kills of the hero's team.</param>
/// <param name="RoshanKillsNearby">Of those, the ones the hero was near; null when there were none or a place is unknown.</param>
/// <param name="TormentorKills">Tormentor kills of the hero's team.</param>
/// <param name="TormentorKillsNearby">Of those, the ones the hero was near; null when there were none or a place is unknown.</param>
/// <param name="EnemyTowersTaken">Enemy towers (tiers 1-3) the hero's team took.</param>
/// <param name="EnemyTowersTakenNearby">Of those, the ones the hero was near; null when there were none or a place is unknown.</param>
/// <param name="OwnShrinesLost">Wisdom Shrines of the own side the enemy took (the whole team's); null when unknown.</param>
/// <param name="EnemyShrinesTaken">Wisdom Shrines of the enemy's side the team took (the whole team's); null when unknown.</param>
public record ObjectivesSummary(
    ObjectiveCountsSummary? Counts, int RoshanKills, int? RoshanKillsNearby, int TormentorKills, int? TormentorKillsNearby,
    int EnemyTowersTaken, int? EnemyTowersTakenNearby, int? OwnShrinesLost, int? EnemyShrinesTaken);
