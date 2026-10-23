using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One statistic of one hero over the match.</summary>
/// <param name="Stat">The statistic (a key of the definitions).</param>
/// <param name="Values">One value per point of seconds.</param>
public record StatSeries(StatKey Stat, IReadOnlyList<int> Values);
