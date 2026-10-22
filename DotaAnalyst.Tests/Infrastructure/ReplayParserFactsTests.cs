using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class ReplayParserFactsTests
{
    private static ReplayParserMatchResponse Read(string sections) => JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""
        { "players": [{ "player":0,"heroId":1 }, { "player":5,"heroId":6 }], {{sections}} }
        """, CoachingJson.Options)!;

    [Fact]
    public void Objectives_DeduplicateLegacy_AndPreserveUnknownAlive()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("""
            "objectives": { "outpostCaptures": [[10.2,123,"radiant"]], "outposts": [{"t":10.2,"team":"radiant","player":0}],
              "tormentorStates": [{"side":null,"x":10,"y":20,"samples":[[0,null],[2,1],[4,0]]}],
              "couriers": [{"t":20,"victim":5,"killer":null}] }
            """));
        Assert.Equal((123, 1), (Assert.Single(facts.Outposts!).EntityIndex, facts.Outposts![0].HeroId));
        Assert.Null(Assert.Single(facts.Tormentors!).Samples[0].Alive);
        Assert.Equal(6, Assert.Single(facts.CourierDeaths!).VictimHeroId);
        Assert.Empty(facts.InvalidFamilies);
    }

    [Fact]
    public void MalformedFamily_DoesNotEraseOtherFacts_AndAbsentIsUnknown()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("\"objectives\": {\"outpostCaptures\":[[0]],\"couriers\":[]}"));
        Assert.Contains("outposts", facts.InvalidFamilies);
        Assert.Null(facts.Outposts);
        Assert.Empty(facts.CourierDeaths!);
        Assert.Null(facts.Tormentors);
    }

    [Fact]
    public void Healing_RowsWithHeroesAndUnits_AndSelfHealOnce()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("""
            "combat": { "healing": [
              {"sourcePlayer":0,"sourceKind":"hero","targetPlayer":0,"targetKind":"hero","inflictor":"item_magic_wand","damageType":null,"amount":120,"hits":1},
              {"sourcePlayer":0,"sourceKind":"hero","targetPlayer":null,"targetKind":"laneCreeps","inflictor":null,"damageType":null,"amount":30,"hits":2},
              {"sourcePlayer":null,"sourceKind":"buildings","targetPlayer":5,"targetKind":"hero","inflictor":null,"damageType":null,"amount":400,"hits":40}] }
            """));

        Assert.Equal(3, facts.Healing!.Count);
        Assert.True(facts.Healing[0].SelfHeal);
        Assert.Equal((1, (int?)null), (facts.Healing[0].SourceHeroId!.Value, facts.Healing[1].TargetHeroId));
        Assert.Equal(6, facts.Healing[2].TargetHeroId);
        Assert.Empty(facts.InvalidFamilies);
    }

    [Theory]
    [InlineData("\"combat\": { \"healing\": [{\"sourcePlayer\":0,\"sourceKind\":\"hero\",\"targetKind\":\"hero\",\"amount\":-1}] }")]
    [InlineData("\"combat\": { \"healing\": [{\"sourcePlayer\":7,\"sourceKind\":\"hero\",\"targetKind\":\"hero\",\"amount\":10}] }")]
    [InlineData("\"combat\": { \"healing\": {} }")]
    public void Healing_Malformed_IsInvalid_NotEmpty(string sections)
    {
        var facts = ReplayParserMappings.ReadFacts(Read(sections));

        Assert.Null(facts.Healing);
        Assert.Contains("healing", facts.InvalidFamilies);
    }

    private const string Final = """
        {"level":25,"kills":10,"deaths":3,"assists":12,"lastHits":300,"denies":9,"missedLastHits":40,"netWorth":30000,
         "goldEarned":28000,"xp":40000,"heroDamage":35000,"towerDamage":8000,"healing":0,"stunSeconds":12.5,
         "teamFightParticipation":0.71,"campsStacked":2,"observersPlaced":1,"sentriesPlaced":0,"wardsDestroyed":3,
         "goldLostToDeath":900,"gold":1200,"goldBySource":{},"goldSpent":{}}
        """;

    private static ReplayParserMatchResponse WithFinals(string first, string second, string items = "") =>
        JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""
            { "players": [{ "player":0,"heroId":1,"final":{{first}} }, { "player":5,"heroId":6,"final":{{second}} }] {{items}} }
            """, CoachingJson.Options)!;

    [Fact]
    public void FinalStats_AndItems_ForEveryHero()
    {
        var facts = ReplayParserMappings.ReadFacts(WithFinals(Final, Final, """
            , "items": { "final": [{"player":0,"inventory":["item_falcon_blade",null,null,null,null,"item_magic_wand"],
              "backpack":["item_recipe_mjollnir",null,null],"teleport":"item_tpscroll","neutral":null,"enhancement":null}] }
            """));

        Assert.Equal(2, facts.FinalStats!.Count);
        Assert.Equal((1, 10, 12.5), (facts.FinalStats[0].HeroId, facts.FinalStats[0].Kills, facts.FinalStats[0].StunSeconds));
        var items = Assert.Single(facts.FinalItems!);
        Assert.Equal((1, "item_falcon_blade", (string?)null), (items.HeroId, items.Inventory[0], items.Inventory[1]));
        Assert.Equal("item_recipe_mjollnir", items.Backpack[0]);
        Assert.Null(items.Neutral);
        Assert.Empty(facts.InvalidFamilies);
    }

    [Fact]
    public void FinalStats_OnePlayerWithoutIt_IsInvalid_NoneIsUnknown()
    {
        var partial = ReplayParserMappings.ReadFacts(WithFinals(Final, "null"));
        Assert.Null(partial.FinalStats);
        Assert.Contains("finalStats", partial.InvalidFamilies);

        var none = ReplayParserMappings.ReadFacts(WithFinals("null", "null"));
        Assert.Null(none.FinalStats);
        Assert.DoesNotContain("finalStats", none.InvalidFamilies);
    }

    [Fact]
    public void FinalItems_WrongSlotCount_IsInvalid()
    {
        var facts = ReplayParserMappings.ReadFacts(WithFinals(Final, Final, """
            , "items": { "final": [{"player":0,"inventory":["item_falcon_blade"],"backpack":[null,null,null]}] }
            """));

        Assert.Null(facts.FinalItems);
        Assert.Contains("finalItems", facts.InvalidFamilies);
    }

    [Fact]
    public void ChatWheel_PhraseIdsOnly_InTimeOrder()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("\"vision\": { \"wards\": [], \"chatWheel\": [[30.5,5,71001],[12,0,7]] }"));

        Assert.Equal(new[] { (1, 7u), (6, 71001u) }, facts.ChatWheel!.Select(use => (use.HeroId, use.Phrase)));
    }

    [Theory]
    [InlineData("[[1,0]]")]
    [InlineData("[[1,0,-3]]")]
    [InlineData("[[1,9,3]]")]
    public void ChatWheel_Malformed_IsInvalid(string chatWheel)
    {
        var facts = ReplayParserMappings.ReadFacts(Read("\"vision\": { \"wards\": [], \"chatWheel\": " + chatWheel + " }"));

        Assert.Null(facts.ChatWheel);
        Assert.Contains("chatWheel", facts.InvalidFamilies);
    }

    [Fact]
    public void MapState_FourFamilies_FromTheGoContract()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("""
            "timelines": { "perMinute": [], "positions": [], "tempTrees": [[61.3,-619,-770,true],[63.8,-619,-770,false]] },
            "objectives": { "bossTracks": [{"kind":"roshan","samples":[[0,-3194,2395,true],[600,-3194,2395,false]]}] },
            "vision": { "wards": [], "lanterns": [[8,5,1087,-4064],[9,0,null,null]] },
            "combat": { "goldEvents": [[-90,0,600,0,-6700,-6700],[300,5,-150,null,null,null]] }
            """));

        Assert.Equal(2, facts.TempTrees!.Count);
        Assert.False(facts.TempTrees[1].Present);
        Assert.Equal("roshan", Assert.Single(facts.BossTracks!).Kind);
        Assert.False(facts.BossTracks![0].Samples[1].Alive);
        Assert.Equal((6, (int?)1087), (facts.Lanterns![0].HeroId, facts.Lanterns[0].X));
        Assert.Null(facts.Lanterns[1].X);
        Assert.Equal((600L, (uint?)0), (facts.GoldEvents![0].Amount, facts.GoldEvents[0].Reason));
        Assert.Equal((-150L, (uint?)null), (facts.GoldEvents[1].Amount, facts.GoldEvents[1].Reason));
        Assert.Empty(facts.InvalidFamilies);
    }

    [Theory]
    [InlineData("\"timelines\": { \"perMinute\": [], \"positions\": [], \"tempTrees\": [[1,2,3]] }", "tempTrees")]
    [InlineData("\"objectives\": { \"bossTracks\": [{\"samples\":[]}] }", "bossTracks")]
    [InlineData("\"vision\": { \"wards\": [], \"lanterns\": [[1,9,0,0]] }", "lanterns")]
    [InlineData("\"combat\": { \"goldEvents\": [[1,0,1.5,0,0,0]] }", "goldEvents")]
    public void MapState_Malformed_IsInvalid(string sections, string family)
    {
        var facts = ReplayParserMappings.ReadFacts(Read(sections));

        Assert.Contains(family, facts.InvalidFamilies);
    }

    [Fact]
    public void Stolen_PeriodsAndCooldowns_OfTheCarrier()
    {
        var facts = ReplayParserMappings.ReadFacts(Read("""
            "abilities": { "upgrades": [], "stolen": [
              {"player":0,"abilities":[[576,606,"antimage_blink",252,402,2,false],[2700,null,"furion_sprout",3012,867,2,false]],
               "cooldowns":[[580,"antimage_blink",252,402,592]]},
              {"player":5,"abilities":[],"cooldowns":[]}] }
            """));

        Assert.Equal(2, facts.StolenAbilities!.Count);
        Assert.All(facts.StolenAbilities, ability => Assert.Equal(1, ability.HeroId));     // the carrier
        Assert.True(facts.StolenAbilities[0].HeldAt(600));
        Assert.False(facts.StolenAbilities[0].HeldAt(606));
        Assert.True(facts.StolenAbilities[1].HeldAt(9999));                               // until the end
        Assert.Equal(("antimage_blink", 592.0), (Assert.Single(facts.StolenCooldowns!).Name, facts.StolenCooldowns![0].ReadyAt));
    }

    [Fact]
    public void Stolen_AbsentIsUnknown_MalformedIsInvalid()
    {
        Assert.Null(ReplayParserMappings.ReadFacts(Read("\"abilities\": { \"upgrades\": [] }")).StolenAbilities);

        var facts = ReplayParserMappings.ReadFacts(Read("\"abilities\": { \"upgrades\": [], \"stolen\": [{\"player\":0,\"abilities\":[[1,2,\"x\"]],\"cooldowns\":[]}] }"));
        Assert.Null(facts.StolenAbilities);
        Assert.Contains("stolenAbilities", facts.InvalidFamilies);
    }

    [Fact]
    public void Healing_AbsentIsUnknown()
    {
        Assert.Null(ReplayParserMappings.ReadFacts(Read("\"combat\": { }")).Healing);
    }

    [Theory]
    [InlineData("\"outposts\":[null]", "outposts")]
    [InlineData("\"tormentorStates\":[null]", "tormentors")]
    [InlineData("\"tormentorStates\":[{\"samples\":[]}]", "tormentors")]
    [InlineData("\"tormentorStates\":[{\"x\":1,\"y\":2,\"samples\":{}}]", "tormentors")]
    public void InvalidObjectiveEntry_IsIsolated(string malformed, string family)
    {
        var facts = ReplayParserMappings.ReadFacts(Read("\"objectives\": { " + malformed + ", \"couriers\": [] }"));
        Assert.Contains(family, facts.InvalidFamilies);
        Assert.Empty(facts.CourierDeaths!);
    }
}
