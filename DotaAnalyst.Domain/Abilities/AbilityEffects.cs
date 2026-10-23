using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Domain.Abilities;

// What an ability or item does in a fight.
public enum AbilityEffect
{
    Stun,
    Root,
    Silence,
    Hex,
    Disarm,
    Fear,
    Taunt,
    Sleep,
    Slow,
    Nuke,
    DamageOverTime,
    ArmorReduction,
    Heal,
    Shield,
    Save,
    Escape,
    Invisibility,
    SpellImmunity,
    Dispel,
    Summon,
    Vision,
    ForcedMovement,
}

// Domain service: the effects of an ability or item, from the game data of the current patch (AbilityDetails /
// ItemDetails). The constants have no "effect" field, so it reads, in order:
// - the attribute headers the game shows ("STUN DURATION", "MOVEMENT SLOW", "ROOT DURATION", "DAMAGE PER SECOND"...);
// - the description's words ("stuns", "silences", "invisible", "teleports", "applies a basic dispel"...);
// - the internal key (lion_voodoo is a hex) and, for the common items, an explicit list (the item texts do not say
//   "spell immunity" for Black King Bar, nor "escape" for Blink Dagger).
// A heuristic over the game's own words: an effect it does not recognise is simply left out; it never invents one.
public static partial class AbilityEffects
{
    public static bool IsDisable(AbilityEffect effect) =>
        effect is AbilityEffect.Stun or AbilityEffect.Root or AbilityEffect.Silence or AbilityEffect.Hex or AbilityEffect.Disarm
            or AbilityEffect.Fear or AbilityEffect.Taunt or AbilityEffect.Sleep;

    private static readonly (string Part, AbilityEffect Effect)[] Headers =
    [
        ("STUN", AbilityEffect.Stun), ("ROOT", AbilityEffect.Root), ("ENSNARE", AbilityEffect.Root),
        ("SILENCE", AbilityEffect.Silence), ("HEX", AbilityEffect.Hex), ("DISARM", AbilityEffect.Disarm),
        ("FEAR", AbilityEffect.Fear), ("TAUNT", AbilityEffect.Taunt), ("SLEEP", AbilityEffect.Sleep),
        ("MOVEMENT SLOW", AbilityEffect.Slow), ("MOVE SLOW", AbilityEffect.Slow), ("ATTACK SLOW", AbilityEffect.Slow),
        ("ATTACK SPEED SLOW", AbilityEffect.Slow), ("SLOW DURATION", AbilityEffect.Slow),
        ("DAMAGE PER SECOND", AbilityEffect.DamageOverTime), ("BURN", AbilityEffect.DamageOverTime),
        ("ARMOR REDUCTION", AbilityEffect.ArmorReduction), ("HEAL", AbilityEffect.Heal), ("HP REGEN", AbilityEffect.Heal),
        ("BARRIER", AbilityEffect.Shield), ("SHIELD", AbilityEffect.Shield), ("DAMAGE BLOCK", AbilityEffect.Shield),
        ("ILLUSION", AbilityEffect.Summon), ("KNOCKBACK", AbilityEffect.ForcedMovement),
    ];

