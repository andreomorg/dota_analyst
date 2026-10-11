using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Abstractions;

public interface IItemCatalog
{
    // Items by ID. Returns an empty dictionary (never throws) when the list is unavailable,
    // so missing item names don't hide the analysis.
    Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken);
}
