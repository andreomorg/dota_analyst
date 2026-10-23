using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One ward placed in the match, read from the replay.</summary>
/// <param name="Kind">observer or sentry.</param>
/// <param name="Team">The team that owns it.</param>
/// <param name="IsPlayersTeam">True when it belongs to the player's team; null when the player is not in the match.</param>
/// <param name="PlacedByHeroName">Hero that placed it, or "Unknown hero"; null when the replay does not say.</param>
/// <param name="X">Map coordinate, as in PlayerDeath.</param>
/// <param name="Y">Map coordinate, as in PlayerDeath.</param>
/// <param name="PlacedAt">Game clock when it was placed, in seconds.</param>
/// <param name="RemovedAt">Game clock when it was gone (expired or destroyed); null when it still existed when the match ended.</param>
/// <param name="Destroyed">Killed by a unit; false when it expired (or still existed at the end).</param>
/// <param name="DestroyedByHeroName">Hero that destroyed it; null when it expired or a non-hero unit killed it.</param>
/// <param name="InLaningPhase">Placed before 10:00 of the game clock.</param>
public record MapWard(
    WardKind Kind,
    Team Team,
    bool? IsPlayersTeam,
    string? PlacedByHeroName,
    int X,
    int Y,
    int PlacedAt,
    int? RemovedAt,
    bool Destroyed,
    string? DestroyedByHeroName,
    bool InLaningPhase);
