using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

public enum ReplayFactsStatus
{
    Found,

    // No kept document for the match (or no storage on this machine).
    NoDocument,

    // The request does not fit the match (hero not in it, a second after the end, a final query not at the end).
    Invalid,
}

public sealed record ReplayFactsResult(ReplayFactsStatus Status, ReplayFacts? Facts, string? Problem = null);

// Use case: the facts of a kept replay around one hero at one second (work/data-flow-audit/contracts.md, D18), for the
// internal consumers. One read of the document and one catalog load per request, shared by every family. Only what was
// known by the anchor: the snapshot at the exact second, events in the closed window [max(0, seconds - windowSeconds),
// seconds], whole-match facts only in a final query (includeFinal, at the end). Never parses, refreshes or infers.
public sealed class GetReplayFactsHandler(IArchivedReplaySource replays, GameCatalogLoader catalogs)
{
    public const int MaxWindowSeconds = 300;

    public async Task<ReplayFactsResult> HandleAsync(
        MatchId matchId, int heroId, int seconds, int windowSeconds, bool includeFinal, CancellationToken cancellationToken)
    {
        if (heroId <= 0 || seconds < 0 || windowSeconds is < 0 or > MaxWindowSeconds)
        {
            return new(ReplayFactsStatus.Invalid, null, $"heroId > 0, seconds ≥ 0 and windowSeconds 0-{MaxWindowSeconds}.");
        }

        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay)
        {
            return new(ReplayFactsStatus.NoDocument, null);
        }

        if (replay.Metadata?.DurationSeconds is not { } duration || !double.IsFinite(duration) || duration < 0 || duration >= int.MaxValue)
        {
            return new(ReplayFactsStatus.Invalid, null, "The match duration is unknown.");
        }
        if (!replay.Analysis.Players.Any(player => player.HeroId == heroId))
        {
            return new(ReplayFactsStatus.Invalid, null, "The hero is not in the match.");
        }

        // A final query is at the end: the duration rounded up (the only rounding allowed); events stop at the real end.
        if (includeFinal ? seconds != (int)Math.Ceiling(duration) : seconds > duration)
        {
            return new(ReplayFactsStatus.Invalid, null, includeFinal
                ? $"A final query needs seconds = {(int)Math.Ceiling(duration)} (the end of the match)."
                : $"seconds must be at most the end of the match ({duration}).");
        }

        var (catalog, build) = await catalogs.LoadForMatchAsync(replay.StartedAt, cancellationToken);
        var windowStart = Math.Max(0, seconds - windowSeconds);
        var eventsEnd = includeFinal ? (int)Math.Floor(duration) : seconds;
        var facts = replay.Facts;
        var team = replay.Analysis.Players.First(player => player.HeroId == heroId).Team;
        var allies = replay.Analysis.Players.Where(player => player.Team == team).Select(player => player.HeroId).ToHashSet();
        var exactEventsEnd = includeFinal ? duration : seconds;
        bool InWindow(double moment) => moment >= windowStart && moment <= exactEventsEnd;

        var snapshot = new ReplayFactsSnapshot(
            HeroStateFacts.From(GetHeroStateHandler.Evaluate(replay, heroId, seconds, catalog, build)),
            InventoryFacts.At(replay.Activity, heroId, (int)Math.Floor(exactEventsEnd)),
            facts?.StolenAbilities?.Where(ability => ability.HeroId == heroId && ability.HeldAt(exactEventsEnd))
                .Select(ability => ability with { To = null }).ToList(),
            facts?.Tormentors?.Select(site => new TormentorAtAnchor(site.Side, site.X, site.Y,
                site.Samples.LastOrDefault(sample => sample.Seconds <= exactEventsEnd), site.RawSide)).ToList());
        var events = new ReplayFactsEvents(
            CombatFacts.Window(replay.Activity, heroId, windowStart, exactEventsEnd),
            CommunicationFacts.Window(replay.Activity, facts, heroId, windowStart, exactEventsEnd),
            MapFacts.Window(facts, heroId, windowStart, exactEventsEnd),
            facts?.Outposts?.Where(outpost => InWindow(outpost.Seconds)).ToList(),
            facts?.CourierDeaths?.Where(death => InWindow(death.Seconds)).ToList(),
            facts?.StolenCooldowns?.Where(cooldown => cooldown.HeroId == heroId && InWindow(cooldown.Seconds)).ToList());
        var final = includeFinal
            ? new ReplayFactsFinal(
                facts?.FinalStats?.FirstOrDefault(stats => stats.HeroId == heroId),
                facts?.FinalItems?.FirstOrDefault(items => items.HeroId == heroId),
                CombatFacts.FinalHealing(facts, heroId, allies))
            : null;

