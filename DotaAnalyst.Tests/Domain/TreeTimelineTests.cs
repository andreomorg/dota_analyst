using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class TreeTimelineTests
{
    [Fact]
    public void DownAt_StartThenCutsAndRegrowth()
    {
        var trees = new TreeTimeline([3], [new TreeChange(120, 10, false), new TreeChange(60, 3, true), new TreeChange(400, 10, true)]);

        Assert.Equal(new[] { 3 }, trees.DownAt(0).Order());
        Assert.Empty(trees.DownAt(60));
        Assert.Equal(new[] { 10 }, trees.DownAt(120).Order());
        Assert.Empty(trees.DownAt(400));
    }

    [Fact]
    public void Indexes_AreNotNegative()
    {
        Assert.Throws<ArgumentException>(() => new TreeTimeline([-1], []));
    }
}
