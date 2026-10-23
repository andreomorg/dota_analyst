using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>The full game data available for an item.</summary>
/// <param name="Id">Numeric item identifier.</param>
/// <param name="Key">Internal item key without item_.</param>
/// <param name="Name">Display name.</param>
/// <param name="Cost">Gold cost.</param>
/// <param name="Components">Component keys.</param>
/// <param name="IsConsumable">Whether consumed rather than used in a build.</param>
/// <param name="IsRecipe">Whether this is a recipe.</param>
/// <param name="Abilities">Active and passive effects.</param>
/// <param name="Behavior">Casting behaviors.</param>
/// <param name="Cooldown">Cooldown seconds; null when unavailable.</param>
/// <param name="ManaCost">Mana cost; null when unavailable.</param>
/// <param name="Values">Item attributes per level.</param>
/// <param name="HasCharges">Whether the item has charges.</param>
public sealed record ItemResponse(int Id, string Key, string Name, int Cost, IReadOnlyList<string> Components,
    bool IsConsumable, bool IsRecipe, IReadOnlyList<CatalogItemAbility> Abilities, IReadOnlyList<string> Behavior,
    double? Cooldown, double? ManaCost, IReadOnlyList<CatalogValue> Values, bool HasCharges);