        return new(ReplayFactsStatus.Found, new ReplayFacts(
            matchId.ToString(), heroId, seconds, windowStart, eventsEnd,
            replay.Metadata is { } metadata
                ? new ReplayFactsMetadata(metadata.DurationSeconds, metadata.EngineBuild, metadata.ProducerVersion, metadata.MatchEnded, build?.Id)
                : new ReplayFactsMetadata(duration, null, null, null, build?.Id),
            snapshot, events, final,
            Availability(facts))
        {
            ExactEventsEnd = exactEventsEnd,
            AvailabilityDetails = Availability(facts).ToDictionary(entry => entry.Key,
                entry => new FactSourceAvailability(entry.Value, "keptReplay",
                    facts?.InvalidFamilies.Contains(entry.Key) == true ? "malformedFamily"
                        : entry.Value == FactAvailability.Unavailable ? "absentFamily" : "observedFamily")),
        });
    }

    // Per family of the replay's additional facts: available, or unavailable (absent in the document, or malformed).
    private static IReadOnlyDictionary<string, FactAvailability> Availability(ReplayAdditionalFacts? facts)
    {
        var families = new Dictionary<string, object?>
        {
            ["outposts"] = facts?.Outposts,
            ["tormentors"] = facts?.Tormentors,
            ["courierDeaths"] = facts?.CourierDeaths,
            ["healing"] = facts?.Healing,
            ["finalStats"] = facts?.FinalStats,
            ["finalItems"] = facts?.FinalItems,
            ["chatWheel"] = facts?.ChatWheel,
            ["tempTrees"] = facts?.TempTrees,
            ["bossTracks"] = facts?.BossTracks,
            ["lanterns"] = facts?.Lanterns,
            ["goldEvents"] = facts?.GoldEvents,
            ["stolenAbilities"] = facts?.StolenAbilities,
            ["stolenCooldowns"] = facts?.StolenCooldowns,
        };
        return families.ToDictionary(family => family.Key,
            family => family.Value is null ? FactAvailability.Unavailable : FactAvailability.Available);
    }
}

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
    public string ObservationScope => "omniscientReplay";
    public IReadOnlyDictionary<string, FactSourceAvailability> AvailabilityDetails { get; init; }
        = new Dictionary<string, FactSourceAvailability>();
}

public sealed record FactSourceAvailability(FactAvailability Status, string Source, string Reason);

/// <summary>Where the document and the game data came from: the replay's duration and engine build, the parser's version
/// when known, whether the match ended in the replay, and the game build (catalog) used for the hero's nominal data.</summary>
public sealed record ReplayFactsMetadata(double DurationSeconds, uint? EngineBuild, string? ProducerVersion, bool? MatchEnded, string? CatalogBuild);

/// <summary>The hero at the anchor: its state (each source apart), its items, and the stolen abilities it held then.</summary>
public sealed record ReplayFactsSnapshot(HeroStateFacts Hero, InventoryAt Items, IReadOnlyList<StolenAbility>? StolenHeld,
    IReadOnlyList<TormentorAtAnchor>? Tormentors);

public sealed record TormentorAtAnchor(DotaAnalyst.Domain.Matches.Team? Side, int X, int Y,
    AliveObservation? Observation, string? RawSide);

/// <summary>What happened in the window: the hero's combat, signals and casts, the map's state around it, and the
/// match's objective events (outposts captured, couriers killed) and the hero's stolen abilities' cooldowns.</summary>
public sealed record ReplayFactsEvents(
    CombatWindowFacts Combat, CommunicationWindowFacts Communication, MapWindowFacts Map,
    IReadOnlyList<OutpostObservation>? Outposts, IReadOnlyList<CourierDeath>? CourierDeaths,
    IReadOnlyList<StolenCooldown>? StolenCooldowns);

/// <summary>Whole-match facts of the hero, only in a final query: the replay's own scoreboard, its items at the end and its
/// healing over the match.</summary>
public sealed record ReplayFactsFinal(FinalHeroStats? Stats, FinalInventory? Items, HealingFinalFacts Healing);
