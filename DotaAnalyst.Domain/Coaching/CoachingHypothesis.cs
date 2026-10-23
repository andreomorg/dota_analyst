namespace DotaAnalyst.Domain.Coaching;

public sealed record CoachingSceneEvidence(string Id, string Kind, int Seconds, string Description);
public sealed record CoachingHypothesis(string EpisodeId, string Disposition, IReadOnlyList<string> EvidenceIds,
    string Hypothesis, string Alternative, string Exercise, string Limitations);
public sealed record CoachingHypothesisReport(IReadOnlyList<CoachingHypothesis> Assessments);

public static class CoachingSceneEvidenceBuilder
{
    public static IReadOnlyList<CoachingSceneEvidence> Build(CoachingDecisionScene scene)
    {
        var facts = scene.AtOrBeforeAnchor;
        var episode = scene.RetrospectiveEpisode;
        var evidence = new List<CoachingSceneEvidence>
        {
            new("outcome", "retrospective", episode.EventSeconds,
                $"Retrospective {episode.Kind}; isolated={episode.IsolatedDeath?.ToString() ?? "unknown"}; nearFight={episode.NearFight?.ToString() ?? "unknown"}. Not player knowledge at anchor.")
        };
        foreach (var p in facts.Positions)
            evidence.Add(new($"position-{p.HeroId}", "omniscient-position", p.SampleSeconds,
                FormattableString.Invariant($"Hero {p.HeroId}, team {p.Team}, position ({p.X},{p.Y}); visibility unknown.")));
        if (facts.Economy is { } economy)
            evidence.Add(new("economy", "earlier-snapshot", economy.SampleSeconds,
                $"Net worth {economy.NetWorth}; last hits {economy.LastHits?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}. Not gold carried or causal gain."));
        foreach (var (purchase, index) in (facts.RecentPurchases ?? []).TakeLast(3).Select((p, i) => (p, i)))
            evidence.Add(new($"purchase-{index}", "purchase", purchase.Seconds,
                $"Hero {purchase.HeroId} purchase {purchase.ItemKey}; delivery, inventory and readiness unknown."));
        foreach (var (upgrade, index) in (facts.RecentAbilityUpgrades ?? []).TakeLast(3).Select((p, i) => (p, i)))
            evidence.Add(new($"upgrade-{index}", "ability-upgrade", upgrade.Seconds,
                $"Hero {upgrade.HeroId} learned {upgrade.AbilityName} level {upgrade.Level}; cooldown and mana unknown."));
        foreach (var (objective, index) in (facts.RecentObjectives ?? []).TakeLast(3).Select((p, i) => (p, i)))
            evidence.Add(new($"objective-{index}", "objective", objective.Seconds,
                $"{objective.Kind} taken by {objective.TakenBy}; no causal link to the later episode is established."));
        return evidence;
    }
}

public static class CoachingHypothesisValidator
{
    public static IReadOnlyList<string> Validate(CoachingDecisionContext context, CoachingHypothesisReport report)
    {
        if (report.Assessments is null || report.Assessments.Count != context.Scenes.Count)
            return ["Expected one assessment per supplied scene, including explicit abstentions."];
        var scenes = context.Scenes.ToDictionary(s => s.RetrospectiveEpisode.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in report.Assessments)
        {
            if (item is null || item.EpisodeId is null || !used.Add(item.EpisodeId)
                || !scenes.TryGetValue(item.EpisodeId, out var scene)
                || item.Disposition is not ("review" or "insufficient")
                || item.EvidenceIds is not { Count: > 0 and <= 8 }
                || item.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != item.EvidenceIds.Count
                || item.EvidenceIds.Any(id => !CoachingSceneEvidenceBuilder.Build(scene).Any(e => e.Id == id))
                || new[] { item.Hypothesis, item.Alternative, item.Exercise, item.Limitations }
                    .Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 700))
                return ["Malformed assessment, unknown scene/evidence or oversized text."];
        }
        // References are verifiable. Free-form tactical hypotheses still require content review.
        return [];
    }
}
