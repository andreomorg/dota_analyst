namespace DotaAnalyst.Domain.Coaching;

// Availability at the earlier anchor and coverage of the later event are independent observations.
public sealed record CoachingSceneAvailability(
    string AnchorSupportDistance, string AnchorSampleSynchronization, string EventPositionCoverage);

public static class CoachingSceneAvailabilityBuilder
{
    public static CoachingSceneAvailability Build(CoachingDecisionScene scene)
    {
        var canonical = CoachingCanonicalSceneBuilder.Build(scene);
        var coverage = scene.RetrospectiveEpisode.PositionCoverage;
        if (coverage.ExpectedChecks <= 0 || coverage.ObservedChecks < 0 || coverage.ObservedChecks > coverage.ExpectedChecks)
            throw new ArgumentException("Invalid event position coverage.", nameof(scene));
        var viewer = scene.AtOrBeforeAnchor.Positions.SingleOrDefault(p => p.HeroId == scene.RetrospectiveEpisode.HeroId);
        var distance = canonical.NearestObservedAlly;
        return new(viewer is null ? "missing-viewer-sample" : distance is null ? "no-observed-ally" : "available",
            distance is null ? "not-comparable" : distance.ViewerSampleSeconds == distance.AllySampleSeconds
                ? "same-recorded-second" : "different-recorded-seconds",
            coverage.IsComplete ? "complete" : coverage.ObservedChecks == 0 ? "none" : "partial");
    }
}
