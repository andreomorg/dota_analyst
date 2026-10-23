using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Every statistic of every hero, one value per point of the match (each minute, then the end): the value at any moment is the last point at or before it, and what happened in a period is the difference between two points.</summary>
/// <param name="Definitions">What each statistic is (the same catalog for every match).</param>
/// <param name="Seconds">Game clock of each point, in seconds: 0, 60, 120, ... and the end of the match.</param>
/// <param name="Heroes">The ten heroes, Radiant first.</param>
public record MatchStats(IReadOnlyList<StatDefinitionView> Definitions, IReadOnlyList<int> Seconds, IReadOnlyList<HeroStats> Heroes);
