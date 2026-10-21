using DotaAnalyst.Application.Abstractions;

namespace DotaAnalyst.Application.Catalogs;

public sealed class GetItemHandler(IItemCatalog items)
{
    public async Task<ItemResponse?> HandleAsync(string itemKey, CancellationToken cancellationToken)
    {
        var item = (await items.GetItemsAsync(cancellationToken)).Values.FirstOrDefault(i => i.Details?.Key == itemKey);
        if (item?.Details is not { } d) return null;
        return new(item.Id, d.Key, item.Name, d.Cost, d.Components, d.IsConsumable, d.IsRecipe,
            d.Abilities.Select(a => new CatalogItemAbility(a.Kind, a.Title, a.Description)).ToArray(),
            d.Behavior, d.Cooldown, d.ManaCost, d.Values.Select(CatalogValue.From).ToArray(), d.HasCharges);
    }
}
