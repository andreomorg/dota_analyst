using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using LaneOf = DotaAnalyst.Domain.Replays.Lane;

namespace DotaAnalyst.Domain.Analysis;

// Domain service: where each objective was taken, for the presence of MapObjectiveMetrics. An objective whose place
// can't be told for sure is left out (its presence count then stays unknown).
// - Towers 1-3: where the patch's map has them, by their map name (dota_goodguys_tower1_top...).
// - Roshan and Tormentors: by the boss tracks (objectives.bossTracks), which are sparse — a sample only when the unit
//   moved 200 or lived/died — and whose "dead" sample can come over a minute after the kill (0-77 s on the owner's
//   matches, 23/10/2026). Each kill is matched to one death of a track: a death (alive -> dead) from DeathEarly before
//   the kill to DeathDelay after it, of a life already alive before the kill. Two Tormentors live at once and their late
//   deaths can come in either order, so a kill with more than one possible death, or a death that could belong to more
//   than one kill, is ambiguous and left out. The place is the track's latest sample by the kill (Roshan walks between
//   his two pits in 7.41: the place he was, not his pit).
public static class ObjectivePlaces
{
    public const int DeathEarly = 3;

    public const int DeathDelay = 90;

    public static IReadOnlyList<ObjectivePlace> Locate(
        IReadOnlyList<MapObjective> objectives, IReadOnlyList<BossTrack>? bossTracks, GameMap? map)
    {
        var places = new List<ObjectivePlace>();
        foreach (var objective in objectives)
        {
            if (objective is { Kind: ObjectiveKind.Tower, TowerTier: <= 3 and var tier, Lane: { } lane, Owner: { } owner }
                && map?.Landmarks.FirstOrDefault(landmark => landmark.Kind == LandmarkKind.Tower
                    && landmark.Name == TowerName(owner, tier, lane)) is { } tower)
            {
                places.Add(new ObjectivePlace(objective, tower.X, tower.Y));
            }
        }

        if (bossTracks is not null)
        {
            places.AddRange(BossPlaces(objectives, bossTracks, ObjectiveKind.Roshan, "roshan"));
            places.AddRange(BossPlaces(objectives, bossTracks, ObjectiveKind.Tormentor, "tormentor"));
        }

        return places;
    }

    private static IEnumerable<ObjectivePlace> BossPlaces(
        IReadOnlyList<MapObjective> objectives, IReadOnlyList<BossTrack> tracks, ObjectiveKind kind, string trackKind)
    {
        var kills = objectives.Where(objective => objective.Kind == kind).ToList();
        // Every death of every track of this kind: the track, the dead sample and the index of that sample.
        var deaths = tracks.Where(track => track.Kind == trackKind)
            .SelectMany(track => track.Samples.Select((sample, index) => (Track: track, Sample: sample, Index: index))
                .Where(entry => !entry.Sample.Alive && (entry.Index == 0 || entry.Track.Samples[entry.Index - 1].Alive)))
            .ToList();

        // The possible deaths of each kill: in the window, with the same life seen alive by the kill.
        var candidates = kills.ToDictionary(kill => kill, kill => deaths.Where(death =>
            death.Sample.Seconds >= kill.Seconds - DeathEarly && death.Sample.Seconds <= kill.Seconds + DeathDelay
            && death.Index > 0 && death.Track.Samples[death.Index - 1].Seconds <= kill.Seconds).ToList());

        foreach (var kill in kills)
        {
            if (candidates[kill] is [var death]
                && kills.Count(other => candidates[other].Any(entry => entry.Track == death.Track && entry.Index == death.Index)) == 1)
            {
                var place = death.Track.Samples.Take(death.Index).Last(sample => sample.Seconds <= kill.Seconds);
                yield return new ObjectivePlace(kill, place.X, place.Y);
            }
        }
    }

    private static string TowerName(Team owner, int tier, LaneOf lane) =>
        $"dota_{(owner == Team.Radiant ? "goodguys" : "badguys")}_tower{tier}_{lane switch { LaneOf.Top => "top", LaneOf.Middle => "mid", _ => "bot" }}";
}
