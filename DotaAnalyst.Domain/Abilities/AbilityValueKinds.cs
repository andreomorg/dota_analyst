namespace DotaAnalyst.Domain.Abilities;

// What an ability attribute measures, for the numbers a player asks about first: how much damage it deals, how much
// health it gives back, and how much health it costs the caster (the mana cost is its own list on AbilityDetails).
public enum AbilityValueKind
{
    Other,
    Damage,
    Healing,
    HealthCost,
}

// Domain service: classifies an attribute by the words of its game header (key when there is no header). Checked
// against every attribute of the 7.41 constants: "BONUS HEALTH" is not healing (whole words, not "HEAL" inside
// "HEALTH"), and words about amplification, shields, ranges or timing ("HEAL AMPLIFICATION", "DAMAGE ABSORB",
// "DAMAGE TICK RATE", "ILLUSION DAMAGE") are not an amount dealt or healed.
public static class AbilityValueKinds
{
    private static readonly HashSet<string> NotAnAmount =
    [
        "REDUCTION", "AMPLIFICATION", "AMP", "TAKEN", "RESISTANCE", "RESIST", "BLOCK", "THRESHOLD", "WINDOW", "RADIUS",
        "RANGE", "DURATION", "INTERVAL", "TIME", "RATE", "CHANCE", "DELAY", "SPEED", "ILLUSION", "INCOMING", "OUTGOING",
        "INCREASED", "ABSORB", "DISTANCE", "COOLDOWN", "STACKS", "MANA",
    ];

    private static readonly HashSet<string> HealingWords = ["HEAL", "HEALS", "HEALING", "LIFESTEAL"];

    public static AbilityValueKind Of(AbilityValue value)
    {
        var key = value.Key.ToLowerInvariant();
        var header = (value.Header ?? value.Key.Replace('_', ' ')).ToUpperInvariant();
        if (key.Contains("health_cost") || key.Contains("healthcost") || key.Contains("hp_cost") || key.Contains("self_damage")
            || header.Contains("HEALTH COST") || header.Contains("HP COST") || header.Contains("SELF DAMAGE"))
            return AbilityValueKind.HealthCost;
        var words = header.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(NotAnAmount.Contains)) return AbilityValueKind.Other;
        if (words.Any(HealingWords.Contains) || header.Contains("HP REGEN") || header.Contains("HEALTH REGEN")
            || header.Contains("HP RESTORE") || header.Contains("HEALTH RESTORE"))
            return AbilityValueKind.Healing;
        return words.Contains("DAMAGE") || words.Contains("DPS") ? AbilityValueKind.Damage : AbilityValueKind.Other;
    }

    private static readonly char[] Separators = [' ', ':', '/', '_', '%', '(', ')', '-', '+', ','];
}
