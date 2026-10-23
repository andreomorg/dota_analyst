using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>An amount for one hero.</summary>
/// <param name="HeroId">Hero.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Amount">The amount.</param>
public record HeroAmount(int HeroId, string HeroName, int Amount);
