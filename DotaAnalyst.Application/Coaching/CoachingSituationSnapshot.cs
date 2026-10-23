using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Application.Coaching;

/// <summary>What the player's team could know at a review anchor: a curated, visible-only slice of the hero's situation
/// (the same values the prediction models read, SituationRow). Never a hidden (fog of war) field; null = unknown.
/// The field list is the v5 coaching contract (work/coach-data/spec.md) and the Python projection checks it.</summary>
public sealed record CoachingSituationSnapshot(
    int Level, int NetWorth, int? Gold, double? HealthShare, double? ManaShare, bool? Stunned, bool? Silenced,
    int? ActiveDisables, int? ActiveSlows,
    bool? TeleportReady, bool? HasEscapeItem, bool? EscapeItemReady, bool? UltimateReady, bool? MyDisableReady,
    bool? MyEscapeReady, bool? BuybackReady,
    int EnemiesAlive, int? VisibleEnemies, int? VisibleEnemiesWithinFightRange, int? NearestVisibleEnemyDistance,
    int? MissingEnemies, int? MissingEnemyLastSeenSecondsAgo, int? NearestVisibleEnemyKillsOnMe,
    int AlliesAlive, int? NearestAllyDistance, int AlliesWithinFightRange, double? LowestAllyHealthShare,
    int? AlliesWithTeleportReady,
    MapRegion Region, bool OnEnemySide, bool? OnEnemyHighGround, int? TerrainHeightAdvantage, double? TerrainOpenness,
    bool InOwnObserverVision, double? TeamLocalVisionCoverage, int? NearestOwnTowerDistance, int? NearestEnemyTowerDistance,
    int? AheadOfOwnWave, int? WaveClashPoint, int? WaveClashPastOwnTower, int? OwnBuildingsUnderAttack,
    int TeamNetWorthLead, int TeamExperienceLead, bool? IsNight, int? SecondsSinceRoshanKill, bool? RoshanAlive,
    bool HasAegis, bool EnemyHasAegis, int RecentDeaths, int EnemyKillsLastMinute,
    int? DamageFromHeroesLast6Seconds, int? EnemiesHittingMe, int? EnemySpellsOnMeLast10, bool? UnderSmoke,
    // v5.1: the player's own orders in the last 10 s (the observable intent) and the team's pings near the hero.
    int? IntentMoves = null, int? IntentAttacks = null, int? IntentCasts = null, int? IntentDestinationDistance = null,
    int? IntentDestinationAdvance = null, bool? IntentDestinationOnEnemySide = null, int? AllyPingsNearLast10 = null,
    int? OwnPingsLast10 = null)
{
    public static CoachingSituationSnapshot From(HeroSituation situation, bool activityKnown)
    {
        var r = SituationRow.From(situation, activityKnown);
        return new(
            r.Level, r.NetWorth, r.Gold, r.HealthShare, r.ManaShare, r.Stunned, r.Silenced, r.ActiveDisables, r.ActiveSlows,
            r.TeleportReady, r.HasEscapeItem, r.EscapeItemReady, r.UltimateReady, r.MyDisableReady, r.MyEscapeReady,
            r.BuybackReady,
            r.EnemiesAlive, r.VisibleEnemies, r.VisibleEnemiesWithinFightRange, r.NearestVisibleEnemyDistance,
            r.MissingEnemies, r.MissingEnemyLastSeenSecondsAgo, r.NearestVisibleEnemyKillsOnMe,
            r.AlliesAlive, r.NearestAllyDistance, r.AlliesWithinFightRange, r.LowestAllyHealthShare, r.AlliesWithTeleportReady,
            r.Region, r.OnEnemySide, r.OnEnemyHighGround, r.TerrainHeightAdvantage, r.TerrainOpenness,
            r.InOwnObserverVision, r.TeamLocalVisionCoverage, r.NearestOwnTowerDistance, r.NearestEnemyTowerDistance,
            r.AheadOfOwnWave, r.WaveClashPoint, r.WaveClashPastOwnTower, r.OwnBuildingsUnderAttack,
            r.TeamNetWorthLead, r.TeamExperienceLead, r.IsNight, r.SecondsSinceRoshanKill, r.RoshanAlive,
            r.HasAegis, r.EnemyHasAegis, r.RecentDeaths, r.EnemyKillsLastMinute,
            // Activity-based numbers mean nothing without the activity: unknown, not "none".
            activityKnown ? r.DamageFromHeroesLast6Seconds : null, r.EnemiesHittingMe, r.EnemySpellsOnMeLast10,
            r.UnderSmoke,
            r.IntentMoves, r.IntentAttacks, r.IntentCasts, r.IntentDestinationDistance, r.IntentDestinationAdvance,
            r.IntentDestinationOnEnemySide, r.AllyPingsNearLast10, r.OwnPingsLast10);
    }
}
