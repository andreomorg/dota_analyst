using System.Text.Json;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Body of POST /v1/match (and of POST /v1/laning, which the API no longer calls).
internal sealed record ReplayParserRequest(string ReplayUrl);

// The part of the Go parser's POST /v1/match document the API reads today (contract: replay-parser/internal/matchdata).
// The whole document is kept as it came (IReplayDocumentArchive) for the analyses still to come. Everything is
// nullable so a missing field becomes "unreadable answer" in the mapping below instead of a silent zero.
internal sealed record ReplayParserMatchResponse(
    int? SchemaVersion,
    ReplayParserMatchInfo? Match,
    IReadOnlyList<ReplayParserMatchPlayer?>? Players,
    ReplayParserTimelines? Timelines,
    ReplayParserLaning? Laning,
    IReadOnlyList<ReplayParserKill?>? Kills,
    ReplayParserVision? Vision,
    ReplayParserCombat? Combat,
    ReplayParserItems? Items);

internal sealed record ReplayParserItems(IReadOnlyList<ReplayParserPlayerItems?>? Purchases);

// One player's purchases as [t, "item_name"] tuples (JSON arrays, read one by one in the mapping).
internal sealed record ReplayParserPlayerItems(int? Player, IReadOnlyList<JsonElement>? Items);

internal sealed record ReplayParserVision(IReadOnlyList<ReplayParserWard?>? Wards);

// Times are game-clock seconds with one decimal; player and destroyedBy are player indexes.
internal sealed record ReplayParserWard(
    string? Type, int? Player, string? Team, int? X, int? Y, double? PlacedAt, double? RemovedAt, bool? Destroyed,
    int? DestroyedBy);

// One hero death; killer is a player index (null when no player dealt the blow), killerUnit the combat log name.
internal sealed record ReplayParserKill(
    double? T, int? Victim, int? Killer, string? KillerUnit, int? X, int? Y, bool? Reincarnated,
    IReadOnlyList<int>? Assists);

internal sealed record ReplayParserCombat(IReadOnlyList<ReplayParserDamage?>? Damage);

// Aggregated damage from one source to one target with one inflictor; sourcePlayer/targetPlayer are player indexes
// (null for a non-hero unit, then named by its kind).
internal sealed record ReplayParserDamage(
    int? SourcePlayer, string? SourceKind, int? TargetPlayer, string? TargetKind, string? Inflictor, int? Amount);

internal sealed record ReplayParserMatchInfo(double? DurationSeconds);

internal sealed record ReplayParserMatchPlayer(int? Player, int? HeroId, string? Team);

internal sealed record ReplayParserTimelines(IReadOnlyList<ReplayParserPlayerMinutes?>? PerMinute);

// One value per point: every minute of the game clock from 0:00, then the end of the match.
internal sealed record ReplayParserPlayerMinutes(
    int? Player,
    IReadOnlyList<int>? NetWorth,
    IReadOnlyList<int>? Xp,
    IReadOnlyList<int>? LastHits,
    IReadOnlyList<int>? Denies,
    IReadOnlyList<int>? Deaths);

internal sealed record ReplayParserLaning(int? EndsAtSeconds, IReadOnlyList<ReplayParserPlayerLane?>? Lanes);

internal sealed record ReplayParserPlayerLane(int? Player, string? Lane);

// The parser's format -> the domain model, like OpenDotaMappings does for OpenDota.
internal static class ReplayParserMappings
{
    // The /v1/match schema this mapping understands; a newer one only adds fields (a breaking change is /v2/).
    public const int SupportedSchemaVersion = 1;

    /// <exception cref="InvalidDataException">The answer is incomplete or breaks a domain rule.</exception>
    public static ReplayAnalysis ToReplayAnalysis(this ReplayParserMatchResponse response, MatchId matchId)
    {
        try
        {
            return Map(response, matchId);
        }
        catch (ArgumentException ex)
        {
            // The domain refused the values (negative numbers, the same hero twice, 700 s of laning phase...), or the
            // same player index came twice.
            throw new InvalidDataException($"The replay analysis breaks a rule: {ex.Message}", ex);
        }
    }