    private static readonly (string Part, AbilityEffect Effect)[] Words =
    [
        ("stun", AbilityEffect.Stun), ("roots", AbilityEffect.Root), ("rooted", AbilityEffect.Root), ("ensnare", AbilityEffect.Root),
        ("silence", AbilityEffect.Silence), ("hex", AbilityEffect.Hex), ("critter", AbilityEffect.Hex), ("disarm", AbilityEffect.Disarm),
        ("fear", AbilityEffect.Fear), ("taunt", AbilityEffect.Taunt), ("sleep", AbilityEffect.Sleep), ("cyclone", AbilityEffect.Stun),
        ("slows", AbilityEffect.Slow), ("slowing", AbilityEffect.Slow), ("slowed", AbilityEffect.Slow),
        // Not "per second": healing and regeneration say it too.
        ("damage over time", AbilityEffect.DamageOverTime),
        ("armor reduction", AbilityEffect.ArmorReduction), ("reduces armor", AbilityEffect.ArmorReduction),
        ("heals", AbilityEffect.Heal), ("restores health", AbilityEffect.Heal), ("barrier", AbilityEffect.Shield),
        ("invulnerab", AbilityEffect.Save), ("banish", AbilityEffect.Save), ("untargetable", AbilityEffect.Save),
        ("teleport", AbilityEffect.Escape), ("blink", AbilityEffect.Escape), ("leaps", AbilityEffect.Escape),
        ("dashes", AbilityEffect.Escape), ("jumps", AbilityEffect.Escape),
        ("invisible", AbilityEffect.Invisibility), ("invisibility", AbilityEffect.Invisibility),
        ("spell immunity", AbilityEffect.SpellImmunity), ("debuff immunity", AbilityEffect.SpellImmunity),
        ("applies a basic dispel", AbilityEffect.Dispel), ("applies a strong dispel", AbilityEffect.Dispel),
        ("dispels", AbilityEffect.Dispel), ("purges", AbilityEffect.Dispel),
        // Not "illusion": many texts say what does not work on illusions (the ILLUSION DURATION header still counts).
        ("summons", AbilityEffect.Summon),
        ("true sight", AbilityEffect.Vision), ("reveals", AbilityEffect.Vision), ("knocks back", AbilityEffect.ForcedMovement),
        ("pulls", AbilityEffect.ForcedMovement), ("pushes", AbilityEffect.ForcedMovement),
    ];

    private static readonly (string Part, AbilityEffect Effect)[] KeyParts =
    [
        ("voodoo", AbilityEffect.Hex), ("sheepstick", AbilityEffect.Hex), ("_hex", AbilityEffect.Hex),
        ("blink", AbilityEffect.Escape), ("invis", AbilityEffect.Invisibility),
    ];

    // The common items whose effect the item text does not name plainly.
    private static readonly Dictionary<string, AbilityEffect[]> Items = new()
    {
        ["black_king_bar"] = [AbilityEffect.SpellImmunity, AbilityEffect.Dispel],
        ["blink"] = [AbilityEffect.Escape],
        ["overwhelming_blink"] = [AbilityEffect.Escape, AbilityEffect.Slow, AbilityEffect.Nuke],
        ["swift_blink"] = [AbilityEffect.Escape],
        ["arcane_blink"] = [AbilityEffect.Escape],
        ["force_staff"] = [AbilityEffect.Escape, AbilityEffect.ForcedMovement],
        ["hurricane_pike"] = [AbilityEffect.Escape, AbilityEffect.ForcedMovement],
        ["glimmer_cape"] = [AbilityEffect.Invisibility, AbilityEffect.Save],
        ["ghost"] = [AbilityEffect.Save],
        ["cyclone"] = [AbilityEffect.Stun, AbilityEffect.Save, AbilityEffect.Dispel],
        ["wind_waker"] = [AbilityEffect.Stun, AbilityEffect.Save, AbilityEffect.Dispel, AbilityEffect.Escape],
        ["sheepstick"] = [AbilityEffect.Hex],
        ["orchid"] = [AbilityEffect.Silence],
        ["bloodthorn"] = [AbilityEffect.Silence],
        ["rod_of_atos"] = [AbilityEffect.Root],
        ["gungir"] = [AbilityEffect.Root],
        ["abyssal_blade"] = [AbilityEffect.Stun],
        ["heavens_halberd"] = [AbilityEffect.Disarm],
        ["lotus_orb"] = [AbilityEffect.Dispel, AbilityEffect.Save],
        ["aeon_disk"] = [AbilityEffect.Save, AbilityEffect.Dispel],
        ["sphere"] = [AbilityEffect.Save],
        ["shadow_amulet"] = [AbilityEffect.Invisibility],
        ["invis_sword"] = [AbilityEffect.Invisibility, AbilityEffect.Escape],
        ["silver_edge"] = [AbilityEffect.Invisibility, AbilityEffect.Escape],
        ["manta"] = [AbilityEffect.Dispel, AbilityEffect.Summon],
        ["nullifier"] = [AbilityEffect.Dispel, AbilityEffect.Slow],
        ["diffusal_blade"] = [AbilityEffect.Slow],
        ["disperser"] = [AbilityEffect.Slow, AbilityEffect.Dispel],
        ["ethereal_blade"] = [AbilityEffect.Save, AbilityEffect.Slow, AbilityEffect.Nuke],
        ["dagon"] = [AbilityEffect.Nuke],
        ["shivas_guard"] = [AbilityEffect.Slow, AbilityEffect.Nuke],
        ["mekansm"] = [AbilityEffect.Heal],
        ["guardian_greaves"] = [AbilityEffect.Heal, AbilityEffect.Dispel],
        ["pipe"] = [AbilityEffect.Shield],
        ["crimson_guard"] = [AbilityEffect.Shield],
        ["satanic"] = [AbilityEffect.Heal],
        ["solar_crest"] = [AbilityEffect.ArmorReduction],
        ["medallion_of_courage"] = [AbilityEffect.ArmorReduction],
        ["spirit_vessel"] = [AbilityEffect.DamageOverTime, AbilityEffect.Heal],
        ["urn_of_shadows"] = [AbilityEffect.DamageOverTime, AbilityEffect.Heal],
        ["tpscroll"] = [AbilityEffect.Escape],
        ["travel_boots"] = [AbilityEffect.Escape],
        ["travel_boots_2"] = [AbilityEffect.Escape],
        ["magic_wand"] = [AbilityEffect.Heal],
        ["bottle"] = [AbilityEffect.Heal],
        ["gem"] = [AbilityEffect.Vision],
        ["ward_sentry"] = [AbilityEffect.Vision],
        ["dust"] = [AbilityEffect.Vision],
        ["ward_observer"] = [AbilityEffect.Vision],
    };

