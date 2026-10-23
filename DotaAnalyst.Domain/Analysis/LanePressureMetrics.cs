using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Where the creep waves met in the hero's lane during the laning phase (0:00 to 10:00), seen from the hero's side: the
// point halfway between its team's wave front and the enemy's (ReplayActivity.Waves, x + y along the lane; 0 = the river,
// positive = past the river, towards the enemy's base). PushedShare: the part of the samples with the meeting point past
// the river (the lane pushed); AverageMeetingPoint: its mean, in map units.
public sealed record LanePressureMetrics(int Samples, double PushedShare, double AverageMeetingPoint);

// Domain service: the LanePressureMetrics of one hero.
public static class LanePressureMetricsCalculator
{
    // Null for a hero without a physical lane (jungle, roaming), without waves in the document, or with no sample where
    // both waves were in its lane.
    public static LanePressureMetrics? Calculate(Team team, Lane lane, IReadOnlyList<WaveFront>? waves)
    {
        if (waves is null || lane is not (Lane.Top or Lane.Middle or Lane.Bottom))
        {
            return null;
        }

        var meetingPoints = waves
            .Where(wave => wave.Lane == lane && wave.Seconds is >= 0 and < ReplayAnalysis.LaningPhaseEndsAtSeconds
                && wave.Radiant is not null && wave.Dire is not null)
            // From the Radiant side x + y grows towards the enemy; from the Dire side it is the opposite.
            .Select(wave => (wave.Radiant!.Value + wave.Dire!.Value) / 2.0 * (team == Team.Radiant ? 1 : -1))
            .ToList();
        return meetingPoints.Count == 0
            ? null
            : new LanePressureMetrics(meetingPoints.Count, (double)meetingPoints.Count(point => point > 0) / meetingPoints.Count,
                Math.Round(meetingPoints.Average(), 1));
    }
}
