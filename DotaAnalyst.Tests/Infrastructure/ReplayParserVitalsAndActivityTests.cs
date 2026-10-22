using System.Text.Json;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

// The parts of the kept document only the prediction models read: vitals next to the positions, and the activity.
public class ReplayParserVitalsAndActivityTests
{
    private const string Players = """
        "players": [
          { "player": 0, "heroId": 1, "team": "radiant", "role": 1 },
          { "player": 5, "heroId": 6, "team": "dire", "role": 1 }
        ]
        """;

    [Fact]
    public void Vitals_GoWithTheSampleOfTheSameIndex_AndMissingOnesAreNull()
    {
        var positions = ReplayParserMappings.ReadPositions(Read("""
            "timelines": { "positions": [
              { "player": 0, "samples": [[0, 10, 20], [2, 30, 40]],
                "vitals": [[640, 700, 250, 291, 1, 32, 1200], [null, 700, null, 291, null, null, null]] }
            ] }
            """));

        var samples = positions.Single().Samples;
        Assert.Equal((640, 700, 250, 291, true), (samples[0].Vitals!.Health, samples[0].Vitals!.MaxHealth, samples[0].Vitals!.Mana,
            samples[0].Vitals!.MaxMana, samples[0].Vitals!.VisibleToEnemies));
        Assert.Equal((32, 1200, true), (samples[0].Vitals!.UnitStateMask, samples[0].Vitals!.Gold, samples[0].Vitals!.Stunned));
        Assert.Equal(640 / 700d, samples[0].Vitals!.HealthShare);
        Assert.Null(samples[1].Vitals!.HealthShare);
    }

    [Fact]
    public void OlderDocumentsWithoutVitals_KeepTheirSamples()
    {
        var positions = ReplayParserMappings.ReadPositions(Read("""
            "timelines": { "positions": [ { "player": 0, "samples": [[0, 10, 20]] } ] }
            """));

        Assert.Null(positions.Single().Samples.Single().Vitals);
    }

