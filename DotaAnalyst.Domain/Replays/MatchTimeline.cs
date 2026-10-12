using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// Value object: how the heroes' net worth and experience changed during the match, read from the replay.
// Point i of every hero is taken at Seconds[i] of the game clock: one point per minute (0:00, 1:00, ...) and a
// last one at the end of the match.
public record MatchTimeline
{
    public MatchTimeline(IReadOnlyList<int> seconds, IReadOnlyList<HeroTimeline> heroes)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        ArgumentNullException.ThrowIfNull(heroes);
        if (seconds.Count == 0 || seconds[0] < 0 || seconds.Zip(seconds.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            throw new ArgumentException("The points must start at 0:00 or later and go forward in time.", nameof(seconds));
        }

        if (heroes.Any(hero => hero is null || hero.NetWorth.Count != seconds.Count))
        {
            throw new ArgumentException("Every hero needs one value per point.", nameof(heroes));
        }

        // Heroes are matched by hero ID, as in ReplayAnalysis.
        if (heroes.DistinctBy(hero => hero.HeroId).Count() != heroes.Count)
        {
            throw new ArgumentException("Each hero can appear only once in a match.", nameof(heroes));
        }

        Seconds = seconds.ToList();
        Heroes = heroes.ToList();
    }

    public IReadOnlyList<int> Seconds { get; }

    public IReadOnlyList<HeroTimeline> Heroes { get; }

    public HeroTimeline? FindHero(int heroId) => Heroes.FirstOrDefault(hero => hero.HeroId == heroId);
}

// One hero's values at each point of the MatchTimeline.
public record HeroTimeline
{
    public HeroTimeline(int heroId, Team team, IReadOnlyList<int> netWorth, IReadOnlyList<int> experience)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(netWorth);
        ArgumentNullException.ThrowIfNull(experience);
        if (netWorth.Count != experience.Count)
        {
            throw new ArgumentException("Net worth and experience need the same points.", nameof(experience));
        }

        if (netWorth.Any(value => value < 0) || experience.Any(value => value < 0))
        {
            throw new ArgumentException("Net worth and experience can't be negative.", nameof(netWorth));
        }

        HeroId = heroId;
        Team = team;
        NetWorth = netWorth.ToList();
        Experience = experience.ToList();
    }

    public int HeroId { get; }

    public Team Team { get; }

    public IReadOnlyList<int> NetWorth { get; }

    public IReadOnlyList<int> Experience { get; }
}
