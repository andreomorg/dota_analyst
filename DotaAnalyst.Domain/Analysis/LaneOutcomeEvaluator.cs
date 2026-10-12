using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// How a hero's lane went: who was there and, when there was a fight for it, who won.
public record LaneEvaluation(
    LaningStats Player,
    // Teammates in the same lane (without the player), richest first.
    IReadOnlyList<LaningStats> Allies,
    // Enemies in the same physical lane, richest first.
    IReadOnlyList<LaningStats> Opponents,
    // Null when there was no fight for the lane: jungle, roaming, or no enemy in it.
    LaneOutcome? Outcome,
    // Net worth of the player and allies minus the opponents'; null when Outcome is null.
    int? NetWorthDifference);

// Domain service: decides whether a hero won, tied or lost their lane, by comparing the net worth of the heroes
// of each side of that lane at the end of the laning phase.
public static class LaneOutcomeEvaluator
{
    // Net worth lead (sum of each side of the lane) from which the lane counts as won; the same deficit counts as lost.
    public const int NetWorthLeadToWin = 1000;

    /// <returns>The evaluation, or null when the hero is not in the replay.</returns>
    public static LaneEvaluation? Evaluate(ReplayAnalysis replay, int heroId)
    {
        if (replay.FindHero(heroId) is not { } player)
        {
            return null;
        }

        // Jungle and roaming heroes didn't face anyone in particular.
        if (player.Lane is not (Lane.Top or Lane.Middle or Lane.Bottom))
        {
            return new LaneEvaluation(player, [], [], Outcome: null, NetWorthDifference: null);
        }

        var allies = InLane(replay, player, sameTeam: true).Where(ally => ally.HeroId != player.HeroId).ToList();
        var opponents = InLane(replay, player, sameTeam: false).ToList();

        // A free lane (no enemy came) is not a win over anyone: there is nothing to compare.
        if (opponents.Count == 0)
        {
            return new LaneEvaluation(player, allies, [], Outcome: null, NetWorthDifference: null);
        }

        var difference = player.NetWorth + allies.Sum(ally => ally.NetWorth) - opponents.Sum(opponent => opponent.NetWorth);
        return new LaneEvaluation(player, allies, opponents, Rate(difference), difference);
    }

    public static LaneOutcome Rate(int netWorthDifference) => netWorthDifference switch
    {
        >= NetWorthLeadToWin => LaneOutcome.Won,
        <= -NetWorthLeadToWin => LaneOutcome.Lost,
        _ => LaneOutcome.Even,
    };

    // Lanes are physical (see Lane), so the same value means the same place on the map for both teams.
    private static IEnumerable<LaningStats> InLane(ReplayAnalysis replay, LaningStats player, bool sameTeam) =>
        replay.Players
            .Where(other => other.Lane == player.Lane && (other.Team == player.Team) == sameTeam)
            .OrderByDescending(other => other.NetWorth);
}
