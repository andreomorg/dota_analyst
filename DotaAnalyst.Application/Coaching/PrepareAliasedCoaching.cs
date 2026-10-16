using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Coaching;

// Only Request is serializable publicly. The reverse map must never be sent to the model.
public sealed class AliasedCoachingInput
{
    public BoundedCoachingRequest Request { get; }
    private readonly IReadOnlyDictionary<string, CoachingSceneReference> references;

    internal AliasedCoachingInput(BoundedCoachingRequest request, IReadOnlyDictionary<string, CoachingSceneReference> references)
    {
        Request = request;
        this.references = references;
    }

    public GroundedCoachingResponse Restore(GroundedCoachingResponse response)
    {
        var errors = PrepareBoundedCoaching.Validate(Request, response);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        return response with
        {
            Reflections = response.Reflections.Select(reflection =>
            {
                var source = references[reflection.EpisodeId];
                return reflection with { EpisodeId = source.EpisodeId,
                    EvidenceIds = reflection.EvidenceIds.Select(id => source.EvidenceIds[id]).ToArray() };
            }).ToArray()
        };
    }
}

internal sealed record CoachingSceneReference(string EpisodeId, IReadOnlyDictionary<string, string> EvidenceIds);

public static class PrepareAliasedCoaching
{
    public static AliasedCoachingInput Build(CoachingDecisionContext context)
    {
        var original = PrepareBoundedCoaching.Build(context);
        var references = new Dictionary<string, CoachingSceneReference>(StringComparer.Ordinal);
        string[] evidenceAliases = ["outcome", "anchor-viewer", "anchor-ally"];
        var scenes = original.Scenes.Select((scene, index) =>
        {
            var alias = "scene-" + (char)('a' + index);
            var evidence = scene.EvidenceIds.Select((id, i) => (Alias: evidenceAliases[i], Id: id)).ToArray();
            references.Add(alias, new(scene.EpisodeId, evidence.ToDictionary(e => e.Alias, e => e.Id, StringComparer.Ordinal)));
            return scene with { EpisodeId = alias, EvidenceIds = evidence.Select(e => e.Alias).ToArray() };
        }).ToArray();
        return new(original with { Scenes = scenes }, references);
    }
}
