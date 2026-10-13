namespace DotaAnalyst.Domain.Replays;

// One position of a hero: game clock and map coordinates (like HeroDeath: about -8200..8200, Radiant bottom-left).
public readonly record struct PositionSample(int Seconds, int X, int Y);

// Where one hero was during the match, read from the replay: a sample every few seconds while it was alive (a dead
// hero has none). Used to compute the metrics in the parse and never saved: it is most of the replay's data.
public sealed class HeroPositions
{
    private readonly Dictionary<int, PositionSample> bySecond;

    public HeroPositions(int heroId, IReadOnlyList<PositionSample> samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(samples);

        HeroId = heroId;
        Samples = samples.OrderBy(sample => sample.Seconds).ToList();
        // The first sample of a second wins (a second never has two in practice).
        bySecond = Samples.DistinctBy(sample => sample.Seconds).ToDictionary(sample => sample.Seconds);
    }

    public int HeroId { get; }

    public IReadOnlyList<PositionSample> Samples { get; }

    // Where the hero was around this second (the closest sample at most `tolerance` seconds away); null when it was
    // dead (or not sampled) then.
    public PositionSample? At(int seconds, int tolerance)
    {
        for (var offset = 0; offset <= tolerance; offset++)
        {
            if (bySecond.TryGetValue(seconds - offset, out var before))
            {
                return before;
            }

            if (bySecond.TryGetValue(seconds + offset, out var after))
            {
                return after;
            }
        }

        return null;
    }
}
