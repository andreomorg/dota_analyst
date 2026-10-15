using System.Globalization;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Coaching;

// A separate experimental contract: v1 archived decision inputs and outputs retain their semantics.
public sealed record GroundedCoachingScene(CoachingCanonicalScene Facts, string Observation,
    IReadOnlyList<string> EvidenceIds);
public sealed record GroundedCoachingRequest(int SchemaVersion, int HeroId, CoachingRole Role,
    IReadOnlyList<GroundedCoachingScene> Scenes);
public sealed record GroundedCoachingReflection(string EpisodeId, IReadOnlyList<string> EvidenceIds,
    string Hypothesis, string Alternative, string Exercise, string Limitations);
public sealed record GroundedCoachingResponse(int SchemaVersion, IReadOnlyList<GroundedCoachingReflection> Reflections);

public static class PrepareGroundedCoaching
{
    public static GroundedCoachingRequest Build(CoachingDecisionContext context)
    {
        if (context.SchemaVersion != 1 || context.Role.Position is < 1 or > 5
            || context.Scenes.Count > 3
            || context.Scenes.Any(s => s.RetrospectiveEpisode.HeroId != context.HeroId
                || string.IsNullOrWhiteSpace(s.RetrospectiveEpisode.Id))
            || context.Scenes.Select(s => s.RetrospectiveEpisode.Id).Distinct().Count() != context.Scenes.Count)
            throw new ArgumentException("Expected up to three unique scenes for the viewer.", nameof(context));
        return new(2, context.HeroId, context.Role, context.Scenes.Select(scene =>
        {
            var facts = CoachingCanonicalSceneBuilder.Build(scene);
            var state = facts.RetrospectiveState switch
            {
                "isolated-death" => "morte com isolamento confirmado pela cobertura do episódio",
                "non-isolated-death" => "morte sem isolamento confirmado: houve aliado próximo no episódio",
                "unknown-death-isolation" => "morte com isolamento desconhecido por cobertura insuficiente",
                "near-detected-fight" => "proximidade observada da luta detectada",
                "distant-detected-fight" => "distância confirmada da luta detectada",
                _ => "proximidade da luta desconhecida por cobertura insuficiente"
            };
            var observation = $"Evento retrospectivo em {facts.EventClock}: {state}. Âncora de revisão em {facts.AnchorClock}.";
            var refs = new List<string> { "outcome" };
            if (facts.NearestObservedAlly is { } ally)
            {
                observation += string.Create(CultureInfo.InvariantCulture,
                    $" Entre {ally.ObservedAllies} aliados observados, o mais próximo foi o herói {ally.AllyHeroId}, a {ally.Distance:0.##} unidades entre amostras de {ally.ViewerSampleSeconds}s e {ally.AllySampleSeconds}s. As amostras podem não ser simultâneas; aliados ausentes não foram avaliados.");
                refs.AddRange(ally.EvidenceIds);
            }
            else observation += " Não há posições suficientes para calcular distância a um aliado observado na âncora.";
            observation += " Isso não comprova erro, visão do jogador ou causa do resultado.";
            return new GroundedCoachingScene(facts, observation, refs);
        }).ToArray());
    }

    public static IReadOnlyList<string> Validate(GroundedCoachingRequest request, GroundedCoachingResponse response)
    {
        if (request.SchemaVersion != 2 || response.SchemaVersion != 2 || response.Reflections is null
            || response.Reflections.Count != request.Scenes.Count)
            return ["Expected a v2 reflection for every scene."];
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reflection in response.Reflections)
        {
            var scene = request.Scenes.SingleOrDefault(s => s.Facts.EpisodeId == reflection?.EpisodeId);
            if (reflection is null || scene is null || !used.Add(reflection.EpisodeId)
                || reflection.EvidenceIds is not { Count: > 0 and <= 3 }
                || reflection.EvidenceIds.Distinct().Count() != reflection.EvidenceIds.Count
                || reflection.EvidenceIds.Any(id => !scene.EvidenceIds.Contains(id))
                || new[] { reflection.Hypothesis, reflection.Alternative, reflection.Exercise, reflection.Limitations }
                    .Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 700))
                return ["Malformed reflection, unknown scene/evidence or oversized text."];
        }
        // Free prose can still contradict evidence. Structural validation does not authorize publication.
        return [];
    }
}
