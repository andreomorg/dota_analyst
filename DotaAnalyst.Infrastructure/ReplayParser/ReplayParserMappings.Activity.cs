using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// What the heroes did (ReplayActivity), what the metrics need from it, and the model sections check.
internal static partial class ReplayParserMappings
{
    // Whether the document has every section the prediction models read (added on 20/10/2026): vitals next to the
    // positions, waves, combat buckets, inventory changes and cooldowns. Older documents are parsed again to get them.
    // 21/10/2026 (X1/X2/X4): also day/night, combat attributes next to the positions, hero hits, creep kills and the
    // debuffs. The orders (X3) are not required: a replay may lack them (the field is then left out), so they can't
    // mark a document as old. X8/X16: the buffs and the wave sizes too (a lane without creeps has no sizes).
    internal static bool HasModelSections(ReplayParserMatchResponse response) =>
        response.Timelines?.Positions is { Count: > 0 } positions && positions.All(entry => entry?.Vitals is not null && entry.Stats is not null)
        // W1/W2: wave strength next to the sizes, the off-lane creeps (always written, [] when none) and the lane creep
        // deaths.
        && response.Timelines.Waves is { } waves
        && waves.All(lane => lane?.Fronts is not { Count: > 0 }
            || (lane.Sizes is not null && lane.Strength is not null && lane.Composition is not null))
        // X25/X26: gold/XP by reason and runes on the map (pings and ability targets may be empty: not required).
        && response.Combat?.Economy is { Count: > 0 } && response.Objectives?.RunesOnMap is { Count: > 0 }
        // X28/X30: couriers and typed damage (some player always has it).
        && response.Timelines.Couriers is { Count: > 0 }
        && response.Combat.Buckets is { } typedBuckets && typedBuckets.Any(entry => entry?.ByType is not null)
        // W3/W6: neutral camps and creep hits on heroes.
        && response.Objectives?.NeutralCamps is { Count: > 0 } && response.Timelines.CreepHeroDamage is { Count: > 0 }
        && response.Timelines.OffLaneCreeps is not null
        && response.Combat?.LaneCreepDeaths is { Count: > 0 }
        && response.Timelines.DayNight is not null && response.Combat?.Buckets is not null
        && response.Combat.HeroHits is not null && response.Combat.CreepKills is not null && response.Combat.Debuffs is not null
        && response.Combat.Buffs is not null
        // X15/X15b: building health, with positions.
        && response.Objectives?.BuildingHealth is { Count: > 0 } buildings && buildings.All(entry => entry?.X is not null && entry.Y is not null)
        && response.Items?.Slots is not null && response.Items.Cooldowns is not null && response.Abilities?.Cooldowns is not null
        // N2: neutral crafts and Madstones (always present in a new document, [] when nothing happened).
        && response.Items.NeutralCrafts is not null && response.Timelines.Madstones is not null;

