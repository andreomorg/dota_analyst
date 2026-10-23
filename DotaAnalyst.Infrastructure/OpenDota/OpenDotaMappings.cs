using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Translates OpenDota's format into the domain model.
public static class OpenDotaMappings
{
    // OpenDota benchmark names -> domain metrics. Names not listed here are ignored.
    private static readonly Dictionary<string, BenchmarkMetric> BenchmarkMetrics =
        new Dictionary<string, BenchmarkMetric>
        {
            ["gold_per_min"] = BenchmarkMetric.GoldPerMinute,
            ["xp_per_min"] = BenchmarkMetric.ExperiencePerMinute,
            ["kills_per_min"] = BenchmarkMetric.KillsPerMinute,
            ["deaths_per_min"] = BenchmarkMetric.DeathsPerMinute,
            ["assists_per_min"] = BenchmarkMetric.AssistsPerMinute,
            ["last_hits_per_min"] = BenchmarkMetric.LastHitsPerMinute,
            ["denies_per_min"] = BenchmarkMetric.DeniesPerMinute,
            ["hero_damage_per_min"] = BenchmarkMetric.HeroDamagePerMinute,
            ["hero_healing_per_min"] = BenchmarkMetric.HeroHealingPerMinute,
            ["tower_damage"] = BenchmarkMetric.TowerDamage,
        };

    public static PlayerMatch ToPlayerMatch(this OpenDotaRecentMatch match) =>
        new(
            MatchId: match.MatchId,
            HeroId: match.HeroId,
            Team: PlayerSlot.ToTeam(match.PlayerSlot),
            WinningTeam: ToWinningTeam(match.RadiantWin),
            Kda: new Kda(match.Kills, match.Deaths, match.Assists),
            Duration: TimeSpan.FromSeconds(match.Duration),
            StartTime: DateTimeOffset.FromUnixTimeSeconds(match.StartTime));

    public static MatchDetails ToMatchDetails(this OpenDotaMatch match)
    {
        if (!MatchId.TryCreate(match.MatchId, out var matchId))
        {
            throw new DataSourceUnavailableException($"OpenDota returned an invalid match ID ({match.MatchId}).");
        }

        return new MatchDetails(
            MatchId: matchId,
            Duration: TimeSpan.FromSeconds(match.Duration ?? 0),
            StartTime: DateTimeOffset.FromUnixTimeSeconds(match.StartTime ?? 0),
            WinningTeam: ToWinningTeam(match.RadiantWin),
            RadiantScore: match.RadiantScore ?? 0,
            DireScore: match.DireScore ?? 0,
            // OpenDota only fills "version" after parsing the replay.
            IsParsed: match.Version is not null,
            Players: (match.Players ?? []).Select(ToMatchPlayer).ToList(),
            ReplayUrl: ReplayUrls.Parse(match.ReplayUrl),
            SupplementalStatsVersion: match.Version is null ? 0 : DotaAnalyst.Application.Matches.SupplementalStatsSchema.CurrentVersion,
            PatchId: match.Patch is >= 0 ? match.Patch : null);
    }

    // Valve's placeholder account ID for players with an anonymous profile.
    private const long AnonymousAccountId = uint.MaxValue;

    public static MatchPlayer ToMatchPlayer(this OpenDotaMatchPlayer player) =>
        new(
            AccountId: player.AccountId is { } id and not AnonymousAccountId && AccountId.TryCreate(id, out var accountId)
                ? accountId
                : null,
            HeroId: player.HeroId ?? 0,
            Team: PlayerSlot.ToTeam(player.PlayerSlot),
            Kda: new Kda(player.Kills ?? 0, player.Deaths ?? 0, player.Assists ?? 0),
            LastHits: player.LastHits ?? 0,
            Denies: player.Denies ?? 0,
            GoldPerMinute: player.GoldPerMin ?? 0,
            ExperiencePerMinute: player.XpPerMin ?? 0,
            NetWorth: player.NetWorth ?? 0,
            HeroDamage: player.HeroDamage ?? 0,
            TowerDamage: player.TowerDamage ?? 0,
            HeroHealing: player.HeroHealing ?? 0,
            Level: player.Level ?? 0,
            // Item ID 0 means an empty slot.
            ItemIds: new[] { player.Item0, player.Item1, player.Item2, player.Item3, player.Item4, player.Item5 }
                .Where(itemId => itemId is > 0)
                .Select(itemId => itemId!.Value)
                .ToList(),
            NeutralItemId: player.ItemNeutral is > 0 ? player.ItemNeutral : null,
            Benchmarks: ToBenchmarks(player.Benchmarks),
            SupplementalStats: player.DamageInflictorReceived is null && player.AbilityUses is null && player.ItemUses is null
                && player.ActionsPerMin is null && player.Actions is null && player.GoldReasons is null && player.XpReasons is null
                ? null : new SupplementalPlayerStats(Clean(player.DamageInflictorReceived), Clean(player.AbilityUses), Clean(player.ItemUses),
                    player.ActionsPerMin is >= 0 ? player.ActionsPerMin : null, Clean(player.Actions),
                    player.GoldReasons?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    player.XpReasons?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));

