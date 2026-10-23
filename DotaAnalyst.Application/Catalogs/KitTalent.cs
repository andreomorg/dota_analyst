using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>A talent in the hero's talent tree.</summary>
/// <param name="Key">Internal talent name.</param>
/// <param name="Tier">Game tier, normally 1 (level 10) through 4 (level 25).</param>
/// <param name="Name">Game text, with numbers as {s:...} placeholders; null when unavailable.</param>
public sealed record KitTalent(string Key, int Tier, string? Name);
