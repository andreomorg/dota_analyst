using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Tests.Domain;

public sealed class GameBuildTests
{
    private static readonly DateTimeOffset Since = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);

    // Two 7.41 builds of dotabuff/d2vpkr: Client 6728 (25/03) and 6761 (07/04).
    private static readonly GameBuild First = new("bdc73e68", 6728, new(2026, 3, 25, 0, 0, 34, TimeSpan.Zero));
    private static readonly GameBuild Second = new("358719e0", 6761, new(2026, 4, 7, 20, 40, 19, TimeSpan.Zero));

    // 7.41b reached players on 07/04 07:00 UTC; its files (Second) were published 13.7 hours later.
    private static readonly GamePatch PatchB = new("7.41b", new(2026, 4, 7, 7, 0, 0, TimeSpan.Zero));

    [Fact]
    public void For_WithThePatch_TheFirstBuildOfItBeforeItsFilesArePublished()
    {
        var hoursAfterPatch = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(First, GameBuild.For([First, Second], hoursAfterPatch, Since));
        Assert.Equal(Second, GameBuild.For([First, Second], hoursAfterPatch, Since, PatchB));
        Assert.Equal(Second, GameBuild.For([First, Second], Second.ReleasedAt.AddDays(1), Since, PatchB));
        // A sub-patch without a build of its own yet keeps the last one.
        Assert.Equal(Second, GameBuild.For([First, Second], hoursAfterPatch.AddMonths(1), Since,
            new GamePatch("7.41c", hoursAfterPatch.AddMonths(1).AddHours(-1))));
    }

    [Fact]
    public void Match_GetsTheLastBuildReleasedBeforeItsStart()
    {
        Assert.Equal(First, GameBuild.For([Second, First], new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), Since));
        Assert.Equal(Second, GameBuild.For([Second, First], new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), Since));
        Assert.Equal(Second, GameBuild.For([First, Second], Second.ReleasedAt, Since));
    }

    [Fact]
    public void MatchBeforeTheFirstBuild_GetsItWhenInsideThePatch()
    {
        Assert.Equal(First, GameBuild.For([First, Second], new DateTimeOffset(2026, 3, 24, 18, 0, 0, TimeSpan.Zero), Since));
        Assert.Null(GameBuild.For([First, Second], new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero), Since));
    }

    [Fact]
    public void NoStartTimeOrNoBuilds_GetNone()
    {
        Assert.Null(GameBuild.For([First], null, Since));
        Assert.Null(GameBuild.For([], new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), Since));
    }
}
