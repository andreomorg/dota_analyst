using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The heatmap of one hero. Each phase is a flat list of [column, row, samples] triples, only for the cells the hero was in.</summary>
/// <param name="HeroId">Hero, as OpenDota's hero ID.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Team">The hero's team.</param>
/// <param name="IsCurrentPlayer">True for the hero of the player whose point of view the analysis uses.</param>
/// <param name="EarlyGame">0-10 minutes.</param>
/// <param name="MidGame">10-20 minutes.</param>
/// <param name="LateGame">From 20 minutes on.</param>
/// <param name="Indicators">Where and how the hero played (advance, coverage, allies close by…), per phase.</param>
public record HeroHeatmapView(
    int HeroId,
    string HeroName,
    Team Team,
    bool IsCurrentPlayer,
    IReadOnlyList<int> EarlyGame,
    IReadOnlyList<int> MidGame,
    IReadOnlyList<int> LateGame,
    PhaseIndicators? Indicators = null);
