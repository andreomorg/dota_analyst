using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// Value object: one hero's numbers at the end of the laning phase, read from the match replay.
// Heroes are identified by hero ID, which is unique in a match and also works for anonymous profiles.
public record LaningStats
{
    public LaningStats(
        int heroId, Team team, Lane lane, int lastHits, int denies, int netWorth, int experience, int deaths)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentOutOfRangeException.ThrowIfNegative(lastHits);
        ArgumentOutOfRangeException.ThrowIfNegative(denies);
        ArgumentOutOfRangeException.ThrowIfNegative(netWorth);
        ArgumentOutOfRangeException.ThrowIfNegative(experience);
        ArgumentOutOfRangeException.ThrowIfNegative(deaths);

        HeroId = heroId;
        Team = team;
        Lane = lane;
        LastHits = lastHits;
        Denies = denies;
        NetWorth = netWorth;
        Experience = experience;
        Deaths = deaths;
    }

    public int HeroId { get; }

    public Team Team { get; }

    public Lane Lane { get; }

    public int LastHits { get; }

    public int Denies { get; }

    // Gold in hand plus the value of the items.
    public int NetWorth { get; }

    public int Experience { get; }

    public int Deaths { get; }
}
