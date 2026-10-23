using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One ability level-up in the replay.</summary>
/// <param name="Seconds">Game-clock time in seconds.</param>
/// <param name="AbilityName">The parser's internal ability name.</param>
/// <param name="Level">The resulting level of this ability.</param>
public record AbilityBuildUpgrade(int Seconds, string AbilityName, int Level);
