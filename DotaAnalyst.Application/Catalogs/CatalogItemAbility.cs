using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>An item's active or passive effect.</summary>
/// <param name="Kind">Effect kind.</param>
/// <param name="Title">Effect title.</param>
/// <param name="Description">Game description.</param>
public sealed record CatalogItemAbility(ItemAbilityKind Kind, string? Title, string? Description);
