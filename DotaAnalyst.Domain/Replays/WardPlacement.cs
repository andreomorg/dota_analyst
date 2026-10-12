using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

public enum WardKind
{
    Observer,
    Sentry,
}

// Value object: one ward read from the replay — where (map coordinates, like HeroDeath), when it was placed and
// gone (game clock), and who placed and destroyed it.
public record WardPlacement
{
    public WardPlacement(
        WardKind kind, Team team, int? placedByHeroId, int x, int y, int placedAt, int? removedAt, bool destroyed,
        int? destroyedByHeroId)
    {
        if (placedByHeroId is <= 0 || destroyedByHeroId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placedByHeroId), "Hero IDs are positive.");
        }

        if (removedAt < placedAt)
        {
            throw new ArgumentException("A ward can't be gone before it was placed.", nameof(removedAt));
        }

        if (destroyedByHeroId is not null && !destroyed)
        {
            throw new ArgumentException("Only a destroyed ward has a destroyer.", nameof(destroyedByHeroId));
        }

        Kind = kind;
        Team = team;
        PlacedByHeroId = placedByHeroId;
        X = x;
        Y = y;
        PlacedAt = placedAt;
        RemovedAt = removedAt;
        Destroyed = destroyed;
        DestroyedByHeroId = destroyedByHeroId;
    }

    public WardKind Kind { get; }

    // The team that owns it.
    public Team Team { get; }

    // Null when the replay does not say who placed it.
    public int? PlacedByHeroId { get; }

    public int X { get; }

    public int Y { get; }

    public int PlacedAt { get; }

    // Null when it still existed when the match ended.
    public int? RemovedAt { get; }

    // Killed by a unit (false: it expired, or still existed at the end).
    public bool Destroyed { get; }

    // The hero that destroyed it; null when it expired or a non-hero unit killed it.
    public int? DestroyedByHeroId { get; }

    // Placed before the end of the laning phase.
    public bool InLaningPhase => PlacedAt < ReplayAnalysis.LaningPhaseEndsAtSeconds;
}
