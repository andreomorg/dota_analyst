namespace DotaAnalyst.Application.Matches;

/// <summary>Where the creep waves met in the hero's lane during the laning phase (0:00-10:00), from the hero's side.</summary>
/// <param name="Samples">Samples (every 2 s) with both waves in the lane.</param>
/// <param name="PushedShare">Share (0..1) of those with the meeting point past the river (the lane pushed).</param>
/// <param name="AverageMeetingPoint">Average meeting point along the lane, in map units (0 = the river, positive = towards the enemy).</param>
public record LanePressureSummary(int Samples, double PushedShare, double AverageMeetingPoint);
