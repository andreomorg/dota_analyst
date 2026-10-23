using System.Text.Json;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.ReplayParser;

internal static partial class ReplayParserMappings
{
    internal static IReadOnlyList<EconomyObservation>? ReadEconomyStates(ReplayParserMatchResponse response, ISet<string>? invalid = null) =>
        EconomyFamily("economyStates", invalid, () =>
        {
            var rows = EconomyRows(response.Timelines?.EconomyStates)?.Select(e => new EconomyObservation(
                EconomyHero(response, EconomyInt(e.GetProperty("player"))), EconomyInt(e.GetProperty("t")),
                EconomyNullableInt(e.GetProperty("netWorth")), EconomyNullableInt(e.GetProperty("reliable")),
                EconomyNullableInt(e.GetProperty("unreliable")), EconomyNullableTime(e.GetProperty("cooldownRemaining")),
                EconomyNullableInt(e.GetProperty("customCost")))).ToList();
            if (rows?.Any(e => e.Seconds < 0 || e.NetWorth < 0 || e.Reliable < 0 || e.Unreliable < 0 || e.CooldownRemaining < 0 || e.CustomCost < 0) == true
                || rows is not null && rows.DistinctBy(e => (e.HeroId, e.Seconds)).Count() != rows.Count) throw new InvalidDataException("Invalid economy state.");
            return rows?.OrderBy(e => e.Seconds).ToList();
        });

    internal static IReadOnlyList<GoldLossObservation>? ReadGoldLossChanges(ReplayParserMatchResponse response, ISet<string> invalid) =>
        EconomyFamily("goldLossChanges", invalid, () =>
        {
            var rows = EconomyRows(response.Timelines?.GoldLossChanges)?.Select(e => new GoldLossObservation(
                EconomyHero(response, EconomyInt(e.GetProperty("player"))), EconomyTime(e.GetProperty("t")),
                EconomyInt(e.GetProperty("before")), EconomyInt(e.GetProperty("after")),
                EconomyNullableInt(e.GetProperty("beforeUnreliable")), EconomyNullableInt(e.GetProperty("afterUnreliable")),
                EconomyTime(e.GetProperty("previousAt")))).ToList();
            if (rows?.Any(e => e.Seconds < 0 || e.Before < 0 || e.After <= e.Before || e.BeforeUnreliable < 0 || e.AfterUnreliable < 0 || e.PreviousAt > e.Seconds) == true)
                throw new InvalidDataException("Invalid loss counter observation.");
            if (rows is not null && rows.DistinctBy(e => (e.HeroId, e.Seconds, e.Before, e.After)).Count() != rows.Count)
                throw new InvalidDataException("Duplicate counter observation.");
            return rows?.OrderBy(e => e.Seconds).ToList();
        });

    internal static IReadOnlyList<FarmResourceSnapshot>? ReadFarmSnapshots(ReplayParserMatchResponse response, ISet<string> invalid) =>
        EconomyFamily("farmSnapshots", invalid, () =>
        {
            var rows = EconomyRows(response.Timelines?.FarmSnapshots)?.Select(e =>
            {
                var second = EconomyInt(e.GetProperty("t"));
                var units = EconomyRows(e.GetProperty("units"))?.Select(u =>
                {
                    if (u.ValueKind != JsonValueKind.Array || u.GetArrayLength() != 10) throw new InvalidDataException("Invalid farm resource tuple.");
                    var resource = new FarmResource(u[0].GetString()!, u[1].GetString()!, EconomyInt(u[2]), EconomyInt(u[3]), EconomyInt(u[4]),
                        EconomyInt(u[5]), EconomyNullableInt(u[6]), EconomyNullableInt(u[7]), EconomyVisibility(u[8]), EconomyVisibility(u[9]));
                    return resource;
                }).ToList() ?? throw new InvalidDataException("Missing resource units.");
                if (second < 0 || units.Any(u => string.IsNullOrEmpty(u.Id) || u.RawTeam is not (2 or 3 or 4) || u.Health <= 0
                    || u.BaseBountyMin < 0 || u.BaseBountyMax < 0 || u.BaseBountyMax < u.BaseBountyMin) || units.DistinctBy(u => u.Id).Count() != units.Count)
                    throw new InvalidDataException("Invalid farm resource snapshot.");
                return new FarmResourceSnapshot(second, units);
            }).ToList();
            if (rows is not null && rows.DistinctBy(e => e.Seconds).Count() != rows.Count) throw new InvalidDataException("Duplicate farm snapshot.");
            return rows?.OrderBy(e => e.Seconds).ToList();
        });

    private static T? EconomyFamily<T>(string name, ISet<string>? invalid, Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { invalid?.Add(name); return null; }
    }
    private static List<JsonElement>? EconomyRows(JsonElement? e) => e is null || e.Value.ValueKind == JsonValueKind.Null ? null
        : e.Value.ValueKind == JsonValueKind.Array ? e.Value.EnumerateArray().ToList() : throw new InvalidDataException("Expected economy array.");
    private static int EconomyHero(ReplayParserMatchResponse response, int player) => response.Players?.SingleOrDefault(p => p?.Player == player)?.HeroId
        ?? throw new InvalidDataException("Unknown economy player.");
    private static int EconomyInt(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : throw new InvalidDataException("Expected economy integer.");
    private static int? EconomyNullableInt(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : EconomyInt(e);
    private static double EconomyTime(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n) ? n : throw new InvalidDataException("Expected finite economy time.");
    private static double? EconomyNullableTime(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : EconomyTime(e);
    private static bool? EconomyVisibility(JsonElement e) => EconomyNullableInt(e) switch { null => null, 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid visibility.") };
}
