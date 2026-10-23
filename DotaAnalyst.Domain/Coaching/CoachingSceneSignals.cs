namespace DotaAnalyst.Domain.Coaching;

// Visible anchor values only; routes are rounded like the public coaching snapshots before evaluating the rules.
public sealed record CoachingSignalFacts(
    int? VisibleEnemies = null, int? Allies = null, double? HealthShare = null,
    int? NearestEnemyDistance = null, bool? EscapeItemReady = null, bool? EscapeReady = null,
    int? Damage = null, bool? OnEnemySide = null, int? AllyDistance = null, int? MissingEnemies = null,
    int? LastSeenSeconds = null, int? AheadOfWave = null, bool? ObserverVision = null,
    int? BuildingsUnderAttack = null, bool? TeleportReady = null, bool? UltimateReady = null,
    bool? RoshanAlive = null, int? NetWorthLead = null, bool? HasAegis = null,
    bool? DestinationOnEnemySide = null, int? DestinationAdvance = null, double? TowerSeenShare = null,
    int? TowerNearestEnemy = null, int? TowerPathLength = null, int? AllyPathLength = null,
    int? AllyPings = null);

public sealed record CoachingSceneSignal(string Id, string Name, IReadOnlyDictionary<string, double?> Numbers);

// Priority and cited fields match tactical/policy_v2.py R1–R11. No retrospective result or hidden enemy data.
public static class CoachingSceneSignals
{
    public static IReadOnlyList<CoachingSceneSignal> Of(CoachingSignalFacts f)
    {
        var result = new List<CoachingSceneSignal>();
        void Add(bool active, string id, string name, params (string Key, double? Value)[] numbers)
        {
            if (active) result.Add(new(id, name, numbers.ToDictionary(n => n.Key, n => n.Value)));
        }
        Add(f.VisibleEnemies > f.Allies + 1, "R1", "outnumbered",
            ("alliesWithinFightRange", f.Allies), ("visibleEnemiesWithinFightRange", f.VisibleEnemies));
        Add(f.HealthShare <= .30 && f.VisibleEnemies >= 1, "R2", "low-health",
            ("healthShare", f.HealthShare), ("visibleEnemiesWithinFightRange", f.VisibleEnemies),
            ("nearestVisibleEnemyDistance", f.NearestEnemyDistance));
        Add((f.EscapeItemReady == true || f.EscapeReady == true) && f.Damage > 0, "R3", "escape-ready-under-damage",
            ("damageFromHeroesLast6Seconds", f.Damage));
        Add(f.OnEnemySide == true && f.AllyDistance > 1500 && f.MissingEnemies >= 3, "R4", "isolated-enemy-side",
            ("nearestAllyDistance", f.AllyDistance), ("missingEnemies", f.MissingEnemies),
            ("missingEnemyLastSeenSecondsAgo", f.LastSeenSeconds));
        Add(f.AheadOfWave > 0 && f.ObserverVision == false && f.MissingEnemies >= 2, "R5", "ahead-without-observer",
            ("missingEnemies", f.MissingEnemies));
        Add(f.BuildingsUnderAttack >= 1 && f.TeleportReady == true, "R6", "teleport-defense",
            ("ownBuildingsUnderAttack", f.BuildingsUnderAttack));
        Add(f.UltimateReady == true && f.VisibleEnemies >= 2, "R7", "ultimate-window",
            ("visibleEnemiesWithinFightRange", f.VisibleEnemies));
        Add(f.RoshanAlive == true && f.NetWorthLead >= 3000 && f.HasAegis == false, "R8", "roshan-window",
            ("teamNetWorthLead", f.NetWorthLead));
        Add(f.DestinationOnEnemySide == true && f.MissingEnemies >= 3 && f.TowerSeenShare < .5, "R9", "advance-poor-return-vision",
            ("intentDestinationAdvance", f.DestinationAdvance), ("missingEnemies", f.MissingEnemies),
            ("towerSeenShare", f.TowerSeenShare));
        Add(f.TowerNearestEnemy <= 1200 && f.HealthShare <= .5, "R10", "tower-route-exposed",
            ("healthShare", f.HealthShare), ("towerPathLength", f.TowerPathLength),
            ("towerNearestSeenEnemy", f.TowerNearestEnemy), ("allyPathLength", f.AllyPathLength));
        Add(f.AllyPings >= 2 && f.AllyDistance > 1500, "R11", "distant-ally-pings",
            ("allyPingsNearLast10", f.AllyPings), ("nearestAllyDistance", f.AllyDistance));
        return result;
    }
}
