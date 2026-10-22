namespace DotaAnalyst.Domain.Heroes;

public enum DefenseMechanism
{
    Unknown, FlatArmorReduction, PercentBaseArmorReduction, IgnoreBaseArmor, IgnoreTotalArmor,
    MagicResistanceReduction, DamageAmplification, DamageReduction, Block, Barrier,
    Evasion, Accuracy, CriticalStrike, Break, DebuffImmunity,
}

// Reviewed identities only. Lexical matches, obsolete facets and unknown values never become a mechanic automatically.
// Each rule was reviewed against one version of its hero's game file (Client 6962, 15/10/2026) and holds only while a
// match's catalog has that same version: Drow Ranger's file changed inside 7.41f without an official sub-patch, so
// neither the build nor the sub-patch identifies what was reviewed.
public sealed record DefenseMechanic(string AbilityKey, DefenseMechanism Mechanism, string? ValueKey,
    string Condition, string Source)
{
    private static readonly Dictionary<string, (string File, string Version, DefenseMechanic Rule)> Reviewed = new()
    {
        ["slardar_amplify_damage"] = ("scripts/npc/heroes/npc_dota_hero_slardar.txt", "3f77c59be82478d6468366d26c644b76286ead50",
            new("slardar_amplify_damage", DefenseMechanism.FlatArmorReduction, "armor_reduction",
                "Observed active debuff and verified magnitude on the target", "gameFiles:abilityDescription")),
        ["elder_titan_natural_order"] = ("scripts/npc/heroes/npc_dota_hero_elder_titan.txt", "262dddd9425087018b61626785e8dbbd61c68bdb",
            new("elder_titan_natural_order", DefenseMechanism.PercentBaseArmorReduction, "armor_reduction_pct",
                "Observed hero aura on target; spirit magic-resistance aura is separate", "gameFiles:abilityDescription")),
        ["drow_ranger_marksmanship"] = ("scripts/npc/heroes/npc_dota_hero_drow_ranger.txt", "462efa9fe61e59644e2c84465daca3a0d03fafee",
            new("drow_ranger_marksmanship", DefenseMechanism.IgnoreBaseArmor, null,
                "Confirmed proc with passive active; not every attack", "gameFiles:abilityDescription")),
    };

    // The reviewed rule of an ability when the catalog's file versions (GameCatalog.FileVersions) have the reviewed
    // version of its hero's file; null otherwise (unknown, including the current catalogs, which have no versions).
    public static DefenseMechanic? For(string abilityKey, IReadOnlyDictionary<string, string>? fileVersions) =>
        Reviewed.TryGetValue(abilityKey, out var reviewed) && fileVersions?.GetValueOrDefault(reviewed.File) == reviewed.Version
            ? reviewed.Rule
            : null;
}

// Hypothetical single-effect context. ArmorBeforeEffect excludes this effect. NormalizedMagnitude is a positive
// reduction supplied by the caller, NOT the raw signed AbilityValue (Slardar uses negative raw values).
// No automatic stacking, proc inference or conversion from catalog parameters is performed. FileVersions: those of the
// match's catalog (GameCatalog.FileVersions).
public sealed record ArmorInteractionContext(IReadOnlyDictionary<string, string>? FileVersions, int SourceHeroId, int TargetHeroId,
    int Seconds, double? ArmorBeforeEffect, double? BaseArmor, double? NormalizedMagnitude,
    bool? EffectActive, bool? ProcOccurred, bool InputsVerified);
public sealed record ArmorInteractionResult(double? ArmorForThisAttack, string Status);

public static class DefenseInteractions
{
    public static ArmorInteractionResult ArmorFor(string abilityKey, ArmorInteractionContext context)
    {
        var rule = DefenseMechanic.For(abilityKey, context.FileVersions);
        if (context.SourceHeroId <= 0 || context.TargetHeroId <= 0 || context.Seconds < 0 || rule is null || !context.InputsVerified || context.EffectActive is null || context.ArmorBeforeEffect is not { } total
            || !double.IsFinite(total)) return new(null, "unknown");
        if (context.EffectActive == false) return new(total, "noActiveEffect");
        double? result = rule.Mechanism switch
        {
            DefenseMechanism.FlatArmorReduction when context.NormalizedMagnitude is >= 0 and var amount => total - amount,
            DefenseMechanism.PercentBaseArmorReduction when context.BaseArmor is >= 0 and var baseline && context.NormalizedMagnitude is >= 0 and <= 100 =>
                total - baseline * context.NormalizedMagnitude / 100,
            DefenseMechanism.IgnoreBaseArmor when context.BaseArmor is >= 0 and var baseline && context.ProcOccurred == true => total - baseline,
            DefenseMechanism.IgnoreBaseArmor when context.ProcOccurred == false => total,
            _ => null,
        };
        return result is { } armor && double.IsFinite(armor) ? new(armor, "hypotheticalFromCallerInputs") : new(null, "unknown");
    }
}