    private static readonly string[] DamageHeaders = ["DAMAGE", "BASE DAMAGE", "IMPACT DAMAGE", "INITIAL DAMAGE", "MAX DAMAGE"];

    public static IReadOnlySet<AbilityEffect> Of(AbilityDetails ability, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (version == 2) return OfV2(ability);
        ArgumentOutOfRangeException.ThrowIfNotEqual(version, 1);
        var effects = Read(ability.Key, ability.Description, ability.Values);
        // A damage attribute on an active enemy-targeted spell with a damage type: a nuke (passives/auras excluded).
        if (!ability.IsPassive && ability.DamageType != AbilityDamageType.None
            && ability.TargetTeam is AbilityTargetTeam.Enemy or AbilityTargetTeam.Both or AbilityTargetTeam.None
            && ability.Values.Any(value => DamageHeaders.Contains(Header(value))))
        {
            effects.Add(AbilityEffect.Nuke);
        }

        return effects;
    }

    public static IReadOnlySet<AbilityEffect> Of(ItemDetails item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var text = string.Join(' ', item.Abilities.Where(ability => ability.Kind is ItemAbilityKind.Active or ItemAbilityKind.Use)
            .Select(ability => ability.Description));
        var effects = Read(item.Key, text, item.Values);
        if (Items.TryGetValue(item.Key, out var known))
        {
            effects.UnionWith(known);
        }

        return effects;
    }

    private static HashSet<AbilityEffect> Read(string key, string? description, IEnumerable<AbilityValue> values)
    {
        var effects = new HashSet<AbilityEffect>();
        foreach (var header in values.Select(Header))
        {
            // "SLOW RESISTANCE"/"STATUS RESISTANCE" are the hero's own defense, not a debuff it applies.
            if (header.Contains("RESISTANCE", StringComparison.Ordinal))
            {
                continue;
            }

            effects.UnionWith(Headers.Where(entry => header.Contains(entry.Part, StringComparison.Ordinal)).Select(entry => entry.Effect));
        }

        if (description is { Length: > 0 } text)
        {
            var lower = text.ToLowerInvariant();
            effects.UnionWith(Words.Where(entry => lower.Contains(entry.Part, StringComparison.Ordinal)).Select(entry => entry.Effect));
        }

        effects.UnionWith(KeyParts.Where(entry => key.Contains(entry.Part, StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Effect));
        return effects;
    }

    private static string Header(AbilityValue value) => (value.Header ?? value.Key).Trim().TrimEnd(':').Trim().ToUpperInvariant();
}