    // Smokes and item/ability uses, for the prediction models' samples only (never saved with the analysis). Null when
    // the document lacks one of the three sections: unknown activity is not "nothing was used".
    internal static ReplayActivity? ReadActivity(ReplayParserMatchResponse response)
    {
        if (response.Vision?.Smokes is not { } smokes || response.Items?.Uses is not { } itemUses
            || response.Abilities?.Uses is not { } abilityUses)
        {
            return null;
        }

        var players = Required(response.Players, "players").Select(p => p ?? throw Empty("player"))
            .ToDictionary(p => p.Player ?? throw Missing("players.player"));
        ReplayParserMatchPlayer PlayerOf(int? index) =>
            players.TryGetValue(index ?? throw Missing("player"), out var player)
                ? player : throw new InvalidDataException($"Unknown player {index}.");

        IEnumerable<RecordedUse> Uses(int? player, IReadOnlyList<JsonElement>? tuples, string name) =>
            Required(tuples, name).Select(tuple => ToRecordedUse(PlayerOf(player).HeroId ?? throw Missing("players.heroId"), tuple));

        return new ReplayActivity(
            smokes.Select(smoke => smoke ?? throw Empty("smoke"))
                .Select(smoke => new SmokeUse(
                    PlayerOf(smoke.Player).HeroId ?? throw Missing("players.heroId"), ToTeam(PlayerOf(smoke.Player).Team),
                    smoke.T ?? throw Missing("smoke.t")))
                .ToList(),
            itemUses.Select(entry => entry ?? throw Empty("item uses entry"))
                .SelectMany(entry => Uses(entry.Player, entry.Items, "item uses.items")).ToList(),
            abilityUses.Select(entry => entry ?? throw Empty("ability uses entry"))
                .SelectMany(entry => WithTargets(Uses(entry.Player, entry.Abilities, "ability uses.abilities").ToList(), entry.Targets))
                .ToList(),
            response.Combat?.Buckets is { } buckets
                ? buckets.Select(entry => entry ?? throw Empty("combat buckets entry"))
                    .SelectMany(entry => ToDamageBuckets(HeroOf(entry.Player), entry))
                    .ToList()
                : null,
            response.Items?.Slots is { } slots
                ? slots.Select(entry => entry ?? throw Empty("slots entry"))
                    .SelectMany(entry => Required(entry.Changes, "slots.changes").Select(tuple => ToSlotChange(HeroOf(entry.Player), tuple)))
                    .ToList()
                : null,
            Cooldowns(response.Items?.Cooldowns, "item cooldowns"),
            Cooldowns(response.Abilities?.Cooldowns, "ability cooldowns"),
            response.Buybacks?.Select(buyback => buyback ?? throw Empty("buyback"))
                .Select(buyback => new BuybackUse(HeroOf(buyback.Player), buyback.T ?? throw Missing("buyback.t"),
                    buyback.Cost is < 0 ? throw new InvalidDataException("Invalid buyback cost.") : buyback.Cost))
                .ToList(),
            TeamActions(response.Vision?.Scans, "scan"),
            TeamActions(response.Vision?.Glyphs, "glyph"),
            response.Objectives?.Runes?.Select(rune => rune ?? throw Empty("rune"))
                .Select(rune => new RunePickup(HeroOf(rune.Player), ToTeam(PlayerOf(rune.Player).Team), rune.T ?? throw Missing("rune.t"),
                    rune.RuneType ?? throw Missing("rune.runeType")))
                .ToList(),
            response.Timelines?.Waves?.Select(lane => lane ?? throw Empty("waves entry"))
                .SelectMany(lane => ToWaveFronts(lane.Lane switch
                {
                    "top" => Lane.Top,
                    "middle" => Lane.Middle,
                    "bottom" => Lane.Bottom,
                    _ => throw new InvalidDataException($"Unknown wave lane '{lane.Lane}'."),
                }, Required(lane.Fronts, "waves.fronts"), lane.Sizes, lane.Strength, lane.Composition))
                .ToList(),
            response.Timelines?.DayNight?.Select(ToDayNight).ToList(),
            response.Combat?.HeroHits?.Select(tuple => ToHeroHit(tuple, HeroOf)).ToList(),
            response.Combat?.CreepKills?.Select(tuple => ToCreepKill(tuple, HeroOf)).ToList(),
            response.Combat?.Debuffs?.Where(EndsAfterStart).Select(tuple => ToDebuff(tuple, HeroOf)).ToList(),
            response.Combat?.Buffs?.Where(EndsAfterStart).Select(tuple => ToBuff(tuple, HeroOf)).ToList(),
            response.Objectives?.BuildingHealth?.Select(entry => ToBuildingHealth(entry ?? throw Empty("building health"))).ToList(),
            response.Timelines?.OffLaneCreeps?.Select(ToOffLaneCreeps).ToList(),
            response.Combat?.LaneCreepDeaths?.Select(tuple => ToLaneCreepDeath(tuple, HeroOf)).ToList(),
            response.Timelines?.CreepHeroDamage?.Select(tuple => ToCreepHeroHits(tuple, HeroOf)).ToList(),
            response.Objectives?.NeutralCamps?.Select(camp => ToNeutralCamp(camp ?? throw Empty("neutral camp"))).ToList(),
            response.Combat?.Economy?.Select(tuple => ToEconomyBucket(tuple, HeroOf)).ToList(),
            response.Vision?.Pings?.Select(tuple => ToMapPing(tuple, player => (HeroOf(player), ToTeam(PlayerOf(player).Team)))).ToList(),
            response.Objectives?.RunesOnMap?.Select(ToRuneOnMap).ToList(),
            // Sparse and left out for a player with nothing: one player with the list means the document has them.
            response.Combat?.Buckets is { } typedBuckets && typedBuckets.Any(entry => entry?.ByType is not null)
                ? typedBuckets.SelectMany(entry => (entry!.ByType ?? []).Select(tuple => ToTypedDamage(HeroOf(entry.Player), tuple))).ToList()
                : null,
            response.Combat?.Buckets is { } healingBuckets && healingBuckets.Any(entry => entry?.Healing is not null)
                ? healingBuckets.SelectMany(entry => (entry!.Healing ?? []).Select(tuple => ToHealing(HeroOf(entry.Player), tuple))).ToList()
                : null,
            response.Timelines?.Couriers?.Select(courier => ToCourier(courier ?? throw Empty("courier"))).ToList(),
            response.Items?.NeutralCrafts?.Select(entry => entry ?? throw Empty("neutral crafts entry"))
                .SelectMany(entry => Required(entry.Crafts, "neutralCrafts.crafts").Select(tuple => ToNeutralCraft(HeroOf(entry.Player), tuple)))
                .ToList(),
            response.Timelines?.Madstones?.Select(entry => entry ?? throw Empty("madstones entry"))
                .Select(entry => new MadstoneTrack(HeroOf(entry.Player),
                    Required(entry.Samples, "madstones.samples").Select(ToMadstoneReading).ToList()))
                .ToList(),
            ReadTrees(response),
            response.Timelines?.VisionUnits?.Select(ToVisionUnits).ToList(),
            CooldownChanges(response.Items?.CooldownChanges, "item cooldown changes"),
            CooldownChanges(response.Abilities?.CooldownChanges, "ability cooldown changes"))
        { EconomyStates = ReadEconomyStates(response) };

        List<CooldownCorrection>? CooldownChanges(IReadOnlyList<ReplayParserPlayerCooldownChanges?>? entries, string name) =>
            entries?.Select(entry => entry ?? throw Empty(name))
                .SelectMany(entry => Required(entry.Changes, name).Select(tuple => ToCooldownChange(HeroOf(entry.Player), tuple)))
                .ToList();

        // Targets[i] is the target of uses[i]; a different length means the lists are not parallel.
        List<RecordedUse> WithTargets(List<RecordedUse> uses, IReadOnlyList<JsonElement>? targets)
        {
            if (targets is null)
            {
                return uses;
            }

            if (targets.Count != uses.Count)
            {
                throw new InvalidDataException("Ability targets are not parallel to the uses.");
            }

            return uses.Select((use, index) =>
            {
                var target = targets[index];
                if (target.ValueKind != JsonValueKind.Array || target.GetArrayLength() is not (3 or 4)
                    || Enumerable.Range(0, target.GetArrayLength()).Any(i => target[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
                {
                    throw new InvalidDataException("An ability target is not [player, x, y, source].");
                }

                int? At(int i) => target[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(target[i].GetDouble());
                if (target.GetArrayLength() == 4 && target[3].ValueKind != JsonValueKind.Null && !target[3].TryGetInt32(out _))
                    throw new InvalidDataException("Ability target source is not an integer.");
                return use with
                {
                    TargetHeroId = At(0) is { } player ? HeroOf(player) : null,
                    TargetX = At(1),
                    TargetY = At(2),
                    PointSource = target.GetArrayLength() == 4 ? At(3) : null
                };
            }).ToList();
        }

        int HeroOf(int? player) => PlayerOf(player).HeroId ?? throw Missing("players.heroId");

        static List<TeamAction>? TeamActions(IReadOnlyList<ReplayParserTeamEvent?>? events, string name) =>
            events?.Select(action => action ?? throw Empty(name))
                .Select(action => new TeamAction(ToTeam(action.Team), action.T ?? throw Missing($"{name}.t")))
                .ToList();

        List<CooldownStart>? Cooldowns(IReadOnlyList<ReplayParserPlayerCooldowns?>? entries, string name) =>
            entries?.Select(entry => entry ?? throw Empty($"{name} entry"))
                .SelectMany(entry => Required(entry.Cooldowns, $"{name}.cooldowns").Select(tuple => ToCooldown(HeroOf(entry.Player), tuple)))
                // A zero-length cooldown (ready the moment it starts: seen on a pre-game TP scroll) never ran: no
                // cooldown, not an unreadable document (9 of 1,246 kept documents, 23/10/2026).
                .Where(cooldown => cooldown.ReadyAt > cooldown.Seconds)
                .ToList();
    }

    private static BuildingHealth ToBuildingHealth(ReplayParserBuildingHealth entry) => new(
        entry.Building ?? throw Missing("buildingHealth.building"),
        ToTeam(entry.Team),
        entry.Kind switch
        {
            "tower" => BuildingKind.Tower,
            "barracks" => BuildingKind.Barracks,
            "ancient" => BuildingKind.Ancient,
            _ => throw new InvalidDataException($"Unknown building kind '{entry.Kind}'."),
        },
        entry.Lane switch
        {
            null => null,
            "top" => Lane.Top,
            "middle" => Lane.Middle,
            "bottom" => Lane.Bottom,
            _ => throw new InvalidDataException($"Unknown building lane '{entry.Lane}'."),
        },
        entry.Tier,
        entry.X ?? throw Missing("buildingHealth.x"),
        entry.Y ?? throw Missing("buildingHealth.y"),
        Required(entry.Health, "buildingHealth.health").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 3 && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var health) && tuple[2].TryGetInt32(out var maxHealth)
                ? new BuildingHealthReading(t, health, maxHealth)
                : throw new InvalidDataException("A building health reading is not [t, health, maxHealth].")).ToList());

    // A debuff or a buff, its kind refined in the Domain (DebuffKinds, BuffKinds).
    private static Debuff ToDebuff(JsonElement tuple, Func<int?, int> heroOf)
    {
        var m = ReadModifier(tuple, heroOf, "debuff");
        return new Debuff(m.Start, m.End, m.Source, m.Target, m.Modifier, DebuffKinds.Of(m.Kind, m.Modifier));
    }

    private static Buff ToBuff(JsonElement tuple, Func<int?, int> heroOf)
    {
        var m = ReadModifier(tuple, heroOf, "buff");
        return new Buff(m.Start, m.End, m.Source, m.Target, m.Modifier, BuffKinds.Of(m.Kind, m.Modifier));
    }

    // A modifier applied during a pause gets a start too late: the Go clock counts the pause only when it ends, so the
    // removal can land before the application (0.14% of the rows, in the matches with pauses). Those times are not
    // trustworthy and the row is dropped; a malformed tuple still fails in ReadModifier.
    private static bool EndsAfterStart(JsonElement tuple) =>
        tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 2
        || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.Number
        || tuple[1].GetDouble() >= tuple[0].GetDouble();

    // [start, end | null, source | null, target, "modifier", "kind"] (player indexes; the same tuple for both lists).
    private static (double Start, double? End, int? Source, int Target, string Modifier, string Kind) ReadModifier(
        JsonElement tuple, Func<int?, int> heroOf, string name)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 6 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[2].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || !tuple[3].TryGetInt32(out var target) || tuple[4].ValueKind != JsonValueKind.String
            || tuple[5].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"A {name} is not [start, end, source, target, modifier, kind].");
        }

