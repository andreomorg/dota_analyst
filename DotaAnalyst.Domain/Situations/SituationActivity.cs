using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// Seconds since something was used (null: not in the last HeroSituationBuilder.RecentWindowSeconds, or never). A use
// says it happened, not that the item is now on cooldown: the recharge is unknown here.
public sealed record RecentActivity(
    // A Smoke of Deceit used by this hero's team (the player sees the team's smoke).
    int? OwnSmokeSecondsAgo,
    // This hero's last teleport (scroll or boots of travel).
    int? TeleportSecondsAgo,
    // This hero's last escape or save item (Blink, Force Staff, Glimmer Cape, Black King Bar...; HeroSituationBuilder).
    int? EscapeItemSecondsAgo,
    // How many items and abilities this hero used in the last 30 s: how busy the moment was.
    int ItemUsesLast30Seconds,
    int AbilityUsesLast30Seconds,
    // Damage taken from and dealt to enemy heroes in the last HeroSituationBuilder.HitWindowSeconds (finished 2 s
    // buckets only); null for documents without combat buckets.
    int? DamageFromHeroesRecently = null,
    int? DamageToHeroesRecently = null,
    // This hero's buyback within HeroSituationBuilder.BuybackCooldownSeconds (it then has no buyback), the team's glyph
    // within GlyphCooldownSeconds, the team's scan, and a power rune this hero took (last PowerRuneWindowSeconds).
    int? OwnBuybackSecondsAgo = null,
    int? OwnGlyphSecondsAgo = null,
    int? OwnScanSecondsAgo = null,
    int? OwnPowerRuneSecondsAgo = null)
{
    public static RecentActivity Unknown { get; } = new(null, null, null, 0, 0);
}

// The enemy debuffs on the hero (Debuff, DebuffKinds): how many disables (stun, hex, silence, root), slows and others
// were on it at this second, the seconds it spent disabled in the last HeroSituationBuilder.AfflictionWindowSeconds, and
// how many different enemies put something on it then. Null when the document has no debuffs.
public sealed record Afflictions(int? Disables, int? Slows, int? Others, double? DisabledSeconds, int? EnemiesAfflicting)
{
    public static Afflictions Unknown { get; } = new(null, null, null, null, null);
}

// The buffs on the hero at this second (Buff, BuffKinds): channelling a teleport, magic immune, under a save (Glimmer,
// Eul's, Aeon Disk...), how many heals and speed buffs, and how many different allies put a buff on it in the last
// HeroSituationBuilder.AfflictionWindowSeconds. Null when the document has no buffs.
public sealed record Boons(bool? Teleporting, bool? MagicImmune, bool? Saved, int? Heals, int? SpeedBuffs, int? AlliesBuffing)
{
    public static Boons Unknown { get; } = new(null, null, null, null, null, null);
}

// The player's orders in the last HeroSituationBuilder.IntentWindowSeconds (HeroOrders of the hero's samples then): how
// many moves, attacks and casts, and the last move destination — how far from the hero, how much further towards the
// enemy base it is (map progress from the own side, positive = forward) and whether it is on the enemy side. Null when
// the document has no orders (the destination ones also when there was no move order).
public sealed record Intent(
    int? Moves, int? Attacks, int? Casts, int? DestinationDistance, int? DestinationAdvance, bool? DestinationOnEnemySide)
{
    public static Intent Unknown { get; } = new(null, null, null, null, null, null);
}
