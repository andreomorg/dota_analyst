using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Domain service: each hero's role (1 carry .. 5 hard support) for a match without role queue (pro matches: Captain's
// Mode), from where the heroes laned and their net worth at the end of the laning phase:
// - the middle lane's richest hero is the 2;
// - in the safe lane (Radiant's bottom, Dire's top) the richest is the 1 and the next the 5;
// - in the off lane the richest is the 3 and the next the 4;
// - heroes left (jungle, roaming, a third hero in a lane) take the roles left, the richest the lowest role.
public static class RoleInference
{
    public static IReadOnlyDictionary<int, int> Infer(IReadOnlyList<LaningStats> players)
    {
        var roles = new Dictionary<int, int>();
        foreach (var team in players.GroupBy(player => player.Team))
        {
            var safeLane = team.Key == Team.Radiant ? Lane.Bottom : Lane.Top;
            var offLane = team.Key == Team.Radiant ? Lane.Top : Lane.Bottom;
            var free = new SortedSet<int> { 1, 2, 3, 4, 5 };
            var left = team.ToList();

            void Assign(Lane lane, params int[] laneRoles)
            {
                var inLane = left.Where(player => player.Lane == lane).OrderByDescending(player => player.NetWorth).ToList();
                foreach (var (player, role) in inLane.Zip(laneRoles))
                {
                    roles[player.HeroId] = role;
                    free.Remove(role);
                    left.Remove(player);
                }
            }

            Assign(Lane.Middle, 2);
            Assign(safeLane, 1, 5);
            Assign(offLane, 3, 4);

            foreach (var (player, role) in left.OrderByDescending(player => player.NetWorth).Zip(free.ToList()))
            {
                roles[player.HeroId] = role;
            }
        }

        return roles;
    }
}
