namespace DotaAnalyst.Domain.Items;

public record Item(int Id, string Name)
{
    // What the item is made of, for the item build; null when the catalog came without it (an old saved copy).
    public ItemDetails? Details { get; init; }
}
