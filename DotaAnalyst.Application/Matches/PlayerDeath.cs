using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One death of the player, read from the replay.</summary>
/// <param name="Seconds">Game clock of the death, in seconds (negative before the horn).</param>
/// <param name="X">Map coordinate: roughly −8200 (Radiant's side, left) to 8200.</param>
/// <param name="Y">Map coordinate: roughly −8200 (Radiant's side, bottom) to 8200.</param>
/// <param name="Killer">What dealt the killing blow: hero, tower, creeps, neutrals, roshan or other.</param>
/// <param name="KillerHeroId">The hero that got the kill; null unless killer is hero (and for hero kills the replay does not attribute).</param>
/// <param name="KillerHeroName">That hero's name, or "Unknown hero"; null when killerHeroId is null.</param>
/// <param name="Reincarnated">The hero came back (Aegis, Wraith King): not a death on the scoreboard.</param>
/// <param name="InLaningPhase">Died before 10:00 of the game clock.</param>
public record PlayerDeath(
    int Seconds,
    int X,
    int Y,
    KillerKind Killer,
    int? KillerHeroId,
    string? KillerHeroName,
    bool Reincarnated,
    bool InLaningPhase);
