using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// Creates replay analyses for tests, so each test only spells out what it cares about.
internal static class ReplayBuilder
{
    public static LaningStats Hero(int heroId, Team team, Lane lane, int netWorth = 2000) =>
        new(heroId, team, lane, lastHits: 20, denies: 3, netWorth, experience: 2500, deaths: 1);

    public static ReplayAnalysis Analysis(long matchId, params LaningStats[] players) =>
        new(MatchIdOf(matchId), ReplayAnalysis.LaningPhaseEndsAtSeconds, players);

    public static MatchId MatchIdOf(long value)
    {
        MatchId.TryCreate(value, out var matchId);
        return matchId;
    }
}
