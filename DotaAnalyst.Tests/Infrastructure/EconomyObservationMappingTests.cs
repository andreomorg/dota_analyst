using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class EconomyObservationMappingTests
{
    private static DotaAnalyst.Domain.Replays.ReplayAdditionalFacts Read(string timelines) => ReplayParserMappings.ReadFacts(
        JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""
            {"players":[{"player":0,"heroId":1}],"timelines":{{timelines}}}
            """, CoachingJson.Options)!);
    [Fact]
    public void ReadsLiveResourcesCounterAndStateWithoutInferringFinalPrice()
    {
        var facts = Read("""
            {"economyStates":[{"t":10,"player":0,"netWorth":13000,"reliable":100,"unreliable":40,"cooldownRemaining":5.2,"customCost":0}],
             "goldLossChanges":[{"t":10.1,"player":0,"before":100,"after":140,"beforeUnreliable":40,"afterUnreliable":0,"previousAt":10}],
             "farmSnapshots":[{"t":10,"units":[["100:4","npc_dota_neutral",4,300,0,100,30,40,1,null]]}]}
            """);
        Assert.Empty(facts.InvalidFamilies);
        Assert.Equal(1, Assert.Single(facts.EconomyStates!).HeroId);
        Assert.Equal(40, Assert.Single(facts.GoldLossChanges!).Lost);
        var unit = Assert.Single(Assert.Single(facts.FarmSnapshots!).Units);
        Assert.True(unit.SeenRadiant); Assert.Null(unit.SeenDire);
        Assert.Equal(30, unit.BaseBountyMin); Assert.Equal(40, unit.BaseBountyMax);
    }
    [Theory]
    [InlineData("{\"farmSnapshots\":[{\"t\":10,\"units\":[[0]]}]}", "farmSnapshots")]
    [InlineData("{\"goldLossChanges\":[{\"t\":10,\"player\":0,\"before\":40,\"after\":30,\"beforeUnreliable\":0,\"afterUnreliable\":0,\"previousAt\":9}]}", "goldLossChanges")]
    [InlineData("{\"economyStates\":[{\"t\":10}]}", "economyStates")]
    public void MalformedFamilyIsUnavailableInsteadOfEmpty(string json, string family) => Assert.Contains(family, Read(json).InvalidFamilies);
    [Fact]
    public void MissingOldDocumentIsDistinctFromPresentEmptyCounter()
    {
        Assert.Null(Read("{}").GoldLossChanges);
        Assert.Empty(Read("{\"goldLossChanges\":[]}").GoldLossChanges!);
    }
}
