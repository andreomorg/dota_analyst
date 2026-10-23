using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// The kits (abilities of the current patch, AbilityEffects): living enemies whose kit has a stun / a silence or hex / an
// escape / invisibility (known from the draft; null without the abilities' catalog), and whether the hero has a disable
// / an escape (escape, invisibility, save or spell immunity) ready now, from a learned ability off cooldown or an item in
// its inventory off cooldown (null without the data).
public sealed record KitState(
    int? EnemiesWithStun, int? EnemiesWithSilenceOrHex, int? EnemiesWithEscape, int? EnemiesWithInvisibility,
    bool? MyDisableReady, bool? MyEscapeReady);

// How much the spells can do at this second, in hit points and mana (AbilityDetails.MainAmount: one headline number
// per spell; passives and percentages left out). "Ready" = learned up to now, no cooldown running and enough mana.
// Null when the abilities' data, the upgrades, the cooldowns or the mana are missing.
public sealed record SpellPower(
    // The hero's own ready spells: damage and healing summed, their mana cost, and the mana left after casting them all
    // (negative = cannot cast them all now).
    int? MyReadyDamage,
    int? MyReadyHealing,
    int? MyReadyManaCost,
    int? MyManaAfterReadySpells,
    // Allies within fight range: the healing their ready spells can give (ally cooldowns are on screen).
    int? AlliesReadyHealingNear,
    // Living enemies: the damage of their learned spells at their levels, cooldowns and mana ignored (the levels can be
    // seen by selecting the hero).
    int? EnemyKitDamageAlive,
    // Hidden: the enemies within fight range, with their real cooldowns and mana.
    int? HiddenEnemiesReadyDamageNear,
    int? HiddenEnemiesReadyHealingNear,
    // The hero's own ready spells by shape and reach (talents learned and Scepter/Shard bought up to now applied):
    // how many hit an area, how many hit one unit, the longest cast range, and how many disables reach the nearest
    // enemy the team sees (range plus area).
    int? MyReadyAreaSpells = null,
    int? MyReadySingleTargetSpells = null,
    int? MyReadySpellMaxRange = null,
    int? MyReadyDisablesReachingSeenEnemy = null,
    // The living enemies' learned disables (visible: levels and talents can be inspected): the longest reach, how many
    // a basic dispel cannot remove, how many go through spell immunity (Black King Bar does not stop them).
    int? EnemyKitDisableMaxRange = null,
    int? EnemyKitUndispellableDisables = null,
    int? EnemyKitImmunityPiercingDisables = null,
    // Hidden: the enemies' ready spells (real cooldowns and mana) whose range plus area reaches the hero now, the
    // disables among them, and their damage.
    int? HiddenEnemyReadySpellsReachingMe = null,
    int? HiddenEnemyReadyDisablesReachingMe = null,
    int? HiddenEnemyReadyDamageReachingMe = null)
{
    public static SpellPower Unknown { get; } = new(null, null, null, null, null, null, null, null);
}

// The hero's own tools at this second, from the inventory read every 2 s and the cooldowns that started up to then
// (the player sees all of this on screen). Null when the document has no inventories/cooldowns.
public sealed record Readiness(
    // A teleport (scroll in the teleport slot, or boots of travel) with no cooldown running.
    bool? TeleportReady,
    // An escape or save item (HeroSituationBuilder.EscapeItems) in the inventory, and one of them with no cooldown.
    bool? HasEscapeItem,
    bool? EscapeItemReady,
    // The ultimate, taken as the ability with the longest cooldown seen up to this second (a heuristic: the replay data
    // does not say which one is the ultimate); null until the hero has used it once.
    bool? UltimateReady,
    // How many of the hero's abilities had a cooldown running.
    int? AbilitiesOnCooldown)
{
    public static Readiness Unknown { get; } = new(null, null, null, null, null);
}

// The neutral items at this second (7.38+: crafted from Madstones, one tier at a time). Visible: the tier open (the game
// clock and the build's rules), the hero's own artifact, enhancement, Madstones and crafts. Hidden: the enemies'.
// Null where the build's neutral rules (NeutralRules), the slots or the crafts/Madstones (documents from before N2) are
// missing.
public sealed record NeutralState(
    // The highest tier open now and the seconds until the next opens (null when every tier is open).
    int? TierOpen,
    int? SecondsToNextTier,
    // The tier of the artifact in the neutral slot (0 = none), how many tiers behind the open one, its active ready.
    int? MyArtifactTier,
    int? MyTiersBehind,
    bool? MyArtifactActiveReady,
    // The enhancement's level (0 = none), from the last craft.
    int? MyEnhancementLevel,
    // Madstones in hand; whether they pay for the open tier not crafted yet; seconds since the last craft.
    int? MyMadstones,
    bool? CanCraftNow,
    int? SecondsSinceLastCraft,
    // Hidden: enemies within fight range with an active artifact ready, the living enemies' Madstones in hand, and how
    // many tiers behind the open one the enemies' artifacts are on average.
    int? HiddenEnemyArtifactActivesReadyNear,
    int? HiddenEnemyMadstones,
    double? HiddenEnemyTiersBehind)
{
    public static NeutralState Unknown { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null);
}
