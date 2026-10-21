namespace DotaAnalyst.Domain.Replays;

// Domain service: what a debuff does, from the parser's lexical kind (stun, silence, root, hex, slow, other) and the
// modifier's name. The parser only knows names that say it (`..._stun`, `..._slow`); here the common "other" ones get a
// kind too, by small explicit lists of name parts. It is a heuristic over names, not a per-patch ability database: a
// name it does not recognise stays Other. The first lists were built from the most frequent debuffs of replay
// 9034320024 (storm_spirit_overload_debuff, ancient_apparition_bone_chill_debuff, spectre_spectral_dagger_in_path,
// orb_of_corrosion_debuff, item_radiance_debuff...).
public static class DebuffKinds
{
    // Checked in this order: the first list with a part in the name wins.
    private static readonly (DebuffKind Kind, string[] Parts)[] ByName =
    [
        (DebuffKind.Stun, ["stun", "bash", "sleep", "cyclone", "taunt", "fear"]),
        (DebuffKind.Hex, ["hex", "voodoo", "sheepstick"]),
        (DebuffKind.Silence, ["silence", "orchid", "bloodthorn", "doom_bringer_doom"]),
        (DebuffKind.Root, ["root", "ensnare", "entangle", "rod_of_atos", "gleipnir"]),
        (DebuffKind.ArmorReduction, ["corrosion", "armor", "desolator", "assault", "medallion", "solar_crest", "acid"]),
        (DebuffKind.DamageOverTime, ["radiance", "burn", "poison", "venom", "_dot", "ignite", "decay", "spirit_vessel", "urn_"]),
        (DebuffKind.Slow, ["slow", "frost", "chill", "cold", "overload", "spectral_dagger", "maim", "cripple"]),
    ];

    public static DebuffKind Of(string parserKind, string modifier)
    {
        var fromParser = parserKind switch
        {
            "stun" => DebuffKind.Stun,
            "hex" => DebuffKind.Hex,
            "silence" => DebuffKind.Silence,
            "root" => DebuffKind.Root,
            "slow" => DebuffKind.Slow,
            _ => DebuffKind.Other,
        };
        if (fromParser != DebuffKind.Other)
        {
            return fromParser;
        }

        var name = modifier.ToLowerInvariant();
        foreach (var (kind, parts) in ByName)
        {
            if (parts.Any(name.Contains))
            {
                return kind;
            }
        }

        return DebuffKind.Other;
    }
}
