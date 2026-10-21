namespace DotaAnalyst.Domain.Replays;

// Domain service: what a buff does, from the parser's lexical kind (teleport, smoke, invisible, magicImmune, save, heal,
// other) and the modifier's name, like DebuffKinds. Most buffs of replay 9034320024 came as "other" (957 of 1,430: auras
// such as item_ring_of_basilius_effect, storm_spirit_overload, tidehunter_anchor_smash_caster); the lists below give a
// kind to the ones that change a fight. A heuristic over names: a name it does not recognise stays Other.
public static class BuffKinds
{
    // Checked in this order: the first list with a part in the name wins.
    private static readonly (BuffKind Kind, string[] Parts)[] ByName =
    [
        (BuffKind.Teleport, ["teleport", "travel_boots"]),
        (BuffKind.Smoke, ["smoke_of_deceit"]),
        (BuffKind.MagicImmune, ["black_king_bar", "magic_immune", "life_stealer_rage", "blade_fury", "omniknight_repel"]),
        (BuffKind.Save, ["glimmer", "ghost_state", "item_ghost", "cyclone", "aeon_disk", "lotus_orb", "manta",
            "shallow_grave", "false_promise", "guardian_angel", "invulnerab"]),
        (BuffKind.Invisible, ["invis", "shadow_walk", "shadow_blade", "silver_edge"]),
        (BuffKind.Heal, ["regen", "flask", "salve", "clarity", "tango", "bottle", "mekansm", "greaves", "heal"]),
        (BuffKind.Speed, ["haste", "phase_boots", "sprint", "windrun", "surge"]),
    ];

    public static BuffKind Of(string parserKind, string modifier)
    {
        var name = modifier.ToLowerInvariant();
        // The fountain's invulnerability and regeneration only say the hero is home (the position already does).
        if (name.Contains("fountain"))
        {
            return BuffKind.Other;
        }

        var fromParser = parserKind switch
        {
            "teleport" => BuffKind.Teleport,
            "smoke" => BuffKind.Smoke,
            "invisible" => BuffKind.Invisible,
            "magicImmune" => BuffKind.MagicImmune,
            "save" => BuffKind.Save,
            "heal" => BuffKind.Heal,
            _ => BuffKind.Other,
        };
        if (fromParser != BuffKind.Other)
        {
            return fromParser;
        }

        foreach (var (kind, parts) in ByName)
        {
            if (parts.Any(name.Contains))
            {
                return kind;
            }
        }

        return BuffKind.Other;
    }
}
