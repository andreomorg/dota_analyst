using System.Text.Json;
using System.Text.Json.Nodes;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Matches;

namespace DotaAnalyst.Tests.Infrastructure;

public class CoachingPatchPersistenceTests
{
    [Theory]
    [InlineData(60, 60)]
    [InlineData(0, 0)]
    [InlineData(null, null)]
    [InlineData(-1, null)]
    public void SourcePatchSurvivesStorageWithoutDefaultingToCurrent(int? patch, int? expected)
    {
        var source = new OpenDotaMatch(1, 1200, 0, true, 1, 0, 1, [], Patch: patch);
        var match = source.ToMatchDetails();
        Assert.Equal(expected, match.PatchId);
        var json = StoredMatchMappings.ToJson(match);
        var restored = StoredMatchMappings.FromJson(json);
        Assert.Equal(expected, restored.PatchId);
    }

    [Fact]
    public void LegacyStoredMatchDoesNotAcquireAPatch()
    {
        var match = new OpenDotaMatch(1, 1200, 0, true, 1, 0, 1, [], Patch: 60).ToMatchDetails();
        var node = JsonNode.Parse(StoredMatchMappings.ToJson(match))!;
        node.AsObject().Remove("patchId");
        Assert.Null(StoredMatchMappings.FromJson(node.ToJsonString()).PatchId);
    }
}
