using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Situations;

// Each hero's position at a second: the role queue's, or PositionEstimator's from the evidence up to that second.
public sealed partial class HeroSituationBuilder
{
    // Lanes say who is where only in the laning phase: later everyone moves around the map.
    public const int LaningPhaseSeconds = 600;

    // One estimate per team and second (every hero of the team asks for the same one).
    private readonly Dictionary<(Team Team, int Seconds), IReadOnlyDictionary<int, PositionEstimate>> estimatesAt = [];

    private (int? Role, bool Estimated, double? Confidence) RoleOf(int heroId, int seconds)
    {
        // A role queue's position is the game's own; RoleInferred metrics (the 10:00 rule) are not used here.
        if (metricsOf.GetValueOrDefault(heroId) is { Role: { } role, RoleInferred: false })
        {
            return (role, false, null);
        }

        return teamOf.TryGetValue(heroId, out var team) && EstimatesAt(team, seconds).TryGetValue(heroId, out var estimate)
            ? (estimate.Position, true, estimate.Confidence)
            : (null, false, null);
    }

    // The estimator's answer for a team at a second even when the match has a role queue: to measure it against the
    // real positions (work/agents/plan.md R3).
    public IReadOnlyDictionary<int, PositionEstimate> EstimatedPositions(Team team, int seconds) => EstimatesAt(team, seconds);

    private IReadOnlyDictionary<int, PositionEstimate> EstimatesAt(Team team, int seconds)
    {
        if (estimatesAt.TryGetValue((team, seconds), out var cached))
        {
            return cached;
        }

        var laningUntil = Math.Min(seconds, LaningPhaseSeconds);
        var evidence = teamOf.Where(hero => hero.Value == team).Select(hero => hero.Key).Order().Select(hero =>
        {
            var (safe, middle, off) = positionsOf.GetValueOrDefault(hero) is { } track
                ? PositionEstimator.LaneShares(team, track.Samples, laningUntil)
                : (0, 0, 0);
            return new PositionEvidence(
                hero, safe, middle, off,
                replay.Purchases?.Where(purchase => purchase.HeroId == hero && purchase.Seconds < 0)
                    .Select(purchase => purchase.ItemKey).ToList() ?? [],
                activity?.CreepKills?.Count(kill => kill.HeroId == hero && kill.Kind == CreepKillKind.Lane && kill.Seconds <= seconds),
                Value(hero, StatKey.NetWorth, seconds),
                replay.Wards!.Count(ward => ward.PlacedByHeroId == hero && ward.PlacedAt <= seconds),
                heroDetails?.GetValueOrDefault(hero));
        }).ToList();

        var estimates = PositionEstimator.Estimate(evidence);
        estimatesAt[(team, seconds)] = estimates;
        return estimates;
    }
}
