using System.Text.Json;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

internal sealed record ReplayParserOutpost(double? T, string? Team, int? Player);
internal sealed record ReplayParserTormentorState(string? Side, int? X, int? Y, IReadOnlyList<JsonElement>? Samples);
internal sealed record ReplayParserCourierDeath(double? T, int? Victim, int? Killer);
// players[].final (the numbers read here; the rest of the block is not needed by the facts).
internal sealed record ReplayParserPlayerFinal(int? Level, int? Kills, int? Deaths, int? Assists, int? LastHits, int? Denies,
    int? MissedLastHits, int? NetWorth, int? GoldEarned, int? Xp, int? HeroDamage, int? TowerDamage, int? Healing,
    double? StunSeconds, double? TeamFightParticipation, int? CampsStacked, int? ObserversPlaced, int? SentriesPlaced,
    int? WardsDestroyed, int? GoldLostToDeath, int? Gold,
    // The map's objectives (HeroMetrics.Objectives).
    int? BountyRunes = null, int? PowerRunes = null, int? WaterRunes = null, int? WisdomShrinesTaken = null,
    int? LotusesTaken = null, int? WatchersTaken = null, int? OutpostsCaptured = null, int? TormentorKills = null);
// items.final: one player's items at the end.
internal sealed record ReplayParserFinalItems(int? Player, IReadOnlyList<string?>? Inventory, IReadOnlyList<string?>? Backpack,
    string? Teleport, string? Neutral, string? Enhancement);
// combat.healing rows: the same shape as combat.damage (damageType is always null for healing).
internal sealed record ReplayParserHealing(int? SourcePlayer, string? SourceKind, int? TargetPlayer, string? TargetKind,
    string? Inflictor, int? Amount, int? Hits);

