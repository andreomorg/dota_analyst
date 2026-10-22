using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// How much the spells can do now (SpellPower): the headline damage and healing of each learned spell at its level
// (AbilityDetails.MainAmount), its range and area, with the talents learned and Scepter/Shard bought up to this second
// applied, and the cooldowns and the mana of this second. Only upgrades, purchases, cooldowns and mana up to this
// second are read.
public sealed partial class HeroSituationBuilder
{
    // Purchases that give the upgrades (Aghanim's Blessing and the Roshan drops included). Approximate: an item bought
    // for someone else, or sold, still counts for the buyer.
    private static readonly string[] ScepterItems = ["ultimate_scepter", "ultimate_scepter_2", "ultimate_scepter_roshan"];
    private static readonly string[] ShardItems = ["aghanims_shard", "aghanims_shard_roshan"];

    // The game writes a global spell's range as a huge number (99999): it reaches anywhere, so the map's diagonal.
    private const double GlobalRange = 25_000;
    private const double MapDiagonal = 26_800;

    private sealed record Spell(AbilityDetails Details, int Level, IReadOnlySet<string> Bonuses)
    {
        public double? CastRange => Details.CastRangeAt(Level, Bonuses) is { } range ? Math.Min(range, MapDiagonal) : null;

        public double? Damage => Details.MainAmount(AbilityValueKind.Damage, Level, Bonuses);

        public double? Healing => Details.MainAmount(AbilityValueKind.Healing, Level, Bonuses);

        public double ManaCost => Details.ManaCostAt(Level) ?? 0;

        // How far from the caster it hits: cast range plus area (a spell centred on the caster: its area alone).
        public double? Reach => CastRange >= GlobalRange ? MapDiagonal
            : (CastRange ?? 0) + (Details.RadiusAt(Level, Bonuses) ?? 0) is > 0 and var reach ? Math.Min(reach, MapDiagonal) : null;
    }

    private SpellPower SpellsOf(int heroId, Team team, PositionSample self, int seconds)
    {
        if (abilityDetails is null || replay.AbilityUpgrades is null || activity?.AbilityCooldowns is null)
        {
            return SpellPower.Unknown;
        }

        var mine = ReadySpells(heroId, seconds, self.Vitals?.Mana);
        var living = teamOf.Where(hero => hero.Key != heroId)
            .Select(hero => (HeroId: hero.Key, Ally: hero.Value == team, At: PositionAt(hero.Key, seconds)))
            .Where(hero => hero.At is not null)
            .Select(hero => (hero.HeroId, hero.Ally, At: hero.At!.Value))
            .ToList();
        List<List<Spell>?> ReadyNear(bool allies) => living
            .Where(hero => hero.Ally == allies && Distance(self, hero.At) <= FightRange)
            .Select(hero => ReadySpells(hero.HeroId, seconds, hero.At.Vitals?.Mana)).ToList();
        var allies = ReadyNear(allies: true);
        var enemiesNear = ReadyNear(allies: false);
        var enemies = living.Where(hero => !hero.Ally).ToList();
        var enemyDisables = enemies.SelectMany(enemy => LearnedSpells(enemy.HeroId, seconds)).Where(IsDisable).ToList();
        var seenEnemy = enemies.Where(enemy => enemy.At.Vitals?.VisibleToEnemies == true)
            .Select(enemy => (int?)Distance(self, enemy.At)).Min();
        var reachingMe = enemies.Select(enemy => ReadySpells(enemy.HeroId, seconds, enemy.At.Vitals?.Mana)?
            .Where(spell => spell.Details.AffectsEnemies && spell.Reach >= Distance(self, enemy.At)).ToList()).ToList();

        return new SpellPower(
            MyReadyDamage: Sum(mine, spell => spell.Damage),
            MyReadyHealing: Sum(mine, spell => spell.Healing),
            MyReadyManaCost: Sum(mine, spell => spell.ManaCost),
            MyManaAfterReadySpells: mine is not null ? self.Vitals!.Mana - Sum(mine, spell => spell.ManaCost) : null,
            AlliesReadyHealingNear: SumOrNull(allies, spell => spell.Healing),
            EnemyKitDamageAlive: (int)Math.Round(enemies.SelectMany(enemy => LearnedSpells(enemy.HeroId, seconds)).Sum(spell => spell.Damage ?? 0)),
            HiddenEnemiesReadyDamageNear: SumOrNull(enemiesNear, spell => spell.Damage),
            HiddenEnemiesReadyHealingNear: SumOrNull(enemiesNear, spell => spell.Healing),
            MyReadyAreaSpells: mine?.Count(spell => spell.Details.IsAreaOfEffect),
            MyReadySingleTargetSpells: mine?.Count(spell => spell.Details.IsSingleTarget),
            MyReadySpellMaxRange: mine is null ? null : (int)Math.Round(mine.Max(spell => spell.CastRange) ?? 0),
            MyReadyDisablesReachingSeenEnemy: mine is null ? null
                : seenEnemy is not { } distance ? 0 : mine.Count(spell => IsDisable(spell) && spell.Reach >= distance),
            EnemyKitDisableMaxRange: (int)Math.Round(enemyDisables.Max(spell => spell.Reach) ?? 0),
            EnemyKitUndispellableDisables: enemyDisables.Count(spell => spell.Details.Dispellable is Dispellable.No or Dispellable.StrongOnly),
            EnemyKitImmunityPiercingDisables: enemyDisables.Count(spell => spell.Details.PiercesSpellImmunity == true),
            HiddenEnemyReadySpellsReachingMe: reachingMe.Any(spells => spells is null) ? null : reachingMe.Sum(spells => spells!.Count),
            HiddenEnemyReadyDisablesReachingMe: reachingMe.Any(spells => spells is null) ? null : reachingMe.Sum(spells => spells!.Count(IsDisable)),
            HiddenEnemyReadyDamageReachingMe: SumOrNull(reachingMe, spell => spell.Damage));
    }

