using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>How the player's lane went, at the end of the laning phase (10:00 of the game clock).</summary>
/// <param name="EndsAtSeconds">Game clock, in seconds, at which the numbers were taken: 600, or less when the match ended earlier.</param>
/// <param name="Lane">Where the player spent the laning phase: top, middle, bottom (physical lanes, the same for both teams), jungle or roaming.</param>
/// <param name="Outcome">won (lane net worth lead ≥ 1000), lost (≤ −1000) or even; null in the jungle, roaming, or when no enemy was in the lane.</param>
/// <param name="NetWorthDifference">Net worth of the player and allies minus the opponents'; null when outcome is null.</param>
/// <param name="Player">The player's numbers.</param>
/// <param name="Allies">Teammates in the same lane (without the player), richest first.</param>
/// <param name="Opponents">Enemies in the same lane, richest first.</param>
public record LaningPhase(
    int EndsAtSeconds,
    Lane Lane,
    LaneOutcome? Outcome,
    int? NetWorthDifference,
    LaningPlayer Player,
    IReadOnlyList<LaningPlayer> Allies,
    IReadOnlyList<LaningPlayer> Opponents);
