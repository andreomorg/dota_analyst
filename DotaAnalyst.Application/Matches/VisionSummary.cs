namespace DotaAnalyst.Application.Matches;

/// <summary>What the fog of war showed around the hero, from the game's own visibility, from 0:00.</summary>
/// <param name="SeenByEnemiesShare">Share (0..1) of the time alive the enemy team saw the hero.</param>
/// <param name="DeathsWhileTracked">Deaths after being seen on every sample of the 10 s before; null when a death can't be evaluated.</param>
/// <param name="DeathsUnseenBefore">Deaths after being seen on no sample of the 10 s before; null when a death can't be evaluated.</param>
/// <param name="DeathsToKillerFromFog">Deaths to a killer hero the hero's team had not seen in the 6 s before; null when a death can't be evaluated.</param>
/// <param name="KillsFromFog">Kills on enemies whose team had not seen the hero in the 6 s before; null when a kill can't be evaluated.</param>
/// <param name="ObserverSeenShare">Of the ground in range of the hero's observer wards, the share (0..1) they really saw (terrain, trees); null without observers, map or trees.</param>
public record VisionSummary(
    double SeenByEnemiesShare, int? DeathsWhileTracked, int? DeathsUnseenBefore, int? DeathsToKillerFromFog,
    int? KillsFromFog, double? ObserverSeenShare);
