using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The source of the rules used for this analysis, separate from names used for display.</summary>
public sealed record CatalogProvenance(string Scope, string? BuildId, bool ItemRulesAvailable, bool HeroProfilesAvailable);
