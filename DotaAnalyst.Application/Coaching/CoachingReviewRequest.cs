using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Coaching;

/// <summary>Versioned evidence payload for the internal Python review service.</summary>
public sealed record CoachingReviewRequest(int SchemaVersion, int HeroId, CoachingRole Role, CoachingPatch Patch,
    string Perspective, IReadOnlyList<string> Limitations, IReadOnlyList<CoachingReviewScene> Scenes);

/// <summary>One bounded scene with earlier evidence and a separately identified retrospective outcome.</summary>
public sealed record CoachingReviewScene(string EpisodeId, int AnchorSeconds, int MissingHeroPositions,
    IReadOnlyList<CoachingSceneEvidence> Evidence);

/// <summary>Experimental review with explicit engine provenance; never a human tactical approval.</summary>
public sealed record CoachingReviewResult(int SchemaVersion, string Engine, bool HumanReviewed, CoachingHypothesisReport Report);

public static class PrepareCoachingReview
{
    public static CoachingReviewRequest Build(CoachingDecisionContext context)
    {
        if (context.SchemaVersion != 1 || context.HeroId <= 0 || context.Role is null || context.Role.Position is < 1 or > 5
            || context.Scenes is null || context.Scenes.Count > 3
            || context.Scenes.Any(s => s is null || s.RetrospectiveEpisode is null || s.AtOrBeforeAnchor is null
                || string.IsNullOrWhiteSpace(s.RetrospectiveEpisode.Id) || s.RetrospectiveEpisode.HeroId != context.HeroId)
            || context.Scenes.Select(s => s.RetrospectiveEpisode.Id).Distinct(StringComparer.Ordinal).Count() != context.Scenes.Count)
            throw new InvalidDataException("Expected up to three unique decision scenes for the viewer.");
        var scenes = context.Scenes.Select(scene =>
        {
            _ = CoachingCanonicalSceneBuilder.Build(scene);
            var evidence = CoachingSceneEvidenceBuilder.Build(scene);
            if (evidence.Any(e => e.Kind != "retrospective" && e.Seconds > scene.AtOrBeforeAnchor.AnchorSeconds))
                throw new InvalidDataException("Future evidence cannot enter the earlier snapshot.");
            return new CoachingReviewScene(scene.RetrospectiveEpisode.Id, scene.AtOrBeforeAnchor.AnchorSeconds,
                scene.AtOrBeforeAnchor.MissingHeroPositions, evidence);
        }).ToArray();
        return new(1, context.HeroId, context.Role, context.Patch, context.Perspective, context.Limitations, scenes);
    }
}
