using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Maps;

public enum LandmarkKind
{
    Tower,
    Barracks,
    Ancient,
    // Other buildings (the effigies in each base).
    Building,
    NeutralCamp,
    Shop,
    Fountain,
    Outpost,
    TwinGate,
    LotusPool,
    WisdomShrine,
    BountyRune,
    PowerRune,
    NeutralStash,
    HeroSpawn,
    // The Watchers (lanterns).
    Lantern,
}

// Something with a fixed place on the map. Team is null for what belongs to no team (camps, runes, outposts at the
// start); Name is the map's own (e.g. dota_goodguys_tower1_top), empty when the map gives none.
public sealed record MapLandmark(LandmarkKind Kind, string Name, Team? Team, int X, int Y);
