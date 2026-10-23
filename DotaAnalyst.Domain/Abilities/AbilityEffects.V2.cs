using System.Text.RegularExpressions;

namespace DotaAnalyst.Domain.Abilities;

public static partial class AbilityEffects
{
    private static bool Matches(string text, string pattern) => Regex.IsMatch(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static HashSet<AbilityEffect> OfV2(AbilityDetails ability)
    {
        var effects = new HashSet<AbilityEffect>();
        var text = ability.Description ?? "";
        // Remove prohibitions before classifying positive effects. Movement/attack restrictions are read below.
        var positive = Regex.Replace(text,
            @"\b(?:prevent\w*|cannot|can't|unable to)\b[^.;\n]*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var (part, effect) in Words)
        {
            if (effect == AbilityEffect.Summon) continue;
            if (Matches(positive, @"\b" + Regex.Escape(part) + @"\w*\b")) effects.Add(effect);
        }

        foreach (var header in ability.Values.Select(Header))
        {
            if (Matches(header, @"RESISTANCE|^SELF |REDUCTION.*HEAL|HEAL.*(?:REDUCTION|AMPLIFICATION)|RESTORATION REDUCTION")) continue;
            foreach (var (part, effect) in Headers)
            {
                if (effect is AbilityEffect.Heal or AbilityEffect.Summon || part == "BURN") continue;
                if (header.Contains(part, StringComparison.Ordinal)) effects.Add(effect);
            }
            if (Matches(header, @"^(?:HEAL|HEALING|HP REGEN)(?:$|[ /])") && !Matches(header, @"AMPLIFICATION|REDUCTION"))
                effects.Add(AbilityEffect.Heal);
            if (Matches(header, @"^VISION (?:AOE|RANGE|RADIUS)$")) effects.Add(AbilityEffect.Vision);
            if (header == "DISPEL TYPE") effects.Add(AbilityEffect.Dispel);
        }
        foreach (var (part, effect) in KeyParts)
            if (ability.Key.Contains(part, StringComparison.OrdinalIgnoreCase)) effects.Add(effect);

        if (Matches(positive, @"\broot(?:s|ed|ing)?\b|\bensnar\w*" ) || Matches(text, @"prohibiting movement|cannot move"))
            effects.Add(AbilityEffect.Root);
        if (Matches(text, @"prohibiting movement and attack|cannot (?:move or )?attack")) effects.Add(AbilityEffect.Disarm);
        if (Matches(positive, @"\b(?:basic|strong) dispel\b|removes? (?:all )?debuffs|dispelling")) effects.Add(AbilityEffect.Dispel);
        if (Matches(positive, @"immune from harm|cannot die|immunity from physical damage" ) || Matches(text, @"cannot die"))
            effects.Add(AbilityEffect.Save);
        if (Matches(positive, @"\b(?:creates?|summons?) (?:\w+ ){0,3}(?:images|illusions|wards|units|creatures)\b")) effects.Add(AbilityEffect.Summon);
        if (Matches(positive, @"damage (?:every|per) second|periodic(?:ally)? damage")) effects.Add(AbilityEffect.DamageOverTime);
        if (Matches(positive, @"drag(?:s|ged|ging)|swaps? positions")) effects.Add(AbilityEffect.ForcedMovement);
        if (Matches(positive, @"rushes forward")) effects.Add(AbilityEffect.Escape);
        if (!ability.IsPassive && ability.DamageType != AbilityDamageType.None && ability.AffectsEnemies
            && (ability.BaseDamage.Count > 0 || ability.Values.Any(v => DamageHeaders.Contains(Header(v))
                || Matches(Header(v), @"^HEAL/DAMAGE$|^DAMAGE/SHIELD$|^DAMAGE PER 1 MANA MISSING$"))))
            effects.Add(AbilityEffect.Nuke);

        // CD14 local 7.41 descriptions: these identities specify control/periodicity not expressed by headers.
        switch (ability.Key)
        {
            // The invisible subject is the enchanted tree/eyes, not Treant (CD14 tooltip).
            case "treant_eyes_in_the_forest": effects.Remove(AbilityEffect.Invisibility); break;
            case "shadow_shaman_shackles":
            case "bane_fiends_grip":
            case "pudge_dismember":
                effects.UnionWith([AbilityEffect.Stun, AbilityEffect.DamageOverTime]);
                effects.Remove(AbilityEffect.Root); effects.Remove(AbilityEffect.Disarm);
                effects.Remove(AbilityEffect.Nuke); break;
            case "treant_overgrowth":
                effects.UnionWith([AbilityEffect.Root, AbilityEffect.Disarm, AbilityEffect.DamageOverTime]); break;
            case "naga_siren_song_of_the_siren": effects.Add(AbilityEffect.Sleep); break;
            case "legion_commander_duel": effects.Add(AbilityEffect.Taunt); break;
            case "oracle_false_promise": effects.Add(AbilityEffect.Save); effects.Add(AbilityEffect.Dispel); break;
            case "bane_enfeeble": effects.Add(AbilityEffect.DamageOverTime); effects.Remove(AbilityEffect.Nuke); break;
            case "pudge_rot": effects.Add(AbilityEffect.DamageOverTime); effects.Remove(AbilityEffect.Nuke); break;
            case "vengefulspirit_nether_swap": effects.Add(AbilityEffect.Save); break;
        }
        return effects;
    }
}
