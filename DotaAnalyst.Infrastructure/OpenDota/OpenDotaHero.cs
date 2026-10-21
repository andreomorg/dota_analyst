namespace DotaAnalyst.Infrastructure.OpenDota;

// Only the fields we use from GET /heroes.
public record OpenDotaHero(int Id, string? LocalizedName,
    string? PrimaryAttr = null, string? AttackType = null, IReadOnlyList<string>? Roles = null, string? Name = null);
