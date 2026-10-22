using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Situations;

// Who the enemies are to this hero (the matchup): what the nearest one the team sees has already done to it and its
// team, and (hidden) which enemies in fight range have their ultimate or a Black King Bar ready.
public sealed partial class HeroSituationBuilder
{
    private const string BlackKingBar = "item_black_king_bar";

    private EnemyThreat ThreatOf(int heroId, Team team, PositionSample self, int seconds)
    {
        var enemyIds = teamOf.Where(hero => hero.Value != team).Select(hero => hero.Key).ToList();
        var deaths = replay.Deaths!.Where(death => death.Seconds <= seconds && death.KillerHeroId is not null).ToList();
        int KillsOn(int enemy, Func<int, bool> victims) => deaths.Count(death => death.KillerHeroId == enemy && victims(death.HeroId));
        bool Mine(int hero) => hero == heroId;
        bool MyTeam(int hero) => teamOf.GetValueOrDefault(hero) == team;

        // The nearest enemy the team sees now; none when no enemy is seen or the document has no visibility.
        int? nearest = null;
        var nearestDistance = int.MaxValue;
        foreach (var enemy in enemyIds)
        {
            if (PositionAt(enemy, seconds) is { Vitals.VisibleToEnemies: true } sample && Distance(self, sample) < nearestDistance)
            {
                (nearest, nearestDistance) = (enemy, Distance(self, sample));
            }
        }

        // The draft: the living enemies' catalog profiles (all known, or none counted).
        var alive = enemyIds.Where(enemy => PositionAt(enemy, seconds) is not null).ToList();
        var profiles = heroDetails is not null && alive.All(heroDetails.ContainsKey)
            ? alive.Select(enemy => heroDetails[enemy]).ToList()
            : null;
        int? WithRole(HeroRole role) => profiles?.Count(profile => profile.Roles.Contains(role));

        return new EnemyThreat(
            MostKillsOnMeByOneEnemy: enemyIds.Count == 0 ? 0 : enemyIds.Max(enemy => KillsOn(enemy, Mine)),
            NearestVisibleEnemyKillsOnMe: nearest is { } a ? KillsOn(a, Mine) : null,
            NearestVisibleEnemyKillsOnTeam: nearest is { } b ? KillsOn(b, MyTeam) : null,
            NearestVisibleEnemyLevelLead: nearest is { } c ? Value(c, StatKey.Level, seconds) - Value(heroId, StatKey.Level, seconds) : null,
            // The disables it put on this team so far (felt by the team: the stun bar is on screen).
            NearestVisibleEnemyDisablesOnTeam: nearest is { } d && activity?.Debuffs is { } debuffs
                ? debuffs.Count(debuff => debuff.SourceHeroId == d && debuff.IsDisable && debuff.Start <= seconds && MyTeam(debuff.TargetHeroId))
                : null,
            EnemyDisablersAlive: WithRole(HeroRole.Disabler),
            EnemyInitiatorsAlive: WithRole(HeroRole.Initiator),
            EnemyNukersAlive: WithRole(HeroRole.Nuker),
            EnemyEscapesAlive: WithRole(HeroRole.Escape),
            EnemyMeleeAlive: profiles?.Count(profile => profile.AttackType == HeroAttackType.Melee));
    }

    // Enemies within fight range with their ultimate ready / a Black King Bar in the inventory off cooldown. The player
    // does not know these (cooldowns of enemies are not on screen). Null without cooldowns/inventories.
    private (int? UltimatesReady, int? BlackKingBarsReady) EnemyCooldownsNear(Team team, PositionSample self, int seconds)
    {
        if (activity?.AbilityCooldowns is null || activity.SlotChanges is null || activity.ItemCooldowns is null)
        {
            return (null, null);
        }

        var near = teamOf.Where(hero => hero.Value != team).Select(hero => hero.Key)
            .Where(enemy => PositionAt(enemy, seconds) is { } sample && Distance(self, sample) <= FightRange)
            .ToList();
        // An enemy whose ultimate readiness is unknown (no catalog, not learned yet...) makes the count unknown: counting
        // it as "not ready" would turn unknown into zero (DATA-D21 finding on D03).
        var ultimates = near.Select(enemy => ReadinessOf(enemy, seconds).UltimateReady).ToList();
        return (
            ultimates.Any(ready => ready is null) ? null : ultimates.Count(ready => ready == true),
            near.Count(enemy => ItemsAt(enemy, seconds) is { } items
                && Inventory(items.Slots).Contains(BlackKingBar) && !items.Cooling.Contains(BlackKingBar)));
    }
}