    private bool IsDisable(Spell spell) =>
        abilityEffects!.TryGetValue(spell.Details.Key, out var effects) && effects.Any(AbilityEffects.IsDisable);

    // The hero's learned spells that are not passive, at their level up to this second, with what applies to them: the
    // talents learned (upgrades of special_bonus_*), Scepter and Shard bought.
    private List<Spell> LearnedSpells(int heroId, int seconds)
    {
        var learned = replay.AbilityUpgrades!.Where(upgrade => upgrade.HeroId == heroId && upgrade.Seconds <= seconds).ToList();
        var bonuses = learned.Select(upgrade => upgrade.AbilityName).Where(name => name.StartsWith("special_bonus_", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bought = (replay.Purchases ?? []).Where(purchase => purchase.HeroId == heroId && purchase.Seconds <= seconds)
            .Select(purchase => purchase.ItemKey).ToHashSet();
        if (ScepterItems.Any(bought.Contains))
        {
            bonuses.Add(ValueBonus.Scepter);
        }

        if (ShardItems.Any(bought.Contains))
        {
            bonuses.Add(ValueBonus.Shard);
        }

        return learned.GroupBy(upgrade => upgrade.AbilityName)
            .Select(group => (Details: abilityDetails!.GetValueOrDefault(group.Key), Level: group.Max(upgrade => upgrade.Level)))
            .Where(spell => spell.Details is { IsPassive: false } && spell.Details.Profile?.IsTalent != true)
            .Select(spell => new Spell(spell.Details!, spell.Level, bonuses))
            .ToList();
    }

    // Learned spells with no cooldown running and enough mana (each on its own). Null without the hero's mana.
    private List<Spell>? ReadySpells(int heroId, int seconds, int? mana)
    {
        if (mana is not { } available)
        {
            return null;
        }

        var cooling = activity!.AbilityCooldowns!.Where(cooldown => cooldown.HeroId == heroId && cooldown.ActiveAt(seconds))
            .Select(cooldown => cooldown.Name).ToHashSet();
        return LearnedSpells(heroId, seconds).Where(spell => !cooling.Contains(spell.Details.Key) && spell.ManaCost <= available).ToList();
    }

    private static int? Sum(List<Spell>? spells, Func<Spell, double?> amount) =>
        spells is null ? null : (int)Math.Round(spells.Sum(spell => amount(spell) ?? 0));

    // Null when any hero's mana is unknown (never a partial sum that looks like "less").
    private static int? SumOrNull(List<List<Spell>?> heroes, Func<Spell, double?> amount) =>
        heroes.Any(hero => hero is null) ? null : heroes.Sum(hero => Sum(hero, amount)!.Value);
}
