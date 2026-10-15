using DotaAnalyst.Domain.Analysis;

namespace DotaAnalyst.Domain.Coaching;

public enum CoachingFindingKind
{
    IsolatedDeathsReview,
    FightProximityReview,
    QuickRedeathsReview
}

// These are review opportunities, never proof of a bad decision or patch-dependent ideal play.
public sealed record CoachingFinding(
    string Id, CoachingFindingKind Kind, string RuleVersion, int Role,
    int FromSeconds, int ToSeconds, IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> ExerciseIds);

public sealed record CoachingChoice(string FindingId, string ExerciseId);

public static class CoachingFindingSelector
{
    public static IReadOnlyList<CoachingFinding> Select(CoachingContext context)
    {
        if (context.Role.Position is < 1 or > 5 || context.DurationSeconds <= 0) return [];
        var findings = new List<CoachingFinding>();
        CoachingEvidence? Fact(string metric)
        {
            var matches = context.Evidence.Where(e => e.Metric == metric).ToArray();
            return matches.Length == 1 && double.IsFinite(matches[0].Value)
                && matches[0].Value >= 0 && matches[0].FromSeconds >= 0
                && matches[0].ToSeconds >= matches[0].FromSeconds
                && matches[0].ToSeconds <= context.DurationSeconds ? matches[0] : null;
        }
        static bool Count(CoachingEvidence fact) => fact.Value == Math.Truncate(fact.Value);
        void Add(string id, CoachingFindingKind kind, params CoachingEvidence[] evidence)
        {
            if (evidence.Any(e => e.Unit != "count") || evidence.Select(e => e.Id).Distinct().Count() != evidence.Length) return;
            // Both totals must describe the same window; do not silently compare a partial replay to a full one.
            if (evidence.Any(e => e.FromSeconds != 0 || e.ToSeconds != context.DurationSeconds)) return;
            findings.Add(new(id, kind, "1", context.Role.Position, 0, context.DurationSeconds,
                evidence.Select(e => e.Id).ToArray(), [$"{id}-role-{context.Role.Position}"]));
        }
        if (Fact("Deaths") is { } deaths && Count(deaths)
            && Fact("IsolatedDeaths") is { } isolated && Count(isolated)
            && isolated.Value <= deaths.Value && isolated.Value >= InsightGenerator.MinIsolatedDeaths
            && isolated.Value >= InsightGenerator.MinIsolatedShare * deaths.Value)
            Add("review-isolated-deaths", CoachingFindingKind.IsolatedDeathsReview, deaths, isolated);

        if (Fact("TeamFights") is { } fights && Count(fights) && fights.Value >= InsightGenerator.MinTeamFights
            && Fact("FightsNearby") is { } nearby && Count(nearby) && nearby.Value < fights.Value)
            Add("review-fight-proximity", CoachingFindingKind.FightProximityReview, fights, nearby);

        if (Fact("Deaths") is { } total && Count(total)
            && Fact("QuickRedeaths") is { } quick && Count(quick)
            && quick.Value >= InsightGenerator.MinQuickRedeaths && quick.Value <= total.Value)
            Add("review-quick-redeaths", CoachingFindingKind.QuickRedeathsReview, total, quick);
        return findings;
    }

    public static IReadOnlyList<string> ValidateChoices(CoachingContext context, IReadOnlyList<CoachingChoice>? choices)
    {
        if (context.SchemaVersion != 2 || context.Findings is null || context.Evidence is null
            || context.Role is null || context.Role.Position is < 1 or > 5)
            return ["Expected coaching context v2."];
        if (context.Evidence.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id))
            || context.Evidence.Select(e => e.Id).Distinct().Count() != context.Evidence.Count
            || context.Findings.Any(f => f is null || string.IsNullOrWhiteSpace(f.Id)
                || f.EvidenceIds is null || f.ExerciseIds is null)
            || context.Findings.Select(f => f.Id).Distinct().Count() != context.Findings.Count)
            return ["Malformed evidence or findings."];
        if (choices is null || choices.Count > 3) return ["Expected zero to three choices."];
        // Recompute eligibility from the evidence. A stale or forged candidate cannot bypass the domain rules.
        var eligible = Select(context).ToDictionary(f => f.Id, StringComparer.Ordinal);
        var supplied = context.Findings.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var choice in choices)
        {
            if (choice is null || choice.FindingId is null || !used.Add(choice.FindingId)
                || !eligible.TryGetValue(choice.FindingId, out var finding)
                || !supplied.TryGetValue(choice.FindingId, out var candidate)
                || candidate.Role != finding.Role || candidate.Kind != finding.Kind
                || candidate.RuleVersion != finding.RuleVersion
                || candidate.FromSeconds != finding.FromSeconds || candidate.ToSeconds != finding.ToSeconds
                || !candidate.EvidenceIds.SequenceEqual(finding.EvidenceIds)
                || !candidate.ExerciseIds.SequenceEqual(finding.ExerciseIds)
                || !finding.ExerciseIds.Contains(choice.ExerciseId, StringComparer.Ordinal))
                errors.Add("Ineligible, duplicate or mismatched finding/exercise choice.");
        }
        return errors;
    }
}