    [Theory]
    [InlineData("[[640, 700, 250, 291, null, null, null]]")]                 // one entry for two samples
    [InlineData("[[640, 700, 250, 291, null], [1, 2, 3, 4, null]]")]        // a tuple too short
    [InlineData("[[-1, 700, 250, 291, null, null, null], [1, 2, 3, 4, null, null, null]]")]
    [InlineData("[[1, 700, 250, 291, null, null, -5], [1, 2, 3, 4, null, null, null]]")]   // negative gold
    [InlineData("[[1, 700, 250, 291, 2, null, null], [1, 2, 3, 4, null, null, null]]")]    // visibility not 0/1
    public void BrokenVitals_MakeTheDocumentUnreadable(string vitals)
    {
        var response = Read($$"""
            "timelines": { "positions": [ { "player": 0, "samples": [[0, 10, 20], [2, 30, 40]], "vitals": {{vitals}} } ] }
            """);

        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadPositions(response));
    }

    [Fact]
    public void Activity_IsReadWithTheReplaysOwnTimes_AndTheSmokersTeam()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [ { "t": 90.4, "player": 5, "x": 0, "y": 0 } ] },
            "items": { "purchases": [], "uses": [ { "player": 0, "items": [[100.1, "item_tpscroll"]] } ] },
            "abilities": { "upgrades": [], "uses": [ { "player": 5, "abilities": [[12.5, "lina_dragon_slave"]] } ] }
            """))!;

        var smoke = Assert.Single(activity.Smokes);
        Assert.Equal((6, Team.Dire, 90.4), (smoke.HeroId, smoke.Team, smoke.Seconds));
        Assert.Equal((1, 100.1, "item_tpscroll"), (activity.ItemUses.Single().HeroId, activity.ItemUses.Single().Seconds,
            activity.ItemUses.Single().Name));
        Assert.Equal(6, activity.AbilityUses.Single().HeroId);
    }

    [Fact]
    public void CombatBuckets_MergeDealtAndTakenByStartSecond_AndOlderDocumentsHaveNone()
    {
        const string Activity = """
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] }
            """;
        var withBuckets = ReplayParserMappings.ReadActivity(Read(Activity + """
            , "combat": { "damage": [], "perMinute": [], "buckets": [
              { "player": 0, "toHeroes": [[10, 50], [12, 5]], "fromHeroes": [[12, 80]] }
            ] }
            """))!;
        var older = ReplayParserMappings.ReadActivity(Read(Activity))!;

        Assert.Equal(
            [new DamageBucketView(10, 50, 0), new DamageBucketView(12, 5, 80)],
            withBuckets.DamageBuckets!.Select(bucket => new DamageBucketView(bucket.Seconds, bucket.ToHeroes, bucket.FromHeroes)));
        Assert.All(withBuckets.DamageBuckets!, bucket => Assert.Equal(1, bucket.HeroId));
        Assert.Null(older.DamageBuckets);
    }

    private sealed record DamageBucketView(int Seconds, int ToHeroes, int FromHeroes);

    [Fact]
    public void WaveFronts_AreReadPerLane()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "waves": [
              { "lane": "middle", "fronts": [[20, -900, null], [22, -700, 400]] },
              { "lane": "top", "fronts": [] }
            ] }
            """))!;

        Assert.Equal(
            [new DotaAnalyst.Domain.Replays.WaveFront(DotaAnalyst.Domain.Replays.Lane.Middle, 20, -900, null),
             new DotaAnalyst.Domain.Replays.WaveFront(DotaAnalyst.Domain.Replays.Lane.Middle, 22, -700, 400)],
            activity.Waves!);
    }

    [Fact]
    public void ModifiersRemovedBeforeTheirStart_AreDropped()
    {
        // A pause counted late by the Go clock (21/10/2026: 0.14% of the rows): the real row would make the document unreadable.
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "combat": { "damage": [], "perMinute": [],
              "buffs": [[-88.6, -249, 0, 0, "modifier_kez_switch_weapons_flutter_sai", "other"],
                        [10, 12, 0, 0, "modifier_item_phase_boots_active", "other"],
                        [20, null, 0, 0, "modifier_smoke_of_deceit", "smoke"]],
              "debuffs": [[300, 290, 5, 0, "modifier_stunned", "stun"], [400, 401.5, 5, 0, "modifier_stunned", "stun"]] }
            """))!;

        Assert.Equal(["modifier_item_phase_boots_active", "modifier_smoke_of_deceit"], activity.Buffs!.Select(buff => buff.Modifier));
        Assert.Equal(400, Assert.Single(activity.Debuffs!).Start);
    }

    [Fact]
    public void BuildingHealth_IsReadWithItsPositionAndReadings()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "objectives": { "buildings": [], "roshan": [], "tormentors": [], "buildingHealth": [
              { "building": "npc_dota_goodguys_tower2_mid", "team": "radiant", "kind": "tower", "lane": "middle", "tier": 2,
                "x": -2500, "y": -2000, "health": [[0, 2500, 2500], [600, 750, 2500], [602, 0, 2500]] },
              { "building": "npc_dota_badguys_fort", "team": "dire", "kind": "ancient", "lane": null, "tier": null,
                "x": 5500, "y": 5000, "health": [[0, 4500, 4500]] }
            ] }
            """))!;

        var tower = activity.Buildings![0];
        Assert.Equal((DotaAnalyst.Domain.Replays.BuildingKind.Tower, (DotaAnalyst.Domain.Replays.Lane?)DotaAnalyst.Domain.Replays.Lane.Middle, 2, -2500, -2000),
            (tower.Kind, tower.Lane, tower.Tier, tower.X, tower.Y));
        Assert.Equal(new DotaAnalyst.Domain.Replays.BuildingHealthReading(600, 750, 2500), tower.At(601));
        Assert.False(tower.StandingAt(602));
        Assert.Equal((DotaAnalyst.Domain.Replays.BuildingKind.Ancient, (DotaAnalyst.Domain.Replays.Lane?)null),
            (activity.Buildings[1].Kind, activity.Buildings[1].Lane));
    }

    [Fact]
    public void WaveStrength_OffLaneCreeps_AndLaneCreepDeaths_AreRead()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "offLaneCreeps": [[98, 2, 0, -3000.4, -500, null, null]], "waves": [
              { "lane": "middle", "fronts": [[20, -900, null]], "sizes": [[20, 4, 0, 4, 0]],
                "strength": [[20, 2200, 0, 2200, 0, 1, 0, -950, null]] }
            ] },
            "combat": { "damage": [], "perMinute": [],
              "laneCreepDeaths": [[95.3, "middle", "radiant", "tower", null, 0, 0], [96, null, "dire", "hero", 0, null, null]] }
            """))!;

        Assert.Equal(new DotaAnalyst.Domain.Replays.WaveStrength(2200, 0, 2200, 0, true, false, -950, null), activity.Waves![0].Strength);
        Assert.Equal(new DotaAnalyst.Domain.Replays.OffLaneCreeps(98, 2, 0, -3000, -500, null, null), activity.OffLaneCreeps![0]);
        Assert.Equal(
            [new DotaAnalyst.Domain.Replays.LaneCreepDeath(95.3, DotaAnalyst.Domain.Replays.Lane.Middle, DotaAnalyst.Domain.Matches.Team.Radiant,
                DotaAnalyst.Domain.Replays.CreepKiller.Tower, null, 0, 0),
             new DotaAnalyst.Domain.Replays.LaneCreepDeath(96, null, DotaAnalyst.Domain.Matches.Team.Dire,
                DotaAnalyst.Domain.Replays.CreepKiller.Hero, KillerHeroId: 1, null, null)],
            activity.LaneCreepDeaths!);
    }

    [Fact]
    public void WaveComposition_CreepHeroHits_AndNeutralCamps_AreRead()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "creepHeroDamage": [[26, "middle", "radiant", 5, 1]], "waves": [
              { "lane": "middle", "fronts": [[600, -900, 300]], "composition": [[600, 3, 2, 1, 1, 0, 0, 0, 5, 2, 1, 1, 0, 0, 1]] }
            ] },
            "objectives": { "buildings": [], "roshan": [], "tormentors": [], "neutralCamps": [
              { "id": "camp:-1454:-3356", "x": -1454, "y": -3356, "type": "large", "spawnerType": 2, "alive": [[0, 0], [42, 3]] }
            ] }
            """))!;

        var composition = activity.Waves![0].Composition!;
        Assert.Equal((3, 2, 1, 1, 0, 0, 0), (composition.Radiant.Melee, composition.Radiant.Ranged, composition.Radiant.Siege,
            composition.Radiant.Flagbearer, composition.Radiant.Super, composition.Radiant.Mega, composition.Radiant.LowAtFront));
        Assert.Equal((5, 1), (composition.Dire.Melee, composition.Dire.LowAtFront));
        Assert.Equal(new DotaAnalyst.Domain.Replays.CreepHeroHits(26, DotaAnalyst.Domain.Replays.Lane.Middle,
            DotaAnalyst.Domain.Matches.Team.Radiant, 6, 1), activity.CreepHeroHits![0]);
        var camp = activity.NeutralCamps![0];
        Assert.Equal(2, camp.SpawnerType);
        Assert.Equal(("camp:-1454:-3356", -1454, "large", 3, (int?)null), (camp.Id, camp.X, camp.Type, camp.AliveAt(50), camp.AliveAt(-1)));
    }

    [Fact]
    public void Economy_Pings_RunesOnMap_AndAbilityTargets_AreRead()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [], "pings": [[22.1, 5, -3711.4, 5862, 5]] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [
              { "player": 0, "abilities": [[415.5, "riki_smoke_screen"], [420, "riki_tricks"]], "targets": [[null, 506, 1106, 1], [5, null, null, 0]] }
            ] },
            "combat": { "damage": [], "perMinute": [], "economy": [[-90, 0, 0, 0, 600], [114, 5, 1, 2, 57], [116, 5, 0, null, 10]] },
            "objectives": { "buildings": [], "roshan": [], "tormentors": [], "runesOnMap": [[122, 7, -1640, 1112, 1]] }
            """))!;

        Assert.Equal(
            [new DotaAnalyst.Domain.Replays.RecordedUse(1, 415.5, "riki_smoke_screen", null, 506, 1106, 1),
             new DotaAnalyst.Domain.Replays.RecordedUse(1, 420, "riki_tricks", 6, null, null, 0)],
            activity.AbilityUses);
        Assert.Equal((1, "castPositionOrder"), (activity.AbilityUses[0].PointSource, activity.AbilityUses[0].PointOrigin));
        Assert.Equal(
            [(DotaAnalyst.Domain.Replays.EconomyResource.Gold, DotaAnalyst.Domain.Replays.EconomySource.Other, 600),
             (DotaAnalyst.Domain.Replays.EconomyResource.Experience, DotaAnalyst.Domain.Replays.EconomySource.Creeps, 57),
             (DotaAnalyst.Domain.Replays.EconomyResource.Gold, DotaAnalyst.Domain.Replays.EconomySource.Other, 10)],
            activity.Economy!.Select(bucket => (bucket.Resource, bucket.Source, bucket.Amount)));
        Assert.Equal(new DotaAnalyst.Domain.Replays.MapPing(6, DotaAnalyst.Domain.Matches.Team.Dire, 22.1, -3711, 5862, 5), activity.Pings![0]);
        Assert.Equal(new DotaAnalyst.Domain.Replays.RuneOnMap(122, 7, -1640, 1112, true), activity.RunesOnMap![0]);
    }

    [Fact]
    public void TypedDamage_Healing_Couriers_AndFacing_AreRead()
    {
        var document = Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [{ "player": 0, "samples": [[22, 0, 0], [24, 10, 10]], "facing": [55, null] }],
              "couriers": [{ "id": "radiant:1", "team": "radiant", "samples": [[0, 1, -6841, -6841, false]] }] },
            "combat": { "damage": [], "perMinute": [], "buckets": [
              { "player": 0, "toHeroes": [], "fromHeroes": [], "byType": [[22, 0, 0, 0, 0, 89, 0]], "healing": [[24, 12, 12]] },
              { "player": 5, "toHeroes": [], "fromHeroes": [] }
            ] }
            """);
        var activity = ReplayParserMappings.ReadActivity(document)!;
        var positions = ReplayParserMappings.ReadPositions(document);

        Assert.Equal(new DotaAnalyst.Domain.Replays.TypedDamageBucket(1, 22, 0, 0, 0, 0, 89, 0), Assert.Single(activity.TypedDamage!));
        Assert.Equal(new DotaAnalyst.Domain.Replays.HealingBucket(1, 24, 12, 12), Assert.Single(activity.Healing!));
        Assert.Equal((true, -6841), (activity.Couriers![0].AliveAt(10), activity.Couriers[0].Samples[0].X));
        Assert.False(activity.Couriers[0].Flight![0].Flying);
        Assert.Equal(new int?[] { 55, null }, positions[0].Samples.Select(sample => sample.Facing));
    }

    [Fact]
    public void WaveSizes_AreReadNextToTheirFronts_AndMustBeParallel()
    {
        static string Document(string sizes) => $$"""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "waves": [
              { "lane": "middle", "fronts": [[20, -900, null], [22, -700, 400]], "sizes": {{sizes}} }
            ] }
            """;

        var activity = ReplayParserMappings.ReadActivity(Read(Document("[[20, 4, 0, 4, 0], [22, 9, 5, 4, 3]]")))!;

        Assert.Equal(
            [new DotaAnalyst.Domain.Replays.WaveCounts(4, 0, 4, 0), new DotaAnalyst.Domain.Replays.WaveCounts(9, 5, 4, 3)],
            activity.Waves!.Select(wave => wave.Counts));
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadActivity(Read(Document("[[20, 4, 0, 4, 0]]"))));
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadActivity(Read(Document("[[20, 4, 0, 4, 0], [24, 9, 5, 4, 3]]"))));
    }

    [Fact]
    public void Activity_IsUnknownWhenASectionIsMissing()
    {
        // No ability uses in this document: unknown, not "nothing was used".
        Assert.Null(ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [] }
            """)));
    }

    [Fact]
    public void NeutralCrafts_Madstones_AndTheEnhancementSlot_AreRead()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [],
              "slots": [{ "player": 0, "changes": [[1208, 16, "item_occult_bracelet"], [1208, 17, "item_enhancement_brawny"]] }],
              "neutralCrafts": [
                { "player": 0, "crafts": [[1207.5, "item_occult_bracelet", 1, 1, "item_enhancement_brawny", 1]] },
                { "player": 5, "crafts": [] }
              ] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "madstones": [
              { "player": 0, "samples": [[0, 0, 0, 0, 0], [612, 4, 4, 6, 163]] },
              { "player": 5, "samples": [[0, 0, 0, 0, 0]] }
            ] }
            """))!;

        Assert.Equal(new DotaAnalyst.Domain.Replays.NeutralCraft(1, 1207.5, "item_occult_bracelet", 1, 1, "item_enhancement_brawny", 1),
            Assert.Single(activity.NeutralCrafts!));
        Assert.Equal(DotaAnalyst.Domain.Replays.SlotChange.EnhancementSlot, activity.SlotChanges![1].Slot);
        var madstones = activity.Madstones!.Single(track => track.HeroId == 1);
        Assert.Equal(new DotaAnalyst.Domain.Replays.MadstoneReading(612, 4, 4, 6, 163), madstones.At(700));
        Assert.Equal(0, madstones.At(611)!.InHand);
        Assert.Equal(2, activity.Madstones!.Count);
    }

    [Fact]
    public void DocumentsWithoutNeutralSections_LeaveThemUnknown_AndBrokenTuplesThrow()
    {
        const string Without = """
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] }
            """;
        var older = ReplayParserMappings.ReadActivity(Read(Without))!;
        Assert.Null(older.NeutralCrafts);
        Assert.Null(older.Madstones);

        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [], "neutralCrafts": [{ "player": 0, "crafts": [[10, "item_x", 1]] }] },
            "abilities": { "upgrades": [], "uses": [] }
            """)));
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadActivity(Read("""
            "vision": { "wards": [], "smokes": [] },
            "items": { "purchases": [], "uses": [] },
            "abilities": { "upgrades": [], "uses": [] },
            "timelines": { "positions": [], "madstones": [{ "player": 0, "samples": [[0, 1, 2]] }] }
            """)));
    }

    [Theory]
    [InlineData("[0,1,10,20]", null)]
    [InlineData("[0,1,10,20,null]", null)]
    [InlineData("[0,1,10,20,7]", null)]
    [InlineData("[0,1,10,20,true]", true)]
    public void CourierFlight_PreservesLegacyAndUnknown(string sample, bool? flying)
    {
        var response = Read($$"""
            "vision": { "wards": [], "smokes": [] }, "items": { "uses": [] }, "abilities": { "uses": [] },
            "timelines": { "couriers": [{ "id": "one", "team": "radiant", "samples": [{{sample}}] }] }
            """);
        var flight = ReplayParserMappings.ReadActivity(response)!.Couriers![0].Flight![0];
        Assert.Equal(flying, flight.Flying);
        if (sample.EndsWith(",7]", StringComparison.Ordinal)) Assert.Equal(7, flight.Raw);
    }

    [Theory]
    [InlineData("[null,1,10,20,false]")]
    [InlineData("[0,1,\"x\",20,false]")]
    [InlineData("[0,1,10,20,\"flying\"]")]
    public void CourierFlight_RejectsInvalidTupleAsUnreadable(string sample)
    {
        var response = Read($$"""
            "vision": { "wards": [], "smokes": [] }, "items": { "uses": [] }, "abilities": { "uses": [] },
            "timelines": { "couriers": [{ "id": "one", "team": "radiant", "samples": [{{sample}}] }] }
            """);
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadActivity(response));
    }

    [Fact]
    public void Metadata_PreservesEngineIdentityAndFractionalDuration_WithoutInventingLegacyValues()
    {
        var legacy = ReplayParserMappings.ReadMetadata(Read("\"match\": { \"durationSeconds\": 690.4 }"));
        Assert.Equal(690.4, legacy.DurationSeconds);
        Assert.Null(legacy.EngineBuild);
        Assert.Null(legacy.MatchEnded);
        var current = ReplayParserMappings.ReadMetadata(Read("""
            "match": { "durationSeconds": 690.4, "engineBuild": 6962, "producerVersion": "abc+modified", "matchEnded": true }
            """));
        Assert.Equal((6962u, "abc+modified", true), (current.EngineBuild, current.ProducerVersion, current.MatchEnded));
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadMetadata(Read("\"match\": { \"durationSeconds\": -1 }")));
    }

    private static ReplayParserMatchResponse Read(string sections) =>
        JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""{ "schemaVersion": 1, {{Players}}, {{sections}} }""",
            CoachingJson.Options)!;
}
