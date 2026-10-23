using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>A boss (Roshan or Tormentor) as last observed at or before the anchor: ObservedAt is that sample's second
/// (the unit may have moved since: samples are sparse and never interpolated).</summary>
public sealed record BossObservation(string Kind, int ObservedAt, int X, int Y, bool Alive);

/// <summary>The map's changing state around a hero in a closed window [From, To]: temporary trees standing at To and the
/// ones that appeared in the window, each boss as last observed by To, the Watchers the hero activated, and the hero's
/// gold changes with location (gained, lost, by raw reason). Null: the family is unavailable.</summary>
public sealed record MapWindowFacts(
    FactAvailability TempTrees, FactAvailability Bosses, FactAvailability Lanterns, FactAvailability Gold, int From, double To,
    int? TempTreesStanding, int? TempTreesAppeared, IReadOnlyList<BossObservation>? BossesAtAnchor, int? LanternsActivated,
    long? GoldGained, long? GoldLost, IReadOnlyDictionary<string, long>? GoldByReason, int? GoldEventsWithoutLocation);

// Read-only facts about the map's state for the internal factual consumer (D18). Never reaches past the anchor.
public static class MapFacts
{
    public static MapWindowFacts Window(ReplayAdditionalFacts? facts, int heroId, int from, double to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        bool Inside(double seconds) => seconds >= from && seconds <= to;

        // A temporary tree stands at To when its last change up to To (by place) was an appearance.
        int? standing = facts?.TempTrees?.Where(change => change.Seconds <= to)
            .GroupBy(change => (change.X, change.Y))
            .Count(place => place.Last().Present);
        var appeared = facts?.TempTrees?.Count(change => change.Present && Inside(change.Seconds));
        var bosses = facts?.BossTracks?
            .Select(track => (track.Kind, Last: track.LastAt((int)Math.Floor(to))))
            .Where(track => track.Last is not null)
            .Select(track => new BossObservation(track.Kind, track.Last!.Seconds, track.Last.X, track.Last.Y, track.Last.Alive))
            .ToList();
        var lanterns = facts?.Lanterns?.Count(use => use.HeroId == heroId && Inside(use.Seconds));
        var gold = facts?.GoldEvents?.Where(change => change.HeroId == heroId && Inside(change.Seconds)).ToList();

        return new MapWindowFacts(
            Availability(facts?.TempTrees), Availability(facts?.BossTracks), Availability(facts?.Lanterns), Availability(facts?.GoldEvents),
            from, to, standing, appeared, bosses, lanterns,
            gold?.Where(change => change.Amount > 0).Sum(change => change.Amount),
            gold?.Where(change => change.Amount < 0).Sum(change => -change.Amount),
            gold?.GroupBy(change => change.Reason?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown").ToDictionary(group => group.Key, group => group.Sum(change => change.Amount)),
            gold?.Count(change => change.X is null || change.Y is null));
    }

    private static FactAvailability Availability<T>(IReadOnlyList<T>? family) =>
        family is null ? FactAvailability.Unavailable : FactAvailability.Available;
}
