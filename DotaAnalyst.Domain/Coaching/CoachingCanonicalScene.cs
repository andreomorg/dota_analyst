using System.Globalization;

namespace DotaAnalyst.Domain.Coaching;

// Distance between the two recorded samples, not a synchronized frame or a visibility claim.
public sealed record CoachingObservedAllyDistance(
    int AllyHeroId, int ViewerSampleSeconds, int AllySampleSeconds, double Distance,
    int ObservedAllies, IReadOnlyList<string> EvidenceIds);

public sealed record CoachingCanonicalScene(
    int SchemaVersion, string EpisodeId, string EventClock, string AnchorClock,
    string RetrospectiveState, CoachingObservedAllyDistance? NearestObservedAlly);

public static class CoachingCanonicalSceneBuilder
{
    public static CoachingCanonicalScene Build(CoachingDecisionScene scene)
    {
        var episode = scene.RetrospectiveEpisode;
        var facts = scene.AtOrBeforeAnchor;
        if (episode.EventSeconds < 0 || facts.AnchorSeconds < 0 || facts.AnchorSeconds > episode.EventSeconds)
            throw new ArgumentException("Expected nonnegative event and earlier anchor.", nameof(scene));
        if (facts.Positions.Any(p => p.SampleSeconds < 0 || p.SampleSeconds > facts.AnchorSeconds
                || (long)facts.AnchorSeconds - p.SampleSeconds > 2 || !Enum.IsDefined(p.Team))
            || facts.Positions.Select(p => p.HeroId).Distinct().Count() != facts.Positions.Count)
            throw new ArgumentException("Expected unique, valid positions sampled within two seconds before anchor.", nameof(scene));

        var state = episode.Kind switch
        {
            CoachingEpisodeKind.Death => episode.IsolatedDeath switch
            {
                true => "isolated-death", false => "non-isolated-death", null => "unknown-death-isolation"
            },
            CoachingEpisodeKind.DetectedFight => episode.NearFight switch
            {
                true => "near-detected-fight", false => "distant-detected-fight", null => "unknown-fight-proximity"
            },
            _ => throw new ArgumentException("Unknown episode kind.", nameof(scene))
        };
        CoachingObservedAllyDistance? distance = null;
        var viewer = facts.Positions.SingleOrDefault(p => p.HeroId == episode.HeroId);
        if (viewer is not null)
        {
            var allies = facts.Positions.Where(p => p.HeroId != viewer.HeroId && p.Team == viewer.Team).ToArray();
            var nearest = allies.Select(p => new
                {
                    Position = p,
                    // Cast before subtraction: replay coordinates should not overflow intermediate integers.
                    Distance = Math.Sqrt(Math.Pow((double)p.X - viewer.X, 2) + Math.Pow((double)p.Y - viewer.Y, 2))
                })
                .OrderBy(p => p.Distance).ThenBy(p => p.Position.HeroId).FirstOrDefault();
            if (nearest is not null)
                distance = new(nearest.Position.HeroId, viewer.SampleSeconds, nearest.Position.SampleSeconds,
                    nearest.Distance, allies.Length, [$"position-{viewer.HeroId}", $"position-{nearest.Position.HeroId}"]);
        }
        return new(1, episode.Id, Clock(episode.EventSeconds), Clock(facts.AnchorSeconds), state, distance);
    }

    private static string Clock(int seconds) => string.Create(CultureInfo.InvariantCulture,
        $"{seconds / 60:00}:{seconds % 60:00}");
}
