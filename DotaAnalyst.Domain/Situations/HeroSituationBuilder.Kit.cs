using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// What the heroes can do with their kits (abilities of the current patch, AbilityEffects) and items: the enemies' threats
// known from the draft, the hero's own disable/escape ready now, and (hidden) the enemies near it with a disable ready.
public sealed partial class HeroSituationBuilder
{
    private const string ItemPrefix = "item_";

    private KitState KitOf(int heroId, Team team, int seconds)
    {
        int? EnemiesWhoseKit(Func<AbilityEffect, bool> which)
        {
            if (kits is null || abilityEffects is null)
            {
                return null;
            }

            return teamOf.Where(hero => hero.Value != team && PositionAt(hero.Key, seconds) is not null)
                .Count(hero => kits.TryGetValue(hero.Key, out var kit) && kit.Abilities.Any(ability =>
                    abilityEffects.TryGetValue(ability, out var effects) && effects.Any(which)));
        }

        return new KitState(
            EnemiesWithStun: EnemiesWhoseKit(effect => effect == AbilityEffect.Stun),
            EnemiesWithSilenceOrHex: EnemiesWhoseKit(effect => effect is AbilityEffect.Silence or AbilityEffect.Hex),
            EnemiesWithEscape: EnemiesWhoseKit(effect => effect == AbilityEffect.Escape),
            EnemiesWithInvisibility: EnemiesWhoseKit(effect => effect == AbilityEffect.Invisibility),
            MyDisableReady: Ready(heroId, seconds, AbilityEffects.IsDisable),
            MyEscapeReady: Ready(heroId, seconds, effect => effect is AbilityEffect.Escape or AbilityEffect.Invisibility or AbilityEffect.Save
                or AbilityEffect.SpellImmunity));
    }

    // Living enemies within fight range with a disable ready now (the player cannot see their cooldowns).
    private int? EnemiesWithDisableReadyNear(Team team, PositionSample self, int seconds)
    {
        var ready = teamOf.Where(hero => hero.Value != team && PositionAt(hero.Key, seconds) is { } at && Distance(self, at) <= FightRange)
            .Select(hero => Ready(hero.Key, seconds, AbilityEffects.IsDisable)).ToList();
        return ready.Any(value => value is null) ? null : ready.Count(value => value == true);
    }

    // A learned ability (an upgrade up to this second) or an item in the inventory or the neutral slot with one of the
    // effects and no cooldown running. Null without the abilities' data and the items' data (unknown).
    private bool? Ready(int heroId, int seconds, Func<AbilityEffect, bool> which)
    {
        bool? abilityReady = null;
        if (abilityEffects is not null && replay.AbilityUpgrades is { } upgrades && activity?.AbilityCooldowns is { } cooldowns)
        {
            var cooling = cooldowns.Where(cooldown => cooldown.HeroId == heroId && cooldown.ActiveAt(seconds)).Select(cooldown => cooldown.Name)
                .ToHashSet();
            abilityReady = upgrades.Where(upgrade => upgrade.HeroId == heroId && upgrade.Seconds <= seconds).Select(upgrade => upgrade.AbilityName)
                .Distinct()
                .Any(ability => !cooling.Contains(ability) && abilityEffects.TryGetValue(ability, out var effects) && effects.Any(which));
        }

        bool? itemReady = null;
        if (itemDetails is not null && ItemsAt(heroId, seconds) is { } items)
        {
            itemReady = UsableItems(items.Slots).Any(item => !items.Cooling.Contains(item)
                && itemDetails.TryGetValue(item.StartsWith(ItemPrefix, StringComparison.Ordinal) ? item[ItemPrefix.Length..] : item, out var details)
                && details.HasActive && AbilityEffects.Of(details).Any(which));
        }

        return abilityReady is null && itemReady is null ? null : abilityReady == true || itemReady == true;
    }
}
