namespace DotaAnalyst.Infrastructure.OpenDota;

// Only the field we use from each entry of GET /constants/items (the display name).
public record OpenDotaItem(string? Dname);
