using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>The facts of a kept replay around one hero at one second (internal). Seconds: the anchor; WindowStart and
/// EventsEnd: the closed window of the events (EventsEnd is the real end of the match in a final query). Final is null
/// unless the query asked for it at the end. Availability: per family of the document's additional facts. Hidden and
/// omniscient parts (the fog of war, the enemy's state) are named so in their types; nothing here is a visible fact
/// unless it says so. No account, player name, SteamID or free text.</summary>
public sealed record ReplayFacts(
    string MatchId, int HeroId, int Seconds, int WindowStart, int EventsEnd, ReplayFactsMetadata Metadata,
    ReplayFactsSnapshot Snapshot, ReplayFactsEvents Events, ReplayFactsFinal? Final,
    IReadOnlyDictionary<string, FactAvailability> Availability)
{
    public double ExactEventsEnd { get; init; } = EventsEnd;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance property is part of the serialized JSON contract.")]
    public string ObservationScope => "omniscientReplay";
    public IReadOnlyDictionary<string, FactSourceAvailability> AvailabilityDetails { get; init; }
        = new Dictionary<string, FactSourceAvailability>();
}
