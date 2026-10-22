using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Tests.Domain;

// Times from Valve's patch notes: 7.41 on 24/03/2026 07:00 UTC, 7.41a on 27/03, 7.41b on 07/04.
public sealed class GamePatchTests
{
    private static readonly GamePatch Base = new("7.41", DateTimeOffset.FromUnixTimeSeconds(1774335600));
    private static readonly GamePatch A = new("7.41a", DateTimeOffset.FromUnixTimeSeconds(1774594800));
    private static readonly GamePatch B = new("7.41b", DateTimeOffset.FromUnixTimeSeconds(1775545200));
    private static readonly GamePatch[] Patches = [B, Base, A];

    [Fact]
    public void For_TheLastPatchReleasedBeforeTheHorn()
    {
        Assert.Equal(A, GamePatch.For(Patches, A.ReleasedAt.AddDays(2)));
        Assert.Equal(B, GamePatch.For(Patches, B.ReleasedAt));
        Assert.Equal(Base, GamePatch.For(Patches, Base.ReleasedAt.AddHours(1)));
        Assert.Null(GamePatch.For(Patches, Base.ReleasedAt.AddSeconds(-1)));
        Assert.Null(GamePatch.For(Patches, null));
    }

    [Theory]
    [InlineData("7.41c", "7.41")]
    [InlineData("7.41", "7.41")]
    [InlineData("7.07dd", "7.07")]
    public void Major_WithoutTheLetter(string name, string expected)
    {
        Assert.Equal(expected, new GamePatch(name, DateTimeOffset.UnixEpoch).Major);
    }

    [Fact]
    public void BuildBelongsTo_ReleasedDuringThePatch()
    {
        var duringA = new GameBuild("a1", 6730, A.ReleasedAt.AddHours(5));
        var beforeA = new GameBuild("base1", 6728, A.ReleasedAt.AddHours(-5));
        var afterLast = new GameBuild("b9", 6800, B.ReleasedAt.AddDays(30));

        Assert.True(GamePatch.BuildBelongsTo(duringA, A, Patches));
        Assert.False(GamePatch.BuildBelongsTo(beforeA, A, Patches));
        Assert.False(GamePatch.BuildBelongsTo(afterLast, A, Patches));
        Assert.True(GamePatch.BuildBelongsTo(afterLast, B, Patches));
    }
}
