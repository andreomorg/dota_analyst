namespace DotaAnalyst.Domain.Matches;

// Optional source aggregates. Null means missing; empty means present with no events.
// These totals must not be combined with replay events whose scope differs.
public sealed record SupplementalPlayerStats(
    IReadOnlyDictionary<string, int>? DamageReceived,
    IReadOnlyDictionary<string, int>? AbilityUses,
    IReadOnlyDictionary<string, int>? ItemUses,
    int? ActionsPerMinute = null,
    IReadOnlyDictionary<string, int>? Actions = null,
    IReadOnlyDictionary<string, int>? GoldSources = null,
    IReadOnlyDictionary<string, int>? ExperienceSources = null);