    private static ReplayAnalysis Map(ReplayParserMatchResponse response, MatchId matchId)
    {
        if (response.SchemaVersion is not >= SupportedSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported schemaVersion {response.SchemaVersion}.");
        }

        var duration = response.Match?.DurationSeconds ?? throw new InvalidDataException("match.durationSeconds is missing.");
        var laning = response.Laning ?? throw new InvalidDataException("laning is missing.");
        var endsAt = laning.EndsAtSeconds ?? throw new InvalidDataException("laning.endsAtSeconds is missing.");
        var players = Required(response.Players, "players").Select(player => player ?? throw Empty("player")).ToList();
        var minutes = Required(response.Timelines?.PerMinute, "timelines.perMinute")
            .Select(entry => entry ?? throw Empty("perMinute entry"))
            .ToDictionary(entry => entry.Player ?? throw Missing("perMinute.player"));
        var lanes = Required(laning.Lanes, "laning.lanes")
            .Select(entry => entry ?? throw Empty("lane"))
            .ToDictionary(entry => entry.Player ?? throw Missing("lanes.player"), entry => ToLane(entry.Lane));

        var points = minutes.Values.Select(entry => Required(entry.NetWorth, "netWorth").Count).Distinct().ToList();
        if (points is not [var count and > 0])
        {
            throw new InvalidDataException("Every player needs the same number of points.");
        }

        // The numbers at the end of the laning phase are its point in the timeline: minute 10 when it lasted the
        // whole 10:00, otherwise the last point (the end of the match).
        var at = endsAt >= ReplayAnalysis.LaningPhaseEndsAtSeconds && count > 11 ? 10 : count - 1;

        var heroOf = players.ToDictionary(
            player => player.Player ?? throw Missing("players.player"),
            player => player.HeroId ?? throw Missing("players.heroId"));

        var laningStats = new List<LaningStats>();
        var heroes = new List<HeroTimeline>();
        foreach (var player in players)
        {
            var index = player.Player ?? throw Missing("players.player");
            var heroId = player.HeroId ?? throw Missing("players.heroId");
            var team = ToTeam(player.Team);
            var series = minutes.GetValueOrDefault(index) ?? throw Missing($"perMinute of player {index}");
            var lane = lanes.TryGetValue(index, out var found) ? found : throw Missing($"lane of player {index}");

            laningStats.Add(new LaningStats(
                heroId, team, lane,
                lastHits: Value(series.LastHits, "lastHits", at),
                denies: Value(series.Denies, "denies", at),
                netWorth: Value(series.NetWorth, "netWorth", at),
                experience: Value(series.Xp, "xp", at),
                deaths: Value(series.Deaths, "deaths", at)));
            heroes.Add(new HeroTimeline(heroId, team, Required(series.NetWorth, "netWorth"), Required(series.Xp, "xp")));
        }

        var deaths = Required(response.Kills, "kills")
            .Select(kill => ToDeath(kill ?? throw Empty("kill"), heroOf))
            .ToList();

        var wards = Required(response.Vision?.Wards, "vision.wards")
            .Select(ward => ToWard(ward ?? throw Empty("ward"), heroOf))
            .ToList();

        int? HeroOfPlayer(int? index) => index is { } i
            ? heroOf.TryGetValue(i, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {i}.")
            : null;

        var damage = Required(response.Combat?.Damage, "combat.damage")
            .Select(row => row ?? throw Empty("damage row"))
            // Only rows with a hero on a side (the parser groups the rest by unit kind on both sides).
            .Where(row => row.SourcePlayer is not null || row.TargetPlayer is not null)
            .Select(row => new DamageRow(
                sourceHeroId: HeroOfPlayer(row.SourcePlayer),
                sourceKind: row.SourceKind ?? throw Missing("damage.sourceKind"),
                targetHeroId: HeroOfPlayer(row.TargetPlayer),
                targetKind: row.TargetKind ?? throw Missing("damage.targetKind"),
                inflictor: row.Inflictor,
                amount: row.Amount ?? throw Missing("damage.amount")))
            .ToList();

        var purchases = Required(response.Items?.Purchases, "items.purchases")
            .Select(entry => entry ?? throw Empty("purchases entry"))
            .SelectMany(entry =>
            {
                var heroId = HeroOfPlayer(entry.Player ?? throw Missing("purchases.player"))!.Value;
                return Required(entry.Items, "purchases.items").Select(tuple => ToPurchase(heroId, tuple));
            })
            .ToList();

        return new ReplayAnalysis(
            matchId, endsAt, laningStats, new MatchTimeline(Seconds(count, duration), heroes), deaths, wards, damage,
            purchases);
    }

    // [t, "item_name"] -> a purchase keyed like the item catalog (without "item_").
    private static ItemPurchase ToPurchase(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 2
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A purchase is not [t, \"item_name\"].");
        }

        var name = tuple[1].GetString()!;
        const string Prefix = "item_";
        return new ItemPurchase(
            heroId,
            (int)Math.Round(tuple[0].GetDouble()),
            name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : name);
    }

