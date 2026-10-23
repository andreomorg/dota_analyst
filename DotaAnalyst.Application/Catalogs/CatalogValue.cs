using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>An attribute's numeric levels or original nonnumeric text.</summary>
/// <param name="Key">Internal attribute key.</param>
/// <param name="Header">Display label from the game.</param>
/// <param name="Levels">Numbers in level order; empty for nonnumeric values.</param>
/// <param name="Text">Original nonnumeric value, if any.</param>
/// <param name="IsPercent">Whether the game writes the numbers as percentages ("15%").</param>
/// <param name="Generated">Whether the value was filled in from the ability's own fields instead of the tooltip.</param>
/// <param name="Kind">What the value measures: damage dealt, healing, health the caster pays, or other.</param>
public sealed record CatalogValue(string Key, string? Header, IReadOnlyList<double> Levels, string? Text,
    bool IsPercent, bool Generated, AbilityValueKind Kind)
{
    internal static CatalogValue From(AbilityValue value) =>
        new(value.Key, value.Header, value.Levels, value.Text, value.IsPercent, value.Generated, value.Kind);
}
