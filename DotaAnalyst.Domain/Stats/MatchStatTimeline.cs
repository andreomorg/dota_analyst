using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Stats;

// Every statistic of the catalog for every hero, one value per point of the match timeline (point i at Seconds[i]:
// each minute, then the end). The saved series come from the timeline; the ones counted from events (wards) are
// counted here. Any moment or period is a question to this object: the page decides which moment it shows.
public sealed record MatchStatTimeline
{
    private MatchStatTimeline(IReadOnlyList<int> seconds, IReadOnlyList<HeroStatSeries> heroes)
    {
        Seconds = seconds;
        Heroes = heroes;
    }

    public IReadOnlyList<int> Seconds { get; }

    public IReadOnlyList<HeroStatSeries> Heroes { get; }

    // Null for analyses without a timeline.
    public static MatchStatTimeline? From(ReplayAnalysis replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.Timeline is not { } timeline)
        {
            return null;
        }

        var heroes = timeline.Heroes
            .Select(hero =>
            {
                var series = new Dictionary<StatKey, IReadOnlyList<int>>(hero.Series);
                if (replay.Wards is { } wards)
                {
                    series[StatKey.ObserversPlaced] = Count(timeline.Seconds, wards
                        .Where(ward => ward.PlacedByHeroId == hero.HeroId && ward.Kind == WardKind.Observer)
                        .Select(ward => ward.PlacedAt));
                    series[StatKey.SentriesPlaced] = Count(timeline.Seconds, wards
                        .Where(ward => ward.PlacedByHeroId == hero.HeroId && ward.Kind == WardKind.Sentry)
                        .Select(ward => ward.PlacedAt));
                    series[StatKey.EnemyWardsDestroyed] = Count(timeline.Seconds, wards
                        .Where(ward => ward.Destroyed && ward.DestroyedByHeroId == hero.HeroId && ward.Team != hero.Team)
                        .Select(ward => ward.RemovedAt ?? ward.PlacedAt));
                }

                return new HeroStatSeries(hero.HeroId, hero.Team, series);
            })
            .ToList();
        return new MatchStatTimeline(timeline.Seconds, heroes);
    }

    // How many events happened by each point; the last point (the end of the match) counts them all.
    private static List<int> Count(IReadOnlyList<int> seconds, IEnumerable<int> eventSeconds)
    {
        var times = eventSeconds.ToList();
        return seconds
            .Select((second, index) => index == seconds.Count - 1 ? times.Count : times.Count(time => time <= second))
            .ToList();
    }

    public HeroStatSeries? FindHero(int heroId) => Heroes.FirstOrDefault(hero => hero.HeroId == heroId);

    // The value at a moment of the game clock: the last point at or before it (no guessing between two minutes);
    // before 0:00, the first point; after the end, the end. Null when the hero doesn't have the statistic.
    public int? ValueAt(int heroId, StatKey key, int second) =>
        FindHero(heroId)?.Series.TryGetValue(key, out var values) == true ? values[PointAt(second)] : null;

    // What happened between two moments: the value at the end minus the value at the start. For a total (last hits)
    // it is the count in the period; for a snapshot (net worth) the gain.
    public int? Between(int heroId, StatKey key, int fromSecond, int toSecond)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromSecond, toSecond);
        return ValueAt(heroId, key, toSecond) - ValueAt(heroId, key, fromSecond);
    }

    // The game-clock second of the point a moment falls on (what ValueAt really read).
    public int SecondOfPointAt(int second) => Seconds[PointAt(second)];

    private int PointAt(int second)
    {
        var index = 0;
        for (var i = 0; i < Seconds.Count && Seconds[i] <= second; i++)
        {
            index = i;
        }

        return index;
    }
}

// One hero's statistics, one value per point of the MatchStatTimeline.
public sealed record HeroStatSeries(int HeroId, Team Team, IReadOnlyDictionary<StatKey, IReadOnlyList<int>> Series);
