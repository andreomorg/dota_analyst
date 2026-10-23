using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>A hero facet from the constants.</summary>
/// <param name="Id">Facet identifier.</param>
/// <param name="Key">Internal facet name.</param>
/// <param name="Title">Display title.</param>
/// <param name="Description">Game description, with numbers as {s:...} placeholders.</param>
/// <param name="Deprecated">Whether the facet is no longer pickable.</param>
/// <param name="Icon">Game icon name, when available.</param>
/// <param name="Color">Game color name, when available.</param>
/// <param name="Abilities">Keys of the abilities the facet grants.</param>
public sealed record KitFacet(int Id, string Key, string? Title, string? Description, bool Deprecated,
    string? Icon, string? Color, IReadOnlyList<string> Abilities);
