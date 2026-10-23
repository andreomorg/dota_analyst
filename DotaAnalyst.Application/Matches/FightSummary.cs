using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One teamfight: hero deaths at most 15 s apart, at least 3 of them.</summary>
/// <param name="StartSeconds">Game clock of the first death.</param>
/// <param name="EndSeconds">Game clock of the last death.</param>
/// <param name="RadiantDeaths">Radiant heroes that died (Aegis deaths included).</param>
/// <param name="DireDeaths">Dire heroes that died.</param>
/// <param name="Winner">The team with fewer deaths; null on a tie.</param>
/// <param name="PlayersTeamWon">Whether the player's team won it; null on a tie or when the player is not in the match.</param>
/// <param name="Died">The player died in it.</param>
/// <param name="Kills">Killing blows of the player in it.</param>
/// <param name="Assists">Assists of the player in it (the combat log's: fewer than the scoreboard credits).</param>
/// <param name="X">Map coordinate of the middle of the fight (average of the deaths).</param>
/// <param name="Y">Map coordinate of the middle of the fight.</param>
public record FightSummary(
    int StartSeconds,
    int EndSeconds,
    int RadiantDeaths,
    int DireDeaths,
    Team? Winner,
    bool? PlayersTeamWon,
    bool Died,
    int Kills,
    int Assists,
    int X,
    int Y);