internal static partial class ReplayParserMappings
{
    internal static ReplayAdditionalFacts ReadFacts(ReplayParserMatchResponse response)
    {
        var invalid = new HashSet<string>();
        var players = Required(response.Players, "players").Where(p => p is not null).ToDictionary(p => p!.Player!.Value, p => p!.HeroId!.Value);
        int Hero(int player) => players.TryGetValue(player, out var hero) ? hero : throw new InvalidDataException("Unknown factual player.");
        int? OptionalHero(int? player) => player is { } p ? Hero(p) : null;
        T? Family<T>(string name, Func<T?> read) where T : class
        {
            try { return read(); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
            { invalid.Add(name); return null; }
        }
        var outposts = Family<IReadOnlyList<OutpostObservation>>("outposts", () =>
        {
            var detailed = FactElements(response.Objectives?.OutpostCaptures)?.Select(tuple =>
            {
                FactTuple(tuple, 3);
                var rawTeam = tuple[2].ValueKind == JsonValueKind.Null ? null : tuple[2].GetString();
                return new OutpostObservation(FactTime(tuple[0]), FactInt(tuple[1]), FactTeam(rawTeam), null, "entityMessage", rawTeam);
            }).ToList();
            var legacy = FactObjects<ReplayParserOutpost>(response.Objectives?.Outposts)?.Select(entry => entry is null ? throw Empty("outpost") : new OutpostObservation(
                FactTime(entry.T), null, FactTeam(entry.Team), OptionalHero(entry.Player), "legacyMessage", entry.Team)).ToList();
            if (detailed is null) return legacy;
            if (legacy is null) return detailed;
            return detailed.Select(entry =>
            {
                var matches = legacy.Where(old => entry.Team is not null && old.Seconds == entry.Seconds && old.Team == entry.Team).ToArray();
                return matches.Length == 1 && detailed.Count(other => other.Seconds == entry.Seconds && other.Team == entry.Team) == 1
                    ? entry with { HeroId = matches[0].HeroId } : entry;
            }).Concat(legacy.Where(old => !detailed.Any(entry => entry.Team is not null && entry.Seconds == old.Seconds && entry.Team == old.Team)))
                .OrderBy(entry => entry.Seconds).ToList();
        });
        var tormentors = Family<IReadOnlyList<TormentorSite>>("tormentors", () => FactObjects<ReplayParserTormentorState>(response.Objectives?.TormentorStates)?.Select(site => site is null ? throw Empty("tormentor") : new TormentorSite(
            FactTeam(site.Side), site.X ?? throw Missing("tormentor.x"), site.Y ?? throw Missing("tormentor.y"), Required(site.Samples, "tormentor.samples").Select(tuple =>
            {
                FactTuple(tuple, 2);
                return new AliveObservation(FactInt(tuple[0]), tuple[1].ValueKind == JsonValueKind.Null ? null : FactInt(tuple[1]) switch
                { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid alive flag.") });
            }).OrderBy(sample => sample.Seconds).ToList(), site.Side)).ToList());
        var couriers = Family<IReadOnlyList<CourierDeath>>("courierDeaths", () => FactObjects<ReplayParserCourierDeath>(response.Objectives?.Couriers)?.Select(entry =>
            entry is null ? throw Empty("courier death") : new CourierDeath(FactTime(entry.T), Hero(entry.Victim ?? throw Missing("courier.victim")), OptionalHero(entry.Killer))).ToList());
        var healing = Family<IReadOnlyList<HealingRow>>("healing", () => FactObjects<ReplayParserHealing>(response.Combat?.Healing)?.Select(row =>
            row is null ? throw Empty("healing row")
            : row.Amount is not >= 0 || row.Hits is < 0 ? throw new InvalidDataException("Invalid healing amount.")
            : new HealingRow(OptionalHero(row.SourcePlayer), row.SourceKind is { Length: > 0 } source ? source : throw Missing("healing.sourceKind"),
                OptionalHero(row.TargetPlayer), row.TargetKind is { Length: > 0 } target ? target : throw Missing("healing.targetKind"),
                row.Inflictor, row.Amount.Value, row.Hits)).ToList());
        // Final stats: every known player must have the block; one without it makes the family unknown, not partial.
        var finalStats = Family<IReadOnlyList<FinalHeroStats>>("finalStats", () =>
        {
            var entries = Required(response.Players, "players").Where(p => p is not null).ToList();
            if (entries.All(p => p!.Final is null || p.Final.Value.ValueKind == JsonValueKind.Null)) return null;
            return entries.Select(p =>
            {
                var f = p!.Final is { ValueKind: JsonValueKind.Object } block
                    ? block.Deserialize<ReplayParserPlayerFinal>(DotaAnalyst.Infrastructure.Coaching.CoachingJson.Options)!
                    : throw Missing("players.final");
                int N(int? value, string name) => value is >= 0 ? value.Value : throw Missing("players.final." + name);
                double D(double? value, string name) => value is { } number && double.IsFinite(number) && number >= 0 ? number : throw Missing("players.final." + name);
                return new FinalHeroStats(Hero(p.Player ?? throw Missing("players.player")),
                    N(f.Level, "level"), N(f.Kills, "kills"), N(f.Deaths, "deaths"), N(f.Assists, "assists"), N(f.LastHits, "lastHits"),
                    N(f.Denies, "denies"), N(f.MissedLastHits, "missedLastHits"), N(f.NetWorth, "netWorth"), N(f.GoldEarned, "goldEarned"),
                    N(f.Xp, "xp"), N(f.HeroDamage, "heroDamage"), N(f.TowerDamage, "towerDamage"), N(f.Healing, "healing"),
                    D(f.StunSeconds, "stunSeconds"), D(f.TeamFightParticipation, "teamFightParticipation"), N(f.CampsStacked, "campsStacked"),
                    N(f.ObserversPlaced, "observersPlaced"), N(f.SentriesPlaced, "sentriesPlaced"), N(f.WardsDestroyed, "wardsDestroyed"),
                    N(f.GoldLostToDeath, "goldLostToDeath"), N(f.Gold, "gold"));
            }).ToList();
        });
        var finalItems = Family<IReadOnlyList<FinalInventory>>("finalItems", () => FactObjects<ReplayParserFinalItems>(response.Items?.Final)?.Select(entry =>
        {
            if (entry is null) throw Empty("final items");
            var inventory = entry.Inventory is { Count: 6 } slots ? slots : throw new InvalidDataException("items.final.inventory needs 6 slots.");
            var backpack = entry.Backpack is { Count: 3 } pack ? pack : throw new InvalidDataException("items.final.backpack needs 3 slots.");
            return new FinalInventory(Hero(entry.Player ?? throw Missing("items.final.player")), inventory, backpack, entry.Teleport,
                entry.Neutral, entry.Enhancement);
        }).ToList());
        // [t, player, phrase ID]
        var chatWheel = Family<IReadOnlyList<ChatWheelUse>>("chatWheel", () => FactElements(response.Vision?.ChatWheel)?.Select(tuple =>
        {
            FactTuple(tuple, 3);
            return new ChatWheelUse(Hero(FactInt(tuple[1])), FactTime(tuple[0]),
                tuple[2].ValueKind == JsonValueKind.Number && tuple[2].TryGetUInt32(out var phrase) ? phrase : throw new InvalidDataException("Invalid phrase ID."));
        }).OrderBy(use => use.Seconds).ToList());
        // MP3: [t, x, y, present]
        var tempTrees = Family<IReadOnlyList<TempTreeChange>>("tempTrees", () => FactElements(response.Timelines?.TempTrees)?.Select(tuple =>
        {
            FactTuple(tuple, 4);
            return new TempTreeChange(FactTime(tuple[0]), FactInt(tuple[1]), FactInt(tuple[2]), FactBool(tuple[3]));
        }).OrderBy(change => change.Seconds).ToList());
        // MP3: [{ kind, samples: [[t, x, y, alive]] }]
        var bossTracks = Family<IReadOnlyList<BossTrack>>("bossTracks", () => FactElements(response.Objectives?.BossTracks)?.Select(track =>
        {
            if (track.ValueKind != JsonValueKind.Object || !track.TryGetProperty("kind", out var kind) || kind.GetString() is not { Length: > 0 } name
                || !track.TryGetProperty("samples", out var samples))
            {
                throw new InvalidDataException("A boss track needs a kind and samples.");
            }

            return new BossTrack(name, (FactElements(samples) ?? throw Missing("bossTracks.samples")).Select(tuple =>
            {
                FactTuple(tuple, 4);
                return new BossSample(FactInt(tuple[0]), FactInt(tuple[1]), FactInt(tuple[2]), FactBool(tuple[3]));
            }).OrderBy(sample => sample.Seconds).ToList());
        }).ToList());
        // MP3: [t, player, x | null, y | null]
        var lanterns = Family<IReadOnlyList<LanternUse>>("lanterns", () => FactElements(response.Vision?.Lanterns)?.Select(tuple =>
        {
            FactTuple(tuple, 4);
            return new LanternUse(Hero(FactInt(tuple[1])), FactTime(tuple[0]), FactOptionalInt(tuple[2]), FactOptionalInt(tuple[3]));
        }).OrderBy(use => use.Seconds).ToList());
        // MP3: [t, player, amount, reason | null, x | null, y | null]
        var goldEvents = Family<IReadOnlyList<GoldEvent>>("goldEvents", () => FactElements(response.Combat?.GoldEvents)?.Select(tuple =>
        {
            FactTuple(tuple, 6);
            uint? reason = tuple[3].ValueKind == JsonValueKind.Null ? null
                : tuple[3].TryGetUInt32(out var raw) ? raw : throw new InvalidDataException("Invalid gold reason.");
            return new GoldEvent(Hero(FactInt(tuple[1])), FactTime(tuple[0]),
                tuple[2].ValueKind == JsonValueKind.Number && tuple[2].TryGetInt64(out var amount) ? amount : throw new InvalidDataException("Invalid gold amount."),
                reason, FactOptionalInt(tuple[4]), FactOptionalInt(tuple[5]));
        }).OrderBy(gold => gold.Seconds).ToList());
        // G05: [second, player, reliable | null, unreliable | null], including dead heroes.
        var goldBalances = Family<IReadOnlyList<GoldBalance>>("goldBalances", () =>
        {
            var rows = FactElements(response.Timelines?.GoldBalances)?.Select(tuple =>
            {
                FactTuple(tuple, 4);
                var seconds = FactInt(tuple[0]);
                var reliable = FactOptionalInt(tuple[2]);
                var unreliable = FactOptionalInt(tuple[3]);
                if (seconds < 0 || reliable < 0 || unreliable < 0) throw new InvalidDataException("Invalid gold balance.");
                return new GoldBalance(Hero(FactInt(tuple[1])), seconds, reliable, unreliable);
            }).OrderBy(row => row.Seconds).ToList();
            if (rows is not null && rows.DistinctBy(row => (row.HeroId, row.Seconds)).Count() != rows.Count)
                throw new InvalidDataException("Duplicate gold balance.");
            return rows;
        });
        // DATA-D17: [{ player, abilities: [[from, to | null, name, entity, serial, level, hidden]], cooldowns: [[t, name, entity, serial, readyAt]] }]
        var stolenEntries = Family<List<JsonElement>>("stolenAbilities", () => FactElements(response.Abilities?.Stolen));
        var stolenAbilities = stolenEntries is null ? null : Family<IReadOnlyList<StolenAbility>>("stolenAbilities", () => stolenEntries
            .SelectMany(entry => StolenList(entry, "abilities").Select(tuple =>
            {
                FactTuple(tuple, 7);
                return new StolenAbility(Hero(StolenPlayer(entry)), FactTime(tuple[0]),
                    tuple[1].ValueKind == JsonValueKind.Null ? null : FactTime(tuple[1]),
                    tuple[2].GetString() is { Length: > 0 } name ? name : throw Missing("stolen.name"),
                    FactInt(tuple[3]), FactInt(tuple[4]), FactInt(tuple[5]), FactBool(tuple[6]));
            }))
            .OrderBy(ability => ability.From).ToList());
        var stolenCooldowns = stolenEntries is null ? null : Family<IReadOnlyList<StolenCooldown>>("stolenCooldowns", () => stolenEntries
            .SelectMany(entry => StolenList(entry, "cooldowns").Select(tuple =>
            {
                FactTuple(tuple, 5);
                return new StolenCooldown(Hero(StolenPlayer(entry)), FactTime(tuple[0]),
                    tuple[1].GetString() is { Length: > 0 } name ? name : throw Missing("stolen.cooldown.name"),
                    FactInt(tuple[2]), FactInt(tuple[3]), FactTime(tuple[4]));
            }))
            .OrderBy(cooldown => cooldown.Seconds).ToList());
        return new ReplayAdditionalFacts
        {
            Outposts = outposts, Tormentors = tormentors, CourierDeaths = couriers, Healing = healing, FinalStats = finalStats,
            FinalItems = finalItems, ChatWheel = chatWheel, TempTrees = tempTrees, BossTracks = bossTracks, Lanterns = lanterns,
            GoldEvents = goldEvents, GoldBalances = goldBalances,
            EconomyStates = ReadEconomyStates(response, invalid), GoldLossChanges = ReadGoldLossChanges(response, invalid),
            FarmSnapshots = ReadFarmSnapshots(response, invalid),
            StolenAbilities = stolenAbilities, StolenCooldowns = stolenCooldowns, InvalidFamilies = invalid,
        };

        static int StolenPlayer(JsonElement entry) =>
            entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("player", out var player) ? FactInt(player)
                : throw new InvalidDataException("A stolen entry needs a player.");

        static List<JsonElement> StolenList(JsonElement entry, string name) =>
            entry.TryGetProperty(name, out var list) ? FactElements(list) ?? [] : throw new InvalidDataException("A stolen entry needs " + name + ".");
    }

    private static void FactTuple(JsonElement tuple, int length)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != length) throw new InvalidDataException("Invalid factual tuple.");
    }
    private static Team? FactTeam(string? raw) => raw switch { "radiant" => Team.Radiant, "dire" => Team.Dire, _ => null };
    private static List<T?>? FactObjects<T>(JsonElement? value) where T : class
    {
        return FactElements(value)?.Select(entry => entry.Deserialize<T>(DotaAnalyst.Infrastructure.Coaching.CoachingJson.Options)).ToList();
    }
    private static List<JsonElement>? FactElements(JsonElement? value) => value is null || value.Value.ValueKind == JsonValueKind.Null
        ? null : value.Value.ValueKind == JsonValueKind.Array ? value.Value.EnumerateArray().ToList()
        : throw new InvalidDataException("Factual family is not an array.");
    private static int FactInt(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
        ? number : throw new InvalidDataException("Invalid factual integer.");
    private static int? FactOptionalInt(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : FactInt(value);
    private static bool FactBool(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw new InvalidDataException("Invalid factual flag.");
    private static double FactTime(JsonElement value) => FactTime(value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null);
    private static double FactTime(double? value) => value is { } number && double.IsFinite(number)
        ? number : throw new InvalidDataException("Invalid factual time.");
}
