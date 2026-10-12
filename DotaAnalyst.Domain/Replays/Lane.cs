namespace DotaAnalyst.Domain.Replays;

// Where a hero spent the laning phase, by their position on the map. Top, Middle and Bottom are physical lanes:
// Radiant's bottom (its safe lane) and Dire's bottom (its off lane) are the same lane, so heroes of opposite
// teams with the same lane faced each other.
public enum Lane
{
    Top,
    Middle,
    Bottom,
    Jungle,
    // Moving between lanes (usually a support), so not facing anyone in particular.
    Roaming,
}
