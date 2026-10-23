using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Each hero's position samples with what rides along them (vitals, stats, combat details, facing, orders).
internal static partial class ReplayParserMappings
{
    // Shared by replay analysis and opt-in coaching: one adapter for the Go position contract.
    internal static IReadOnlyList<HeroPositions> ReadPositions(ReplayParserMatchResponse response)
    {
        var heroOf = Required(response.Players, "players").Select(p => p ?? throw Empty("player"))
            .ToDictionary(p => p.Player ?? throw Missing("players.player"), p => p.HeroId ?? throw Missing("players.heroId"));
        return Required(response.Timelines?.Positions, "timelines.positions")
            .Select(entry => entry ?? throw Empty("positions entry"))
            .Select(entry => new HeroPositions(
                heroOf.TryGetValue(entry.Player ?? throw Missing("positions.player"), out var hero)
                    ? hero : throw new InvalidDataException($"Unknown player {entry.Player}."),
                WithCombatDetails(WithFacing(WithOrders(WithStats(WithVitals(Required(entry.Samples, "positions.samples").Select(ToPositionSample).ToList(),
                    entry.Vitals), entry.Stats), entry.Orders), entry.Facing), entry.CombatDetails)))
            .ToArray();
    }

    private static List<PositionSample> WithCombatDetails(List<PositionSample> samples, IReadOnlyList<JsonElement>? details)
    {
        if (details is null) return samples;
        if (details.Count != samples.Count) throw new InvalidDataException("positions.combatDetails needs one entry per sample.");
        return samples.Zip(details, (sample, tuple) =>
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 12
                || tuple[11].ValueKind != JsonValueKind.Null
                || tuple.EnumerateArray().Any(v => v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number)
                    || (v.ValueKind == JsonValueKind.Number && (!v.TryGetDouble(out var d) || !double.IsFinite(d)))))
                throw new InvalidDataException("Invalid combatDetails component tuple.");
            double? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : tuple[i].GetDouble();
            int? capability = null;
            if (tuple[8].ValueKind != JsonValueKind.Null)
            {
                if (!tuple[8].TryGetInt32(out var raw)) throw new InvalidDataException("Attack capability must be an integer.");
                capability = raw;
            }
            return sample with { CombatDetails = new(At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), capability, At(9), At(10)) };
        }).ToList();
    }

    // X29: integer yaw degrees (or null) parallel to the samples; older documents have none.
    private static List<PositionSample> WithFacing(List<PositionSample> samples, IReadOnlyList<JsonElement>? facing)
    {
        if (facing is null)
        {
            return samples;
        }

        if (facing.Count != samples.Count)
        {
            throw new InvalidDataException("positions.facing needs one entry per sample.");
        }

        return samples.Zip(facing, (sample, yaw) => yaw.ValueKind switch
        {
            JsonValueKind.Null => sample,
            JsonValueKind.Number when yaw.TryGetInt32(out var degrees) && degrees is >= 0 and < 360 => sample with { Facing = degrees },
            _ => throw new InvalidDataException("A facing is not an integer 0–359 or null."),
        }).ToList();
    }

    // Like WithVitals: no orders (older documents, or a replay without them: the field is left out) keeps the samples.
    private static List<PositionSample> WithOrders(List<PositionSample> samples, IReadOnlyList<JsonElement>? orders)
    {
        if (orders is null)
        {
            return samples;
        }

        if (orders.Count != samples.Count)
        {
            throw new InvalidDataException("positions.orders needs one entry per sample.");
        }

        return samples.Zip(orders, (sample, tuple) => sample with { Orders = ToOrders(tuple) }).ToList();
    }

    // [moveX | null, moveY | null, moves, attacks, casts].
    private static HeroOrders ToOrders(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5
            || tuple[0].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || !tuple[2].TryGetInt32(out var moves) || !tuple[3].TryGetInt32(out var attacks) || !tuple[4].TryGetInt32(out var casts)
            || moves < 0 || attacks < 0 || casts < 0)
        {
            throw new InvalidDataException("An orders entry is not [moveX, moveY, moves, attacks, casts].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new HeroOrders(At(0), At(1), moves, attacks, casts);
    }

    // Like WithVitals: older documents have no stats; newer ones have one per sample, in the same order.
    private static List<PositionSample> WithStats(List<PositionSample> samples, IReadOnlyList<JsonElement>? stats)
    {
        if (stats is null)
        {
            return samples;
        }

        if (stats.Count != samples.Count)
        {
            throw new InvalidDataException("positions.stats needs one entry per sample.");
        }

        return samples.Zip(stats, (sample, tuple) => sample with { Stats = ToStats(tuple) }).ToList();
    }

    // [armor, magicResist, moveSpeed, damageMin, damageMax, strength, agility, intellect], each a number or null.
    private static HeroCombatStats ToStats(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 8
            || tuple.EnumerateArray().Any(value => value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException(
                "A stats entry is not [armor, magicResist, moveSpeed, damageMin, damageMax, strength, agility, intellect].");
        }

        double? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetDouble();
        int? Whole(int index) => At(index) is { } value ? (int)Math.Round(value) : null;
        return new HeroCombatStats(At(0), At(1), Whole(2), Whole(3), Whole(4), At(5), At(6), At(7));
    }

    // Older documents have no vitals: the samples stay as they are. Newer ones have one per sample, in the same order.
    private static List<PositionSample> WithVitals(List<PositionSample> samples, IReadOnlyList<JsonElement>? vitals)
    {
        if (vitals is null)
        {
            return samples;
        }

        if (vitals.Count != samples.Count)
        {
            throw new InvalidDataException("positions.vitals needs one entry per sample.");
        }

        return samples.Zip(vitals, (sample, tuple) => sample with { Vitals = ToVitals(tuple) }).ToList();
    }

    // [health, maxHealth, mana, maxMana, visibleToEnemies (0/1), unitState, gold], each a whole number or null.
    private static HeroVitals ToVitals(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7
            || tuple.EnumerateArray().Any(value => value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException(
                "A vitals entry is not [health, maxHealth, mana, maxMana, visibleToEnemies, unitState, gold].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[index].GetDouble());
        if (At(0) is < 0 || At(1) is < 0 || At(2) is < 0 || At(3) is < 0 || At(6) is < 0 || At(4) is not (null or 0 or 1))
        {
            throw new InvalidDataException("Health, mana and gold can't be negative; visibleToEnemies is 0 or 1.");
        }

        return new HeroVitals(At(0), At(1), At(2), At(3), At(4) is { } seen ? seen == 1 : null, At(5), At(6));
    }

    // [t, x, y] -> a position sample.
    private static PositionSample ToPositionSample(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || tuple.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number))
        {
            throw new InvalidDataException("A position is not [t, x, y].");
        }

        return new PositionSample(
            (int)Math.Round(tuple[0].GetDouble()), (int)Math.Round(tuple[1].GetDouble()), (int)Math.Round(tuple[2].GetDouble()));
    }
}
