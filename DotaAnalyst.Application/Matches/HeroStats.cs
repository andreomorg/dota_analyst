using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The statistics of one hero.</summary>
/// <param name="HeroId">Hero, as OpenDota's hero ID.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Team">The hero's team.</param>
/// <param name="IsCurrentPlayer">True for the hero of the player whose point of view the analysis uses.</param>
/// <param name="Series">One list per statistic the hero has, with one value per point.</param>
public record HeroStats(int HeroId, string HeroName, Team Team, bool IsCurrentPlayer, IReadOnlyList<StatSeries> Series);