    private static WardPlacement ToWard(ReplayParserWard ward, Dictionary<int, int> heroOf)
    {
        int? HeroOf(int? index) => index is { } i
            ? heroOf.TryGetValue(i, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {i}.")
            : null;

        var placedAt = (int)Math.Round(ward.PlacedAt ?? throw Missing("ward.placedAt"));
        return new WardPlacement(
            kind: ward.Type switch
            {
                "observer" => WardKind.Observer,
                "sentry" => WardKind.Sentry,
                _ => throw new InvalidDataException($"Unknown ward type '{ward.Type}'."),
            },
            team: ToTeam(ward.Team),
            placedByHeroId: HeroOf(ward.Player),
            x: ward.X ?? throw Missing("ward.x"),
            y: ward.Y ?? throw Missing("ward.y"),
            placedAt: placedAt,
            // Rounded separately, so a ward gone a fraction of a second after it was placed can't end before it.
            removedAt: ward.RemovedAt is { } removedAt ? Math.Max(placedAt, (int)Math.Round(removedAt)) : null,
            destroyed: ward.Destroyed ?? false,
            destroyedByHeroId: HeroOf(ward.DestroyedBy));
    }

    private static HeroDeath ToDeath(ReplayParserKill kill, Dictionary<int, int> heroOf)
    {
        int HeroOf(int index) =>
            heroOf.TryGetValue(index, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {index}.");

        var killer = kill.Killer is not null ? KillerKind.Hero : ToKillerKind(kill.KillerUnit);
        return new HeroDeath(
            heroId: HeroOf(kill.Victim ?? throw Missing("kill.victim")),
            seconds: (int)Math.Round(kill.T ?? throw Missing("kill.t")),
            x: kill.X ?? throw Missing("kill.x"),
            y: kill.Y ?? throw Missing("kill.y"),
            killer: killer,
            killerHeroId: kill.Killer is { } index ? HeroOf(index) : null,
            reincarnated: kill.Reincarnated ?? false,
            assistHeroIds: (kill.Assists ?? []).Select(HeroOf).ToList());
    }

    // Combat log names of what dealt the blow when it wasn't a player (contract: Kill.KillerUnit). A hero name
    // without a player (e.g. a kill the replay does not attribute) is still a hero.
    private static KillerKind ToKillerKind(string? unit) => unit switch
    {
        null or "" => KillerKind.Other,
        _ when unit.StartsWith("npc_dota_hero_", StringComparison.Ordinal) => KillerKind.Hero,
        _ when unit.Contains("tower", StringComparison.Ordinal) || unit.Contains("fort", StringComparison.Ordinal)
            || unit.Contains("rax", StringComparison.Ordinal) || unit.Contains("fountain", StringComparison.Ordinal) => KillerKind.Tower,
        _ when unit.StartsWith("npc_dota_creep", StringComparison.Ordinal) || unit.Contains("siege", StringComparison.Ordinal) => KillerKind.Creeps,
        _ when unit.StartsWith("npc_dota_neutral", StringComparison.Ordinal) => KillerKind.Neutrals,
        _ when unit.StartsWith("npc_dota_roshan", StringComparison.Ordinal) => KillerKind.Roshan,
        _ => KillerKind.Other,
    };

    // Point i is minute i; the last one is the end of the match (kept after the previous point even when the match
    // ended exactly on a minute).
    private static List<int> Seconds(int count, double durationSeconds)
    {
        var seconds = Enumerable.Range(0, count).Select(minute => minute * 60).ToList();
        if (count > 1)
        {
            seconds[^1] = Math.Max((int)Math.Round(durationSeconds), seconds[^2] + 1);
        }

        return seconds;
    }

    private static int Value(IReadOnlyList<int>? values, string name, int at) =>
        Required(values, name) is { } list && at < list.Count ? list[at] : throw Missing(name);

    private static T Required<T>(T? value, string name) where T : class => value ?? throw Missing(name);

    private static InvalidDataException Missing(string name) => new($"{name} is missing.");

    private static InvalidDataException Empty(string name) => new($"A {name} is empty.");

    private static Team ToTeam(string? team) => team switch
    {
        "radiant" => Team.Radiant,
        "dire" => Team.Dire,
        _ => throw new InvalidDataException($"Unknown team '{team}'."),
    };

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
