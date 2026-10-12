using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// The JSON format of a saved replay analysis, separate from the domain model on purpose (like StoredMatchDetails):
// renaming a domain property must not silently change what is already in the database.
internal sealed record StoredReplayAnalysis(
    // Bumped when the format changes in a way old rows can't be read as-is.
    int Version,
    long MatchId,
    int EndsAtSeconds,
    IReadOnlyList<StoredLaningStats> Players,
    // Added in sprint 11 without a new Version: rows saved before have none (null) and still read.
    StoredTimeline? Timeline = null,
    // Added in sprint 12, the same way.
    IReadOnlyList<StoredHeroDeath>? Deaths = null);

internal sealed record StoredHeroDeath(
    int HeroId, int Seconds, int X, int Y, KillerKind Killer, int? KillerHeroId, bool Reincarnated);

internal sealed record StoredTimeline(IReadOnlyList<int> Seconds, IReadOnlyList<StoredHeroTimeline> Heroes);

internal sealed record StoredHeroTimeline(int HeroId, Team Team, IReadOnlyList<int> NetWorth, IReadOnlyList<int> Experience);

internal sealed record StoredLaningStats(
    int HeroId,
    Team Team,
    Lane Lane,
    int LastHits,
    int Denies,
    int NetWorth,
    int Experience,
    int Deaths);

// SavedReplayAnalysis <-> a replay_analyses row.
internal static class StoredReplayMappings
{
    public const int CurrentVersion = 1;

    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Expired = "expired";

    // Enums as text ("radiant", "bottom"): readable in the database and safe if enum values are reordered.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string ToStatus(SavedReplayState state) => state switch
    {
        SavedReplayState.Ready => Ready,
        SavedReplayState.Failed => Failed,
        SavedReplayState.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown replay state."),
    };

    // Null for every state but Ready.
    public static string? ToJson(SavedReplayAnalysis result) =>
        result.Analysis is { } analysis ? JsonSerializer.Serialize(ToStored(analysis), JsonOptions) : null;

    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidDataException">The row can't become a result.</exception>
    public static SavedReplayAnalysis ToDomain(long matchIdValue, string status, string? data, DateTimeOffset savedAt)
    {
        if (!MatchId.TryCreate(matchIdValue, out var matchId))
        {
            throw new InvalidDataException($"A saved replay analysis has an invalid match ID ({matchIdValue}).");
        }

        return status switch
        {
            Ready => SavedReplayAnalysis.Ready(FromJson(matchId, data), savedAt),
            Failed => SavedReplayAnalysis.Failed(matchId, savedAt),
            Expired => SavedReplayAnalysis.Expired(matchId, savedAt),
            _ => throw new InvalidDataException($"A saved replay analysis has an unknown status '{status}'."),
        };
    }

    private static ReplayAnalysis FromJson(MatchId matchId, string? data)
    {
        var stored = JsonSerializer.Deserialize<StoredReplayAnalysis>(
                data ?? throw new InvalidDataException("A ready replay analysis has no data."), JsonOptions)
            ?? throw new InvalidDataException("A saved replay analysis is empty.");

        if (stored.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Saved replay analysis format version {stored.Version} is not supported.");
        }

        if (stored.MatchId != matchId.Value)
        {
            throw new InvalidDataException($"A saved replay analysis of match {matchId} says match {stored.MatchId}.");
        }

        try
        {
            return new ReplayAnalysis(
                matchId,
                stored.EndsAtSeconds,
                (stored.Players ?? []).Select(player => player is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty player.")
                        : new LaningStats(player.HeroId, player.Team, player.Lane, player.LastHits, player.Denies,
                            player.NetWorth, player.Experience, player.Deaths))
                    .ToList(),
                stored.Timeline is { } timeline
                    ? new MatchTimeline(
                        timeline.Seconds ?? throw new InvalidDataException("A saved timeline has no seconds."),
                        (timeline.Heroes ?? []).Select(hero => hero is null
                                ? throw new InvalidDataException("A saved timeline has an empty hero.")
                                : new HeroTimeline(hero.HeroId, hero.Team, hero.NetWorth ?? [], hero.Experience ?? []))
                            .ToList())
                    : null,
                stored.Deaths?.Select(death => death is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty death.")
                        : new HeroDeath(death.HeroId, death.Seconds, death.X, death.Y, death.Killer, death.KillerHeroId,
                            death.Reincarnated))
                    .ToList());
        }
        catch (ArgumentException ex)
        {
            // The domain refused the saved values.
            throw new InvalidDataException($"A saved replay analysis breaks a rule: {ex.Message}", ex);
        }
    }

    private static StoredReplayAnalysis ToStored(ReplayAnalysis analysis) =>
        new(
            Version: CurrentVersion,
            MatchId: analysis.MatchId.Value,
            EndsAtSeconds: analysis.EndsAtSeconds,
            Players: analysis.Players
                .Select(player => new StoredLaningStats(
                    player.HeroId, player.Team, player.Lane, player.LastHits, player.Denies,
                    player.NetWorth, player.Experience, player.Deaths))
                .ToList(),
            Timeline: analysis.Timeline is { } timeline
                ? new StoredTimeline(
                    timeline.Seconds,
                    timeline.Heroes
                        .Select(hero => new StoredHeroTimeline(hero.HeroId, hero.Team, hero.NetWorth, hero.Experience))
                        .ToList())
                : null,
            Deaths: analysis.Deaths?
                .Select(death => new StoredHeroDeath(death.HeroId, death.Seconds, death.X, death.Y, death.Killer,
                    death.KillerHeroId, death.Reincarnated))
                .ToList());
}
