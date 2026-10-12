using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Body of POST /v1/laning.
internal sealed record ReplayParserLaningRequest(string ReplayUrl);

// The Go parser's answer to POST /v1/laning (contract in replay-parser/CLAUDE.md). Everything is nullable so a
// missing field becomes "unreadable answer" in the mapping below instead of a silent zero.
internal sealed record ReplayParserLaningResponse(int? LaningEndsAtSeconds, IReadOnlyList<ReplayParserPlayer?>? Players);

// steamId is text (64 bits); not used: heroes are matched by hero ID, which also works for anonymous profiles.
internal sealed record ReplayParserPlayer(
    int? HeroId,
    string? SteamId,
    bool? IsRadiant,
    string? Lane,
    int? LastHits,
    int? Denies,
    int? NetWorth,
    int? Xp,
    int? Deaths);

// The parser's format -> the domain model, like OpenDotaMappings does for OpenDota.
internal static class ReplayParserMappings
{
    /// <exception cref="InvalidDataException">The answer is incomplete or breaks a domain rule.</exception>
    public static ReplayAnalysis ToReplayAnalysis(this ReplayParserLaningResponse response, MatchId matchId)
    {
        var endsAt = response.LaningEndsAtSeconds ?? throw new InvalidDataException("laningEndsAtSeconds is missing.");
        var players = (response.Players ?? throw new InvalidDataException("players is missing."))
            .Select(ToLaningStats)
            .ToList();

        try
        {
            return new ReplayAnalysis(matchId, endsAt, players);
        }
        catch (ArgumentException ex)
        {
            // The domain refused the values (e.g. 700 s of laning phase, the same hero twice).
            throw new InvalidDataException($"The replay analysis breaks a rule: {ex.Message}", ex);
        }
    }

    private static LaningStats ToLaningStats(ReplayParserPlayer? player)
    {
        if (player is null)
        {
            throw new InvalidDataException("A player is empty.");
        }

        static int Required(int? value, string name) =>
            value ?? throw new InvalidDataException($"A player has no {name}.");

        try
        {
            return new LaningStats(
                heroId: Required(player.HeroId, "heroId"),
                team: player.IsRadiant switch
                {
                    true => Team.Radiant,
                    false => Team.Dire,
                    null => throw new InvalidDataException("A player has no isRadiant."),
                },
                lane: ToLane(player.Lane),
                lastHits: Required(player.LastHits, "lastHits"),
                denies: Required(player.Denies, "denies"),
                netWorth: Required(player.NetWorth, "netWorth"),
                experience: Required(player.Xp, "xp"),
                deaths: Required(player.Deaths, "deaths"));
        }
        catch (ArgumentException ex)
        {
            // Negative numbers or hero ID 0.
            throw new InvalidDataException($"A player breaks a rule: {ex.Message}", ex);
        }
    }

    // Spelled out instead of Enum.Parse, which would also accept numbers and other casings.
    private static Lane ToLane(string? lane) => lane switch
    {
        "top" => Lane.Top,
        "middle" => Lane.Middle,
        "bottom" => Lane.Bottom,
        "jungle" => Lane.Jungle,
        "roaming" => Lane.Roaming,
        _ => throw new InvalidDataException($"Unknown lane '{lane}'."),
    };
}
