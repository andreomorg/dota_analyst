using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class HeroCombatDetailsMappingTests
{
    private static ReplayParserMatchResponse Read(string extra) => JsonSerializer.Deserialize<ReplayParserMatchResponse>(
        "{\"players\":[{\"player\":0,\"heroId\":1}],\"timelines\":{\"positions\":[{\"player\":0,\"samples\":[[0,10,20]]" + extra + "}]}}", CoachingJson.Options)!;

    [Fact]
    public void LegacyRemainsUnknownAndComponentsPreservePrecision()
    {
        Assert.Null(ReplayParserMappings.ReadPositions(Read("")).Single().Samples.Single().CombatDetails);
        var samples = ReplayParserMappings.ReadPositions(Read(",\"combatDetails\":[[25,null,null,10,20,0,1.45,150,1,0.25,null,null]]")).Single().Samples;
        Assert.Equal(1.45, samples.Single().CombatDetails!.BaseAttackTime);
        Assert.Equal(0, samples.Single().CombatDetails!.DamageBonus);
    }

    [Theory]
    [InlineData(",\"combatDetails\":[]")]
    [InlineData(",\"combatDetails\":[[null]]")]
    [InlineData(",\"combatDetails\":[[null,null,null,null,null,null,null,null,1.2,null,null,null]]")]
    public void BrokenExtensionIsNotAcceptedAsAValidSnapshot(string extra) =>
        Assert.Throws<InvalidDataException>(() => ReplayParserMappings.ReadPositions(Read(extra)));
}
