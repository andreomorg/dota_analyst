using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// The activity's compact tuples ([t, player, ...]) -> domain records, each checked for its shape.
internal static partial class ReplayParserMappings
{
    // [t, [[x, y, day, night]...radiant], [[...]...dire]] -> each team's moving vision at a second.
    private static VisionUnitsSample ToVisionUnits(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3 || !tuple[0].TryGetInt32(out var seconds)
            || tuple[1].ValueKind != JsonValueKind.Array || tuple[2].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A vision units sample is not [t, radiant, dire].");
        }

        static List<VisionCircle> Circles(JsonElement list) => list.EnumerateArray().Select(circle =>
            circle.ValueKind == JsonValueKind.Array && circle.GetArrayLength() == 4 && circle[0].TryGetInt32(out var x)
            && circle[1].TryGetInt32(out var y) && circle[2].TryGetInt32(out var day) && circle[3].TryGetInt32(out var night)
                ? new VisionCircle(x, y, day, night)
                : throw new InvalidDataException("A vision circle is not [x, y, day, night].")).ToList();

        return new VisionUnitsSample(seconds, Circles(tuple[1]), Circles(tuple[2]));
    }

    // [t, attacker, target, amount] (player indexes) -> a hero hit.
    private static HeroHit ToHeroHit(JsonElement tuple, Func<int?, int> heroOf) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 4 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].TryGetInt32(out var attacker) && tuple[2].TryGetInt32(out var target)
        && tuple[3].TryGetInt32(out var amount) && amount >= 0
            ? new HeroHit(seconds, heroOf(attacker), heroOf(target), amount)
            : throw new InvalidDataException("A hero hit is not [t, attacker, target, amount].");

    // [t, player, "lane" | "deny" | "neutral", x | null, y | null] -> a creep last hit.
    private static CreepKill ToCreepKill(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || !tuple[1].TryGetInt32(out var player) || tuple[2].ValueKind != JsonValueKind.String
            || tuple[3].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[4].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            throw new InvalidDataException("A creep kill is not [t, player, kind, x, y].");
        }

        var kind = tuple[2].GetString() switch
        {
            "lane" => CreepKillKind.Lane,
            "deny" => CreepKillKind.Deny,
            "neutral" => CreepKillKind.Neutral,
            var other => throw new InvalidDataException($"Unknown creep kill kind '{other}'."),
        };
        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new CreepKill(heroOf(player), tuple[0].GetDouble(), kind, At(3), At(4));
    }

    // A lane's fronts with their counts: sizes[i] is [t, radiantInLane, direInLane, radiantAtFront, direAtFront] for
    // fronts[i]. A different length or t means the lists are not parallel: unreadable, never matched by guessing.
    // strength[i] (W1) is [t, radiantHealth, direHealth, radiantHealthAtFront, direHealthAtFront, radiantSiege, direSiege,
    // radiantCenter | null, direCenter | null], parallel the same way.
    // composition[i] (W5) is [t, then for Radiant and Dire: melee, ranged, siege, flagbearer, super, mega, lowAtFront].
    private static IEnumerable<WaveFront> ToWaveFronts(
        Lane lane, IReadOnlyList<JsonElement> fronts, IReadOnlyList<JsonElement>? sizes, IReadOnlyList<JsonElement>? strength = null,
        IReadOnlyList<JsonElement>? composition = null)
    {
        if ((sizes is not null && sizes.Count != fronts.Count) || (strength is not null && strength.Count != fronts.Count)
            || (composition is not null && composition.Count != fronts.Count))
        {
            throw new InvalidDataException("Wave sizes, strength or composition are not parallel to the fronts.");
        }

        return fronts.Select((tuple, index) =>
        {
            var front = ToWaveFront(lane, tuple);
            if (sizes is not null)
            {
                var size = sizes[index];
                if (size.ValueKind != JsonValueKind.Array || size.GetArrayLength() != 5
                    || Enumerable.Range(0, 5).Any(i => !size[i].TryGetInt32(out var value) || value < 0)
                    || size[0].GetInt32() != front.Seconds)
                {
                    throw new InvalidDataException("A wave size is not [t, radiantInLane, direInLane, radiantAtFront, direAtFront] of its front.");
                }

                front = front with { Counts = new WaveCounts(size[1].GetInt32(), size[2].GetInt32(), size[3].GetInt32(), size[4].GetInt32()) };
            }

            if (strength is not null)
            {
                var s = strength[index];
                if (s.ValueKind != JsonValueKind.Array || s.GetArrayLength() != 9
                    || Enumerable.Range(0, 7).Any(i => !s[i].TryGetInt32(out var value) || value < 0)
                    || s[0].GetInt32() != front.Seconds || s[5].GetInt32() > 1 || s[6].GetInt32() > 1
                    || s[7].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
                    || s[8].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
                {
                    throw new InvalidDataException("A wave strength is not [t, health ×4, siege ×2, center ×2] of its front.");
                }

                int? Center(int i) => s[i].ValueKind == JsonValueKind.Null ? null : s[i].GetInt32();
                front = front with
                {
                    Strength = new WaveStrength(s[1].GetInt32(), s[2].GetInt32(), s[3].GetInt32(), s[4].GetInt32(),
                        s[5].GetInt32() == 1, s[6].GetInt32() == 1, Center(7), Center(8)),
                };
            }

            if (composition is not null)
            {
                var c = composition[index];
                if (c.ValueKind != JsonValueKind.Array || c.GetArrayLength() != 15
                    || Enumerable.Range(0, 15).Any(i => !c[i].TryGetInt32(out var value) || value < 0) || c[0].GetInt32() != front.Seconds)
                {
                    throw new InvalidDataException("A wave composition is not [t, 7 Radiant counts, 7 Dire counts] of its front.");
                }

                TeamWave Team(int from) => new(c[from].GetInt32(), c[from + 1].GetInt32(), c[from + 2].GetInt32(), c[from + 3].GetInt32(),
                    c[from + 4].GetInt32(), c[from + 5].GetInt32(), c[from + 6].GetInt32());
                front = front with { Composition = new WaveComposition(Team(1), Team(8)) };
            }

            return front;
        });
    }

    // [bucketStart, player, resource (0 gold, 1 XP), rawReason | null, amount] -> gold/XP by source in [t, t + 2).
    private static EconomyBucket ToEconomyBucket(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var player) || !tuple[2].TryGetInt32(out var resource) || resource is not (0 or 1)
            || tuple[3].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null) || !tuple[4].TryGetInt32(out var amount))
        {
            throw new InvalidDataException("An economy bucket is not [t, player, resource, reason, amount].");
        }

        var kind = resource == 0 ? EconomyResource.Gold : EconomyResource.Experience;
        int? reason = tuple[3].ValueKind == JsonValueKind.Null ? null : tuple[3].GetInt32();
        return new EconomyBucket(heroOf(player), t, kind, EconomyBucket.SourceOf(kind, reason), amount);
    }

    // [t, player, x, y, type] -> a map ping.
    private static MapPing ToMapPing(JsonElement tuple, Func<int, (int HeroId, Team Team)> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || !tuple[1].TryGetInt32(out var player) || tuple[2].ValueKind != JsonValueKind.Number
            || tuple[3].ValueKind != JsonValueKind.Number || !tuple[4].TryGetInt32(out var type))
        {
            throw new InvalidDataException("A ping is not [t, player, x, y, type].");
        }

        var (heroId, team) = heroOf(player);
        return new MapPing(heroId, team, tuple[0].GetDouble(), (int)Math.Round(tuple[2].GetDouble()), (int)Math.Round(tuple[3].GetDouble()), type);
    }

    // A tuple of a fixed length whose items are all non-negative integers (after the leading t, which may be negative).
    private static int[] Integers(JsonElement tuple, int length, string what)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != length
            || Enumerable.Range(0, length).Any(i => !tuple[i].TryGetInt32(out var value) || (i > 0 && value < 0)))
        {
            throw new InvalidDataException($"A {what} entry is not {length} integers.");
        }

        return Enumerable.Range(0, length).Select(i => tuple[i].GetInt32()).ToArray();
    }

    private static TypedDamageBucket ToTypedDamage(int heroId, JsonElement tuple)
    {
        var v = Integers(tuple, 7, "buckets.byType");
        return new TypedDamageBucket(heroId, v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
    }

    private static HealingBucket ToHealing(int heroId, JsonElement tuple)
    {
        var v = Integers(tuple, 3, "buckets.healing");
        return new HealingBucket(heroId, v[0], v[1], v[2]);
    }

    private static CourierTrack ToCourier(ReplayParserCourier courier) => new(
        courier.Id ?? throw Missing("couriers.id"),
        ToTeam(courier.Team),
        Required(courier.Samples, "couriers.samples").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() is 4 or 5
                && Enumerable.Range(0, 4).All(i => tuple[i].ValueKind == JsonValueKind.Number) && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var alive) && alive is 0 or 1 && tuple[2].TryGetInt32(out var x) && tuple[3].TryGetInt32(out var y)
                ? (t, alive == 1, x, y)
                : throw new InvalidDataException("A courier reading is not [t, alive, x, y, flying].")).ToList())
    {
        Flight = Required(courier.Samples, "couriers.samples").Select(tuple => new CourierFlight(tuple[0].GetInt32(),
            tuple.GetArrayLength() < 5 || tuple[4].ValueKind == JsonValueKind.Null ? null
                : tuple[4].ValueKind == JsonValueKind.True ? 1 : tuple[4].ValueKind == JsonValueKind.False ? 0
                : tuple[4].ValueKind == JsonValueKind.Number && tuple[4].TryGetInt32(out var raw) ? raw
                : throw new InvalidDataException("Courier flying is not a boolean, integer or null."))).ToList(),
    };

    // [t, runeType, x, y, appeared (1) / vanished (0)] -> a rune change on the map.
    private static RuneOnMap ToRuneOnMap(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var type) || tuple[2].ValueKind != JsonValueKind.Number
            || tuple[3].ValueKind != JsonValueKind.Number || !tuple[4].TryGetInt32(out var present) || present is not (0 or 1))
        {
            throw new InvalidDataException("A rune on the map is not [t, type, x, y, appeared].");
        }

        return new RuneOnMap(t, type, (int)Math.Round(tuple[2].GetDouble()), (int)Math.Round(tuple[3].GetDouble()), present == 1);
    }

    // [t, lane, creepTeam, targetPlayer, hits] -> lane creep hits on a hero in the 2 s ending at t.
    private static CreepHeroHits ToCreepHeroHits(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || tuple[1].ValueKind != JsonValueKind.String || tuple[2].ValueKind != JsonValueKind.String
            || !tuple[3].TryGetInt32(out var player) || !tuple[4].TryGetInt32(out var hits))
        {
            throw new InvalidDataException("A creep hero damage entry is not [t, lane, team, player, hits].");
        }

        return new CreepHeroHits(t, tuple[1].GetString() switch
        {
            "top" => Lane.Top,
            "middle" => Lane.Middle,
            "bottom" => Lane.Bottom,
            var other => throw new InvalidDataException($"Unknown lane '{other}'."),
        }, ToTeam(tuple[2].GetString()), heroOf(player), hits);
    }

    private static NeutralCamp ToNeutralCamp(ReplayParserNeutralCamp camp) => new(
        camp.Id ?? throw Missing("neutralCamps.id"),
        camp.X ?? throw Missing("neutralCamps.x"),
        camp.Y ?? throw Missing("neutralCamps.y"),
        camp.Type,
        Required(camp.Alive, "neutralCamps.alive").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2 && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var alive)
                ? (t, alive)
                : throw new InvalidDataException("A camp reading is not [t, alive].")).ToList())
    { SpawnerType = camp.SpawnerType };

    // [t, radiantCount, direCount, radiantX | null, radiantY | null, direX | null, direY | null] -> off-lane creeps.
    private static OffLaneCreeps ToOffLaneCreeps(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var radiant) || !tuple[2].TryGetInt32(out var dire)
            || Enumerable.Range(3, 4).Any(i => tuple[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("An off-lane creeps entry is not [t, radiant, dire, x, y, x, y].");
        }

        int? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[i].GetDouble());
        return new OffLaneCreeps(t, radiant, dire, At(3), At(4), At(5), At(6));
    }

    // [t, lane | null, creepTeam, killerKind, killerPlayer | null, x | null, y | null] -> a lane creep death.
    private static LaneCreepDeath ToLaneCreepDeath(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || tuple[2].ValueKind != JsonValueKind.String
            || tuple[3].ValueKind != JsonValueKind.String
            || Enumerable.Range(4, 3).Any(i => tuple[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("A lane creep death is not [t, lane, team, killer, player, x, y].");
        }

        int? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[i].GetDouble());
        return new LaneCreepDeath(
            tuple[0].GetDouble(),
            tuple[1].ValueKind == JsonValueKind.Null ? null : tuple[1].GetString() switch
            {
                "top" => Lane.Top,
                "middle" => Lane.Middle,
                "bottom" => Lane.Bottom,
                var other => throw new InvalidDataException($"Unknown lane '{other}'."),
            },
            ToTeam(tuple[2].GetString()),
            tuple[3].GetString() switch
            {
                "hero" => CreepKiller.Hero,
                "creep" => CreepKiller.Creep,
                "tower" => CreepKiller.Tower,
                "neutral" => CreepKiller.Neutral,
                _ => CreepKiller.Other,
            },
            tuple[4].ValueKind == JsonValueKind.Null ? null : heroOf(tuple[4].GetInt32()),
            At(5), At(6));
    }

    // [t, radiant, dire] -> a lane's creep fronts at a second.
    private static WaveFront ToWaveFront(Lane lane, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3 || !tuple[0].TryGetInt32(out var seconds)
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[2].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            throw new InvalidDataException("A wave front is not [t, radiant, dire].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new WaveFront(lane, seconds, At(1), At(2));
    }

    // [t, slot, "item_name" | null] -> a slot change.
    private static SlotChange ToSlotChange(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || !tuple[0].TryGetInt32(out var seconds) || !tuple[1].TryGetInt32(out var slot)
            || tuple[2].ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException("A slot change is not [t, slot, name].");
        }

        return new SlotChange(heroId, seconds, slot, tuple[2].ValueKind == JsonValueKind.Null ? null : tuple[2].GetString());
    }

    // [t, "item_artifact", artifactLevel | null, tier, "item_enhancement" | null, enhancementLevel | null] -> a craft.
    private static NeutralCraft ToNeutralCraft(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 6
            || !tuple[0].TryGetDouble(out var seconds) || tuple[1].ValueKind != JsonValueKind.String
            || !tuple[3].TryGetInt32(out var tier)
            || tuple[4].ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || new[] { tuple[2], tuple[5] }.Any(level => level.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("A neutral craft is not [t, artifact, level, tier, enhancement, level].");
        }

        int? Level(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
        return new NeutralCraft(heroId, seconds, tuple[1].GetString()!, Level(tuple[2]), tier,
            tuple[4].ValueKind == JsonValueKind.Null ? null : tuple[4].GetString(), Level(tuple[5]));
    }

    // [t, inHand, acquired, found, neutralGold] -> a Madstone reading.
    private static MadstoneReading ToMadstoneReading(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5
            || Enumerable.Range(0, 5).Any(index => tuple[index].ValueKind != JsonValueKind.Number))
        {
            throw new InvalidDataException("A Madstone reading is not [t, inHand, acquired, found, neutralGold].");
        }

        int At(int index) => (int)Math.Round(tuple[index].GetDouble());
        return new MadstoneReading(At(0), At(1), At(2), At(3), At(4));
    }

    // [t, "name", readyAt] -> a cooldown that started.
    // [t, "name", entity, serial, readyAt] -> a cooldown drop.
    private static CooldownCorrection ToCooldownChange(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind != JsonValueKind.String || !tuple[2].TryGetInt32(out var entity) || !tuple[3].TryGetInt32(out var serial)
            || tuple[4].ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("A cooldown change is not [t, name, entity, serial, readyAt].");
        }

        return new CooldownCorrection(heroId, tuple[0].GetDouble(), tuple[1].GetString()!, entity, serial, tuple[4].GetDouble());
    }

    private static CooldownStart ToCooldown(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String
            || tuple[2].ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("A cooldown is not [t, name, readyAt].");
        }

        return new CooldownStart(heroId, tuple[0].GetDouble(), tuple[1].GetString()!, tuple[2].GetDouble());
    }

    // The to/from lists of one player merged into one bucket per start second.
    private static IEnumerable<DamageBucket> ToDamageBuckets(int heroId, ReplayParserCombatBuckets entry)
    {
        static IEnumerable<(int Seconds, int Amount)> Read(IReadOnlyList<JsonElement>? tuples, string name) =>
            Required(tuples, name).Select(tuple =>
                tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2
                && tuple[0].TryGetInt32(out var seconds) && tuple[1].TryGetInt32(out var amount) && amount >= 0
                    ? (seconds, amount)
                    : throw new InvalidDataException("A combat bucket is not [t, amount]."));

        var to = Read(entry.ToHeroes, "buckets.toHeroes").ToDictionary(bucket => bucket.Seconds, bucket => bucket.Amount);
        var from = Read(entry.FromHeroes, "buckets.fromHeroes").ToDictionary(bucket => bucket.Seconds, bucket => bucket.Amount);
        return to.Keys.Union(from.Keys).Order()
            .Select(seconds => new DamageBucket(heroId, seconds, to.GetValueOrDefault(seconds), from.GetValueOrDefault(seconds)));
    }

    // [t, "name"] -> a recorded use, with the replay's fractional time.
    private static RecordedUse ToRecordedUse(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 2
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tuple[1].GetString()))
        {
            throw new InvalidDataException("A use is not [t, \"name\"].");
        }

        return new RecordedUse(heroId, tuple[0].GetDouble(), tuple[1].GetString()!);
    }
}