    private static Dictionary<string, int>? Clean(IReadOnlyDictionary<string, int>? rows) =>
        rows?.Where(pair => pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // Joins the two item constants (ID -> key and key -> data) into ID -> item, with the item's details.
    // Entries without a key match or without a display name are skipped.
    public static IReadOnlyDictionary<int, Item> ToItems(
        IReadOnlyDictionary<int, string> itemIds, IReadOnlyDictionary<string, OpenDotaItem> items)
    {
        var result = new Dictionary<int, Item>();
        foreach (var (id, key) in itemIds)
        {
            if (items.TryGetValue(key, out var item) && !string.IsNullOrWhiteSpace(item?.Dname))
            {
                result[id] = new Item(id, item.Dname) { Details = ToItemDetails(key, item) };
            }
        }

        return result;
    }

    // Details OpenDota can't give in a usable shape (a blank key, a negative cost) are left out: the item keeps its
    // name, only the build loses it.
    private static ItemDetails? ToItemDetails(string key, OpenDotaItem item)
    {
        if (string.IsNullOrWhiteSpace(key) || item.Cost is < 0)
        {
            return null;
        }

        return new ItemDetails(
            key,
            item.Cost ?? 0,
            item.Components?.Where(component => !string.IsNullOrWhiteSpace(component)).Select(component => component!).ToList(),
            isConsumable: item.Qual?.Contains("consumable", StringComparison.OrdinalIgnoreCase) == true)
        {
            Abilities = OpenDotaKitMappings.Array(item.Abilities).Select(ability => new ItemAbility(
                OpenDotaKitMappings.Text(ability, "type") switch
                {
                    "active" => ItemAbilityKind.Active,
                    "passive" => ItemAbilityKind.Passive,
                    "toggle" => ItemAbilityKind.Toggle,
                    "use" => ItemAbilityKind.Use,
                    "upgrade" => ItemAbilityKind.Upgrade,
                    _ => ItemAbilityKind.Other,
                }, OpenDotaKitMappings.Text(ability, "title"), OpenDotaKitMappings.Text(ability, "description"))).ToArray(),
            Behavior = OpenDotaKitMappings.Strings(item.Behavior),
            Cooldown = OpenDotaKitMappings.Numbers(item.Cd).Cast<double?>().FirstOrDefault(),
            ManaCost = OpenDotaKitMappings.Numbers(item.Mc).Cast<double?>().FirstOrDefault(),
            Values = OpenDotaKitMappings.Values(item.Attrib),
            HasCharges = item.Charges.ValueKind == System.Text.Json.JsonValueKind.True
                || OpenDotaKitMappings.Numbers(item.Charges).Any(value => value > 0),
        };
    }

    private static Team? ToWinningTeam(bool? radiantWin) => radiantWin switch
    {
        true => Team.Radiant,
        false => Team.Dire,
        null => null,
    };

    private static List<Benchmark> ToBenchmarks(IReadOnlyDictionary<string, OpenDotaBenchmark>? benchmarks)
    {
        if (benchmarks is null)
        {
            return [];
        }

        var result = new List<Benchmark>();
        foreach (var (name, benchmark) in benchmarks)
        {
            if (BenchmarkMetrics.TryGetValue(name, out var metric) && benchmark?.Pct is { } pct)
            {
                // Clamped defensively: the domain rejects percentiles outside [0, 1].
                result.Add(new Benchmark(metric, benchmark.Raw ?? 0, Math.Clamp(pct, 0, 1)));
            }
        }

        return result;
    }
}
