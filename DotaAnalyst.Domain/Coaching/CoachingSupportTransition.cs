using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Coaching;

public enum CoachingSupportChange { Unavailable, Increased, Decreased, Unchanged }

// Distances between independently recorded samples, not path length or player-visible information.
public sealed record CoachingSupportDistanceSample(
    int TargetSeconds, int ViewerSampleSeconds, int AllySampleSeconds, double Distance);

public sealed record CoachingSupportTransition(
    string EpisodeId, int HeroId, int? AllyHeroId, int AnchorSeconds, int? EarlierTargetSeconds,
    CoachingSupportDistanceSample? Earlier, CoachingSupportDistanceSample? AtAnchor,
    CoachingSupportChange Change, string? UnavailableReason,
    int ExpectedPairedSeconds, int ObservedPairedSeconds);

public static class CoachingSupportTransitionBuilder
{
    public const int InspectionSeconds = 10;
    private const int MaxSampleAgeSeconds = 2;

    public static CoachingSupportTransition Build(CoachingDecisionScene scene, IReadOnlyList<HeroPositions>? positions)
    {
        var canonical = CoachingCanonicalSceneBuilder.Build(scene);
        var anchor = scene.AtOrBeforeAnchor.AnchorSeconds;
        var viewerId = scene.RetrospectiveEpisode.HeroId;
        var ally = canonical.NearestObservedAlly;
        var atAnchor = ally is null ? null : new CoachingSupportDistanceSample(anchor,
            ally.ViewerSampleSeconds, ally.AllySampleSeconds, ally.Distance);
        int? earlierTarget = anchor >= InspectionSeconds ? anchor - InspectionSeconds : null;
        var initial = new CoachingSupportTransition(scene.RetrospectiveEpisode.Id, viewerId, ally?.AllyHeroId,
            anchor, earlierTarget, null, atAnchor, CoachingSupportChange.Unavailable, null,
            earlierTarget is null ? 0 : InspectionSeconds + 1, 0);
        if (earlierTarget is null) return initial with { UnavailableReason = "insufficient-earlier-window" };
        if (ally is null)
            return initial with { UnavailableReason = scene.AtOrBeforeAnchor.Positions.Any(p => p.HeroId == viewerId)
                ? "anchor-ally-unobserved" : "anchor-viewer-unobserved" };
        var tracks = positions ?? [];
        if (tracks.Select(p => p.HeroId).Distinct().Count() != tracks.Count)
            throw new ArgumentException("Expected one position track per hero.", nameof(positions));
        var viewer = tracks.SingleOrDefault(p => p.HeroId == viewerId);
        var support = tracks.SingleOrDefault(p => p.HeroId == ally.AllyHeroId);
        var pairedSeconds = (viewer?.Samples ?? []).Where(p => p.Seconds >= earlierTarget && p.Seconds <= anchor)
            .Select(p => p.Seconds).Intersect((support?.Samples ?? [])
                .Where(p => p.Seconds >= earlierTarget && p.Seconds <= anchor).Select(p => p.Seconds)).Count();
        var previousViewer = Before(viewer, earlierTarget.Value);
        var previousAlly = Before(support, earlierTarget.Value);
        if (previousViewer is null || previousAlly is null)
            return initial with { ObservedPairedSeconds = pairedSeconds,
                UnavailableReason = previousViewer is null && previousAlly is null ? "earlier-both-unobserved"
                    : previousViewer is null ? "earlier-viewer-unobserved" : "earlier-ally-unobserved" };
        var distance = Math.Sqrt(Math.Pow((double)previousViewer.Value.X - previousAlly.Value.X, 2)
            + Math.Pow((double)previousViewer.Value.Y - previousAlly.Value.Y, 2));
        var change = ally.Distance.CompareTo(distance);
        return initial with
        {
            Earlier = new(earlierTarget.Value, previousViewer.Value.Seconds, previousAlly.Value.Seconds, distance),
            Change = change > 0 ? CoachingSupportChange.Increased : change < 0 ? CoachingSupportChange.Decreased : CoachingSupportChange.Unchanged,
            ObservedPairedSeconds = pairedSeconds
        };
    }

    private static PositionSample? Before(HeroPositions? track, int target) => track?.Samples
        .Where(p => p.Seconds >= Math.Max(0, target - MaxSampleAgeSeconds) && p.Seconds <= target)
        .Select(p => (PositionSample?)p).LastOrDefault();
}
