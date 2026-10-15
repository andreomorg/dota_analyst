namespace DotaAnalyst.Domain.Coaching;

// Facts are computed by the existing domain services. The model may interpret them, never replace them.
public sealed record CoachingEvidence(
    string Id, string Metric, double Value, string Unit, int FromSeconds, int ToSeconds,
    string Source, string Limitation);

public sealed record CoachingRole(int Position, string Source, bool IsInferred);

// A major patch ID is not a guarantee that a historical subpatch or item definition is known.
public sealed record CoachingPatch(
    string? MatchPatch, string Source, string? TrainingPatch, bool MechanicsVerified);

public sealed record CoachingContext(
    int SchemaVersion, string MatchId, int HeroId, CoachingRole Role, CoachingPatch Patch,
    int DurationSeconds, string Perspective, IReadOnlyList<CoachingEvidence> Evidence,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<CoachingFinding>? Findings = null);

public sealed record CoachingSuggestion(
    string Topic, string Interpretation, string Action, string Exercise,
    IReadOnlyList<string> EvidenceIds,
    string? FindingId = null, string? ExerciseId = null);

public sealed record CoachingAdvice(IReadOnlyList<CoachingSuggestion> Suggestions);

public static class CoachingAdviceValidator
{
    public static IReadOnlyList<string> Validate(CoachingContext context, CoachingAdvice advice)
    {
        var errors = new List<string>();
        if (advice.Suggestions is null || advice.Suggestions.Count > 3)
            return ["Expected zero to three suggestions."];
        var ids = context.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var suggestion in advice.Suggestions)
        {
            if (suggestion is null)
            {
                errors.Add("Empty suggestion.");
                continue;
            }
            if (new[] { suggestion.Topic, suggestion.Interpretation, suggestion.Action, suggestion.Exercise }
                .Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 1600))
                errors.Add("Missing or oversized suggestion text.");
            if (suggestion.EvidenceIds is not { Count: > 0 } || suggestion.EvidenceIds.Any(id => !ids.Contains(id)))
                errors.Add("Every suggestion needs valid evidence references.");
        }
        // This checks structure and evidence links, not tactical truth. Human review remains mandatory for release.
        return errors;
    }
}
