using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One neutral craft.</summary>
/// <param name="Seconds">Game-clock second of the craft.</param>
/// <param name="Tier">Tier, 1 to 5.</param>
/// <param name="ArtifactId">Item ID of the artifact (icon); null when the catalog does not know it.</param>
/// <param name="Artifact">Artifact display name, or its internal name when unknown.</param>
/// <param name="EnhancementId">Item ID of the enhancement; null when unknown or none.</param>
/// <param name="Enhancement">Enhancement display name; null when none.</param>
/// <param name="EnhancementLevel">Enhancement level; null when unknown.</param>
/// <param name="DelaySeconds">Seconds after the tier opened; null without the build's tiers.</param>
public sealed record NeutralCraftInfo(double Seconds, int Tier, int? ArtifactId, string Artifact, int? EnhancementId, string? Enhancement,
    int? EnhancementLevel, int? DelaySeconds);
