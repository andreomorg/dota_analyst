using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

// Creates replay analyses for tests, so each test only spells out what it cares about.
internal static class ReplayBuilder
{
    public static LaningStats Hero(int heroId, Team team, Lane lane, int netWorth = 2000) =>
        new(heroId, team, lane, lastHits: 20, denies: 3, netWorth, experience: 2500, deaths: 1);

    // With a timeline (what every analysis has now): each hero's net worth and experience at 0:00, 10:00 and 20:00
    // are 0, then the laning numbers, then twice them (the other statistics per minute too, from the laning numbers;
    // zero when the laning phase doesn't have them).
    public static ReplayAnalysis Analysis(long matchId, params LaningStats[] players) =>
        new(MatchIdOf(matchId), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, Timeline(players), deaths: [], wards: [], damage: [], purchases: [], objectives: [], metrics: [], heatmaps: [], abilityUpgrades: []);

    // With these deaths (and the timeline).
    public static ReplayAnalysis AnalysisWithDeaths(long matchId, LaningStats[] players, params HeroDeath[] deaths) =>
        new(MatchIdOf(matchId), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, Timeline(players), deaths, wards: [], damage: [], purchases: [], objectives: [], metrics: [], heatmaps: [], abilityUpgrades: []);

    // An analysis saved before the timeline existed (laning phase only).
    public static ReplayAnalysis AnalysisWithoutTimeline(long matchId, params LaningStats[] players) =>
        new(MatchIdOf(matchId), ReplayAnalysis.LaningPhaseEndsAtSeconds, players);

    public static MatchTimeline Timeline(params LaningStats[] players) => new(
        [0, 600, 1200],
        players.Select(player => new HeroTimeline(
                player.HeroId,
                player.Team,
                [0, player.NetWorth, 2 * player.NetWorth],
                [0, player.Experience, 2 * player.Experience],
                StatCatalog.SavedPerMinute
                    .Where(key => key is not (StatKey.NetWorth or StatKey.Experience))
                    .ToDictionary(key => key, key => Rising(key switch
                    {
                        StatKey.LastHits => player.LastHits,
                        StatKey.Denies => player.Denies,
                        StatKey.Deaths => player.Deaths,
                        _ => 0,
                    }))))
            .ToList());

    private static IReadOnlyList<int> Rising(int atTen) => [0, atTen, 2 * atTen];

    public static MatchId MatchIdOf(long value)
    {
        MatchId.TryCreate(value, out var matchId);
        return matchId;
    }
}
