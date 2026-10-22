using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Tests.Domain;

public class VisionModelTests
{
    // A 20 x 3 strip of 64-unit cells from (0, 0): low ground (128), with high ground (256) from column 15 on.
    private static GameMap Map(IReadOnlyList<MapPoint>? trees = null, IReadOnlyList<MapPoint>? fogBlockers = null)
    {
        const int width = 20, height = 3;
        var floor = new short[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                floor[row * width + column] = (short)(column >= 15 ? 256 : 128);
            }
        }

        return new GameMap("7.41", new MapGrid(64, width, height, 0, 0, Enumerable.Repeat((byte)0x01, width * height).ToArray(), floor),
            trees ?? [], fogBlockers ?? [], [], [], [], [], [], []);
    }

    private static readonly VisionSource Hero = new(32, 96, 800);

    [Fact]
    public void Radius_FarIsNotSeen()
    {
        Assert.True(VisionModel.Sees(Map(), Hero, 600, 96));
        Assert.False(VisionModel.Sees(Map(), Hero with { Radius = 500 }, 600, 96));
    }

    [Fact]
    public void Terrain_LowGroundDoesNotSeeHighGround_HighGroundSeesLow()
    {
        var low = new VisionSource(12 * 64 + 32, 96, 800);
        var high = new VisionSource(17 * 64 + 32, 96, 800);
        Assert.False(VisionModel.Sees(Map(), low, 16 * 64 + 32, 96));
        Assert.True(VisionModel.Sees(Map(), high, 12 * 64 + 32, 96));
        Assert.True(VisionModel.Sees(Map(), low, 16 * 64 + 32, 96, rules: VisionRules.RadiusOnly));
    }

    [Fact]
    public void Trees_BlockUntilCut()
    {
        // A tree on the corner at (320, 128): it covers columns 4-5 of rows 1-2.
        var map = Map(trees: [new MapPoint(320, 128)]);

        Assert.False(VisionModel.Sees(map, Hero, 600, 96));
        Assert.True(VisionModel.Sees(map, Hero, 600, 96, new HashSet<int> { 0 }));
        Assert.True(VisionModel.Sees(map, Hero with { Flying = true }, 600, 96));
        Assert.True(VisionModel.Sees(map, Hero, 600, 96, rules: new VisionRules(Trees: false)));
    }

    [Fact]
    public void FogBlockers_Block()
    {
        var map = Map(fogBlockers: [new MapPoint(300, 96)]);

        Assert.False(VisionModel.Sees(map, Hero, 600, 96));
        Assert.True(VisionModel.Sees(map, Hero, 600, 96, rules: new VisionRules(FogBlockers: false)));
    }

    [Fact]
    public void ObstacleInTheTargetsOwnCellDoesNotHideIt()
    {
        // The tree covers columns 8-9; the target stands in column 8, the last cell on the way.
        var map = Map(trees: [new MapPoint(576, 128)]);

        Assert.True(VisionModel.Sees(map, Hero, 544, 96));
    }
}