        return (tuple[0].GetDouble(),
            tuple[1].ValueKind == JsonValueKind.Null ? null : tuple[1].GetDouble(),
            tuple[2].ValueKind == JsonValueKind.Null ? null : heroOf(tuple[2].GetInt32()),
            heroOf(target),
            tuple[4].GetString()!,
            tuple[5].GetString()!);
    }

    // [t, isDay] -> a day/night change.
    private static DayNightChange ToDayNight(JsonElement tuple) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new DayNightChange(seconds, tuple[1].GetBoolean())
            : throw new InvalidDataException("A day/night change is not [t, isDay].");

    // [t, index, alive] -> a tree going down or growing back.
    // Each hero's counters of the map's objectives from players[].final; null unless every player has all of them.
    private static Dictionary<int, MapObjectiveCounts>? ObjectiveCounts(IReadOnlyList<ReplayParserMatchPlayer> players)
    {
        var counts = new Dictionary<int, MapObjectiveCounts>();
        foreach (var player in players)
        {
            if (player is not { HeroId: { } heroId, Final: { ValueKind: JsonValueKind.Object } block }
                || block.Deserialize<ReplayParserPlayerFinal>(Coaching.CoachingJson.Options) is not
                {
                    BountyRunes: >= 0 and var bounty, PowerRunes: >= 0 and var power, WaterRunes: >= 0 and var water,
                    WisdomShrinesTaken: >= 0 and var wisdom, LotusesTaken: >= 0 and var lotuses, WatchersTaken: >= 0 and var watchers,
                    OutpostsCaptured: >= 0 and var outposts, TormentorKills: >= 0 and var tormentors, CampsStacked: >= 0 and var stacked,
                })
            {
                return null;
            }

            counts[heroId] = new MapObjectiveCounts(bounty, power, water, wisdom, lotuses, watchers, outposts, tormentors, stacked);
        }

        return counts;
    }

    // The activity for the play quality metrics: a malformed activity section only leaves them unknown, it never fails
    // an analysis that did not need it before.
    private static ReplayActivity? ActivityForMetrics(ReplayParserMatchResponse response)
    {
        try
        {
            return ReadActivity(response);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // timelines.trees (MP3); null in documents from before it.
    private static TreeTimeline? ReadTrees(ReplayParserMatchResponse response) =>
        response.Timelines?.Trees is { } trees
            ? new TreeTimeline(Required(trees.DeadAtStart, "trees.deadAtStart"),
                Required(trees.Changes, "trees.changes").Select(ToTreeChange).ToList())
            : null;

    private static TreeChange ToTreeChange(JsonElement tuple) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 3 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].TryGetInt32(out var index) && tuple[2].ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new TreeChange(seconds, index, tuple[2].GetBoolean())
            : throw new InvalidDataException("A tree change is not [t, index, alive].");
}
