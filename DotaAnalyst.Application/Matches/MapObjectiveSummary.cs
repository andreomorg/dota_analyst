using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>A map objective taken: a building that fell, or Roshan/Tormentor killed.</summary>
/// <param name="Kind">Tower, barracks, ancient, roshan or tormentor.</param>
/// <param name="Seconds">Game clock when it fell or died.</param>
/// <param name="TakenBy">The team that took it (destroyed the enemy's building, denied its own, or killed Roshan/Tormentor).</param>
/// <param name="IsPlayersTeam">Taken by the player's team; null when the player is not in the match.</param>
/// <param name="Owner">Buildings: the team that owned it; null otherwise.</param>
/// <param name="TowerTier">Towers: 1 (outer) to 4 (by the Ancient); null otherwise.</param>
/// <param name="Lane">Barracks and towers 1-3: top, middle or bottom; null otherwise.</param>
/// <param name="MeleeBarracks">Barracks: melee (true) or ranged (false); null otherwise.</param>
/// <param name="Denied">A building destroyed by its own team.</param>
/// <param name="KillerHeroName">Hero that dealt the last hit; null when it wasn't a hero (creeps).</param>
/// <param name="AegisHeroName">Roshan: the hero that picked up the Aegis; null otherwise or when nobody did.</param>
public record MapObjectiveSummary(
    ObjectiveKind Kind,
    int Seconds,
    Team TakenBy,
    bool? IsPlayersTeam,
    Team? Owner,
    int? TowerTier,
    Lane? Lane,
    bool? MeleeBarracks,
    bool Denied,
    string? KillerHeroName,
    string? AegisHeroName);
