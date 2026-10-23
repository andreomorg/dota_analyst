namespace DotaAnalyst.Application.Matches;

/// <summary>Heuristics of play quality (not proof that a cast caused the result).</summary>
/// <param name="SkillshotUses">Casts of the listed skillshots (Hook, Sacred Arrow...); null when unknown.</param>
/// <param name="SkillshotHits">Of those, the ones that hit an enemy hero; null when unknown.</param>
/// <param name="LowHealthAllyUses">Save buffs put on an ally at 35% health or less; null when unknown.</param>
/// <param name="Saves">Of those, the allies that survived the next 10 s; null when unknown.</param>
/// <param name="DispelledDebuffs">Disables on the hero that ended shortly after a dispel; null when unknown.</param>
public record PlaysSummary(int? SkillshotUses, int? SkillshotHits, int? LowHealthAllyUses, int? Saves, int? DispelledDebuffs);
