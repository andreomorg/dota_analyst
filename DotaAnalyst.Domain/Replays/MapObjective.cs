using DotaAnalyst.Domain.Matches;
// Inside MapObjective, "Lane" is the property; the enum needs another name there.
using LaneOf = DotaAnalyst.Domain.Replays.Lane;

namespace DotaAnalyst.Domain.Replays;

public enum ObjectiveKind
{
    Tower,
    Barracks,
    Ancient,
    Roshan,
    Tormentor,
}

// Value object: a map objective taken, read from the replay — a building that fell (tower, barracks, Ancient) or a
// Roshan/Tormentor kill.
public record MapObjective
{
    public MapObjective(
        ObjectiveKind kind,
        int seconds,
        Team takenBy,
        int? killerHeroId,
        Team? owner = null,
        int? towerTier = null,
        Lane? lane = null,
        bool? meleeBarracks = null,
        bool denied = false,
        int? aegisHeroId = null)
    {
        if (killerHeroId is <= 0 || aegisHeroId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(killerHeroId), "Hero IDs are positive.");
        }

        var isBuilding = kind is ObjectiveKind.Tower or ObjectiveKind.Barracks or ObjectiveKind.Ancient;
        if (isBuilding != owner.HasValue)
        {
            throw new ArgumentException("Buildings, and only buildings, have an owner.", nameof(owner));
        }

        if ((kind == ObjectiveKind.Tower) != towerTier.HasValue || towerTier is < 1 or > 4)
        {
            throw new ArgumentException("Towers, and only towers, have a tier from 1 to 4.", nameof(towerTier));
        }

        // Tier 4 towers stand in the base, by the Ancient: no lane.
        var hasLane = kind == ObjectiveKind.Barracks || (kind == ObjectiveKind.Tower && towerTier < 4);
        if (hasLane != lane.HasValue || lane is not (null or LaneOf.Top or LaneOf.Middle or LaneOf.Bottom))
        {
            throw new ArgumentException("Barracks and towers 1-3, and only them, stand in a lane.", nameof(lane));
        }

        if ((kind == ObjectiveKind.Barracks) != meleeBarracks.HasValue)
        {
            throw new ArgumentException("Barracks, and only barracks, are melee or ranged.", nameof(meleeBarracks));
        }

        // A denied building is taken by its owner; any other by the enemy.
        if (isBuilding && (takenBy == owner) != denied)
        {
            throw new ArgumentException("A building is taken by the enemy, or by its owner when denied.", nameof(takenBy));
        }

        if (aegisHeroId is not null && kind != ObjectiveKind.Roshan)
        {
            throw new ArgumentException("Only Roshan drops the Aegis.", nameof(aegisHeroId));
        }

        Kind = kind;
        Seconds = seconds;
        TakenBy = takenBy;
        KillerHeroId = killerHeroId;
        Owner = owner;
        TowerTier = towerTier;
        Lane = lane;
        MeleeBarracks = meleeBarracks;
        Denied = denied;
        AegisHeroId = aegisHeroId;
    }

    public ObjectiveKind Kind { get; }

    // Game clock when it fell or died.
    public int Seconds { get; }

    // The team that took it (killed Roshan, destroyed the enemy's building, or denied its own).
    public Team TakenBy { get; }

    // The hero that dealt the last hit; null when it wasn't a hero (creeps).
    public int? KillerHeroId { get; }

    // Buildings: the team that owned it; null otherwise.
    public Team? Owner { get; }

    // Towers: 1 (outer) to 4 (by the Ancient).
    public int? TowerTier { get; }

    // Barracks and towers 1-3: Top, Middle or Bottom.
    public Lane? Lane { get; }

    // Barracks: melee (true) or ranged (false).
    public bool? MeleeBarracks { get; }

    // A building destroyed by its own team.
    public bool Denied { get; }

    // Roshan: the hero that picked up the Aegis; null when nobody did (or the replay does not say).
    public int? AegisHeroId { get; }
}
