using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// What one hero showed up to a second t, for PositionEstimator: where it spent its time since 0:00 (shares of its
// position samples in the safe lane, the middle and the off lane; the rest is jungle/river/base), the items bought before
// the horn, its lane creep last hits (null when the document has none), its net worth, the wards it placed and its
// catalog profile. Nothing after t.
public sealed record PositionEvidence(
    int HeroId, double SafeLane, double MiddleLane, double OffLane, IReadOnlyCollection<string> StartingItems,
    int? LaneLastHits, int NetWorth, int WardsPlaced, HeroDetails? Profile);

// A hero's estimated position (1 carry .. 5 hard support) and how sure: the total score of the best assignment minus
// the best one that gives this hero another position (0 = a tie).
public sealed record PositionEstimate(int Position, double Confidence);

// Domain service: the positions of one team at a second, from evidence up to that second only (unlike RoleInference,
// which reads the net worth at 10:00: the future before then, and volatile — one death swaps a carry and a support).
//
// Each hero gets a "core" score from independent clues: support items bought before the horn (the strongest early clue:
// it is known at 0:00), its share of the team's lane last hits (they are never lost on death, unlike gold; weighted by
// how many there are so far), the wards it placed and, weakly, the catalog roles. Then every way of giving the team's
// heroes distinct positions is scored by lane fit × core/support and the best one wins.
//
// The weights are a hand-written starting point, not fitted: work/agents/plan.md R3 measures them against role-queue
// matches (whose real positions are known) before anything relies on them.
public static class PositionEstimator
{
    private static readonly HashSet<string> SupportStartingItems = ["ward_observer", "ward_sentry", "ward_dispenser", "smoke_of_deceit", "blood_grenade", "dust"];
    private static readonly HashSet<string> CoreStartingItems = ["quelling_blade"];

    private const double SupportItemsWeight = -1.5;
    private const double CoreItemsWeight = 0.7;
    private const double FarmWeight = 2.0;
    // Lane last hits of the whole team at which the farm share counts fully (a few early last hits say little).
    private const int FarmFullAtLastHits = 20;
    private const double WardWeight = -0.5;
    private const double WardWeightFloor = -1.5;
    private const double CatalogWeight = 0.3;
    // A small pull of cores to 1–3 and supports to 4–5 when the lane says nothing yet (before the creeps meet, roamers).
    private const double Prior = 0.05;

    // Shares of the samples (game clock ≥ 0, up to `until`) in the team's safe lane, the middle and the off lane.
    public static (double Safe, double Middle, double Off) LaneShares(Team team, IEnumerable<PositionSample> samples, int until)
    {
        var regions = samples.Where(sample => sample.Seconds >= 0 && sample.Seconds <= until)
            .Select(sample => MapRegions.RegionOf(team, sample.X, sample.Y)).ToList();
        if (regions.Count == 0)
        {
            return (0, 0, 0);
        }

        var (safe, off) = team == Team.Radiant ? (MapRegion.BottomLane, MapRegion.TopLane) : (MapRegion.TopLane, MapRegion.BottomLane);
        double Share(MapRegion region) => (double)regions.Count(found => found == region) / regions.Count;
        return (Share(safe), Share(MapRegion.MiddleLane), Share(off));
    }

    public static IReadOnlyDictionary<int, PositionEstimate> Estimate(IReadOnlyList<PositionEvidence> team)
    {
        ArgumentNullException.ThrowIfNull(team);
        if (team.Count is 0 or > 5)
        {
            return new Dictionary<int, PositionEstimate>();
        }

        var core = CoreScores(team);
        double Fit(int hero, int position)
        {
            var (evidence, c) = (team[hero], core[hero]);
            return position switch
            {
                1 => evidence.SafeLane * c + Prior * c,
                2 => evidence.MiddleLane * c + Prior * c,
                3 => evidence.OffLane * c + Prior * c,
                4 => evidence.OffLane * (1 - c) + Prior * (1 - c),
                _ => evidence.SafeLane * (1 - c) + Prior * (1 - c),
            };
        }

        // Every assignment of distinct positions (at most 5! = 120), with its total.
        var assignments = new List<(int[] Positions, double Total)>();
        void Assign(int hero, int[] positions, bool[] used, double total)
        {
            if (hero == team.Count)
            {
                assignments.Add(((int[])positions.Clone(), total));
                return;
            }

            for (var position = 1; position <= 5; position++)
            {
                if (used[position])
                {
                    continue;
                }

                used[position] = true;
                positions[hero] = position;
                Assign(hero + 1, positions, used, total + Fit(hero, position));
                used[position] = false;
            }
        }

        Assign(0, new int[team.Count], new bool[6], 0);
        var best = assignments.MaxBy(assignment => assignment.Total);
        return team.Select((evidence, hero) => (evidence.HeroId, Estimate: new PositionEstimate(best.Positions[hero],
                Math.Round(best.Total - assignments.Where(other => other.Positions[hero] != best.Positions[hero]).Max(other => other.Total), 3))))
            .ToDictionary(pair => pair.HeroId, pair => pair.Estimate);
    }

    // 0..1: how much each hero looks like a core (farm priority) rather than a support.
    private static double[] CoreScores(IReadOnlyList<PositionEvidence> team)
    {
        // Farm: lane last hits when the document has them, else the net worth gained since the start (600 each).
        var useLastHits = team.All(hero => hero.LaneLastHits is not null);
        var farm = team.Select(hero => useLastHits ? hero.LaneLastHits!.Value : Math.Max(0, hero.NetWorth - 600)).ToList();
        var total = farm.Sum();
        var confidence = useLastHits ? Math.Min(1, (double)total / FarmFullAtLastHits) : Math.Min(1, total / 3000.0);

        return team.Select((hero, index) =>
        {
            // An equal share (1 / team size) is neutral; twice that is clearly the farmer.
            var share = total == 0 ? 0 : (double)farm[index] / total;
            var farmClue = Math.Clamp(share * team.Count - 1, -1, 1) * FarmWeight * confidence;
            var itemClue = (hero.StartingItems.Any(SupportStartingItems.Contains) ? SupportItemsWeight : 0)
                + (hero.StartingItems.Any(CoreStartingItems.Contains) ? CoreItemsWeight : 0);
            var wardClue = Math.Max(WardWeightFloor, hero.WardsPlaced * WardWeight);
            var roles = hero.Profile?.Roles;
            var catalogClue = (roles?.Contains(HeroRole.Carry) == true ? CatalogWeight : 0)
                - (roles?.Contains(HeroRole.Support) == true ? CatalogWeight : 0);
            return 1 / (1 + Math.Exp(-(farmClue + itemClue + wardClue + catalogClue)));
        }).ToArray();
    }
}
