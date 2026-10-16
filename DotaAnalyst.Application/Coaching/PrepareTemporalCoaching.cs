using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Coaching;

public sealed record CoachingSupportInspection(string Change, string EarlierAvailability, string IntermediateCoverage);
public sealed record TemporalCoachingScene(string EpisodeId, string RetrospectiveState,
    CoachingSceneAvailability Availability, CoachingSupportInspection SupportTransition, IReadOnlyList<string> EvidenceIds);
public sealed record TemporalCoachingRequest(int SchemaVersion, string Role, IReadOnlyList<TemporalCoachingScene> Scenes);

public sealed class TemporalCoachingInput
{
    public TemporalCoachingRequest Request { get; }
    private readonly IReadOnlyDictionary<string, CoachingSceneReference> references;
    private readonly IReadOnlyList<CoachingSupportTransition> provenance;

    internal TemporalCoachingInput(TemporalCoachingRequest request,
        IReadOnlyDictionary<string, CoachingSceneReference> references, IReadOnlyList<CoachingSupportTransition> provenance)
    {
        Request = request;
        this.references = references;
        this.provenance = provenance;
    }

    // Explicit backend inspection; not a public property serialized into the model request.
    public IReadOnlyList<CoachingSupportTransition> GetProvenance() => provenance;

    public GroundedCoachingResponse Restore(GroundedCoachingResponse response)
    {
        if (response.SchemaVersion != 4 || response.Reflections is null || response.Reflections.Count != Request.Scenes.Count)
            throw new InvalidDataException("Expected a v4 reflection for every scene.");
        var used = new HashSet<string>(StringComparer.Ordinal);
        var restored = new List<GroundedCoachingReflection>();
        foreach (var reflection in response.Reflections)
        {
            if (reflection is null || reflection.EpisodeId is null || !used.Add(reflection.EpisodeId)
                || !references.TryGetValue(reflection.EpisodeId, out var map)
                || reflection.EvidenceIds is not { Count: > 0 and <= 4 }
                || reflection.EvidenceIds.Distinct().Count() != reflection.EvidenceIds.Count
                || reflection.EvidenceIds.Any(id => !map.EvidenceIds.ContainsKey(id))
                || new[] { reflection.Hypothesis, reflection.Alternative, reflection.Exercise, reflection.Limitations }
                    .Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 700))
                throw new InvalidDataException("Malformed reflection or unknown scene/evidence alias.");
            restored.Add(reflection with { EpisodeId = map.EpisodeId,
                EvidenceIds = reflection.EvidenceIds.Select(id => map.EvidenceIds[id]).ToArray() });
        }
        return response with { Reflections = restored };
    }
}

public static class PrepareTemporalCoaching
{
    public static TemporalCoachingInput Build(CoachingDecisionContext context, IReadOnlyList<HeroPositions>? positions)
    {
        var bounded = PrepareBoundedCoaching.Build(context);
        var references = new Dictionary<string, CoachingSceneReference>(StringComparer.Ordinal);
        var provenance = new List<CoachingSupportTransition>();
        var scenes = new List<TemporalCoachingScene>();
        string[] originalAliases = ["outcome", "anchor-viewer", "anchor-ally"];
        for (var index = 0; index < context.Scenes.Count; index++)
        {
            var source = bounded.Scenes[index];
            var observation = CoachingSupportTransitionBuilder.Build(context.Scenes[index], positions);
            provenance.Add(observation);
            var alias = "scene-" + (char)('a' + index);
            var map = source.EvidenceIds.Select((id, i) => (Id: id, Alias: originalAliases[i]))
                .ToDictionary(e => e.Alias, e => e.Id, StringComparer.Ordinal);
            if (observation.Change != CoachingSupportChange.Unavailable)
                map.Add("support-transition", "support-transition:" + source.EpisodeId);
            var change = observation.Change switch
            {
                CoachingSupportChange.Increased => "recorded-separation-increased",
                CoachingSupportChange.Decreased => "recorded-separation-decreased",
                CoachingSupportChange.Unchanged => "recorded-separation-unchanged",
                _ => "unavailable"
            };
            var coverage = observation.ExpectedPairedSeconds == 0 ? "no-window"
                : observation.ObservedPairedSeconds == 0 ? "no-paired-seconds"
                : observation.ObservedPairedSeconds == observation.ExpectedPairedSeconds ? "all-paired-seconds" : "incomplete-paired-seconds";
            references.Add(alias, new(source.EpisodeId, map));
            scenes.Add(new(alias, source.RetrospectiveState, source.Availability,
                new(change, observation.UnavailableReason ?? "available", coverage), map.Keys.ToArray()));
        }
        return new(new(4, bounded.Role, scenes), references, provenance.AsReadOnly());
    }
}
