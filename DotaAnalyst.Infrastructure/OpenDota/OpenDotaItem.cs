namespace DotaAnalyst.Infrastructure.OpenDota;

// The fields we use from each entry of GET /constants/items: the display name, and for the item build the cost,
// the components (item keys, null when bought whole) and the quality ("consumable", "consumable;laning"...).
public record OpenDotaItem(
    string? Dname, int? Cost = null, IReadOnlyList<string?>? Components = null, string? Qual = null);
