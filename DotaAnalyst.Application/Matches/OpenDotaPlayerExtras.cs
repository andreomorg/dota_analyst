namespace DotaAnalyst.Application.Matches;

/// <summary>Optional OpenDota aggregates; their scope may differ from our own replay. No inferred temporal series.</summary>
public sealed record OpenDotaPlayerExtras(string Source,
    IReadOnlyList<ExternalStatTotal>? DamageReceived,
    IReadOnlyList<ExternalStatTotal>? AbilityUses,
    IReadOnlyList<ExternalStatTotal>? ItemUses,
    int? ActionsPerMinute = null,
    IReadOnlyList<ExternalStatTotal>? Actions = null,
    IReadOnlyList<ExternalStatTotal>? GoldSources = null,
    IReadOnlyList<ExternalStatTotal>? ExperienceSources = null);

public sealed record ExternalStatTotal(string Key, string Name, int Amount, int? ItemId = null);
