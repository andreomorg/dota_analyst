using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The ability level-ups of the player whose point of view the analysis uses.</summary>
/// <param name="Upgrades">Ability names and levels in the order recorded by the replay.</param>
public record PlayerAbilityBuild(IReadOnlyList<AbilityBuildUpgrade> Upgrades);
