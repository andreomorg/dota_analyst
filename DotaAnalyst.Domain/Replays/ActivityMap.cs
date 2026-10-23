using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// One courier of a team over the match (sparse readings: alive, position).
public sealed record CourierTrack(string Id, Team Team, IReadOnlyList<(int Seconds, bool Alive, int X, int Y)> Samples)
{
    public IReadOnlyList<CourierFlight>? Flight { get; init; }
    // Alive at the last reading up to `second`; null before the first.
    public bool? AliveAt(int second)
    {
        bool? alive = null;
        foreach (var sample in Samples)
        {
            if (sample.Seconds > second)
            {
                break;
            }

            alive = sample.Alive;
        }

        return alive;
    }
}

public sealed record CourierFlight(int Seconds, int? Raw)
{
    public bool? Flying => Raw switch { 0 => false, 1 => true, _ => null };
}

// A rune that appeared (Present) or vanished (!Present) on the map, read every 2 s (RunePickup.IsPower's types).
public sealed record RuneOnMap(int Seconds, int RuneType, int X, int Y, bool Present);

// A neutral camp (its spawner's position and type) and its living wild neutrals over the match (sparse readings).
public sealed record NeutralCamp(string Id, int X, int Y, string? Type, IReadOnlyList<(int Seconds, int Alive)> Alive)
{
    public int? SpawnerType { get; init; }
    // The last reading up to `second` (sorted by time); null before the first.
    public int? AliveAt(int second)
    {
        int? alive = null;
        foreach (var reading in Alive)
        {
            if (reading.Seconds > second)
            {
                break;
            }

            alive = reading.Alive;
        }

        return alive;
    }

    // A stacked camp: at least 1.5 × its most common count so far (neutrals spawn as one group; a stack adds another).
    public bool StackedAt(int second)
    {
        var seen = Alive.Where(reading => reading.Seconds <= second && reading.Alive > 0).ToList();
        if (seen.Count == 0 || AliveAt(second) is not { } now)
        {
            return false;
        }

        var usual = seen.GroupBy(reading => reading.Alive).MaxBy(group => (group.Count(), -group.Key))!.Key;
        return now >= 1.5 * usual;
    }
}

// From Seconds on, it is day (IsDay) or night (a temporary night counts as night).
public sealed record DayNightChange(int Seconds, bool IsDay);

public enum BuildingKind
{
    Tower,
    Barracks,
    Ancient,
}

// One reading of a building's health on the game clock (the parser writes one only when it changes).
public sealed record BuildingHealthReading(int Seconds, int Health, int MaxHealth);

// A tower, barracks or Ancient where it stands, its owner and its health over the match (sorted, ending at 0 when it
// fell). Lane is null for the Ancient and tier-4 towers; Tier is null outside towers.
public sealed record BuildingHealth(
    string Building, Team Team, BuildingKind Kind, Lane? Lane, int? Tier, int X, int Y,
    IReadOnlyList<BuildingHealthReading> Readings)
{
    // The last reading up to `second` (never a later one); null before the first.
    public BuildingHealthReading? At(int second) => Readings.LastOrDefault(reading => reading.Seconds <= second);

    public bool StandingAt(int second) => At(second) is { Health: > 0 };
}
