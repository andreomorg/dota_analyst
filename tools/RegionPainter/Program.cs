// RegionPainter: paints the terrain zones of a patch's map and compares them with the old regions.
//
//   dotnet run --project tools/RegionPainter -- <map .map.json.gz> <output .png> [match document .json ...]
//
// The picture: the relief (darker = lower) with each zone tinted (bases blue/red, lanes yellow, river cyan, Radiant
// jungle green, Dire jungle purple, pits orange), narrow ground (openness < 0.5) hatched dark, objectives as white
// rings. With match documents (POST /v1/match or cmd/inspect -match), it also prints how the heroes' positions fall in
// the old regions (MapRegions, seen from Radiant) against the new zones.
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.GameData;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: RegionPainter <map .map.json.gz> <output .png> [match document .json ...]");
    return 2;
}

GameMap map;
using (var file = File.OpenRead(args[0]))
{
    map = GameMapFile.Read(file);
}

const int pixels = 1024;
const double from = -10240, to = 10240; // the grid's x range; y uses the same square
double Scale(double v) => (v - from) / (to - from) * pixels;

using var image = new Bitmap(pixels, pixels);
for (var px = 0; px < pixels; px++)
{
    for (var py = 0; py < pixels; py++)
    {
        var x = from + (px + 0.5) / pixels * (to - from);
        var y = to - (py + 0.5) / pixels * (to - from);
        var floor = map.FloorAt(x, y);
        if (floor is null)
        {
            image.SetPixel(px, py, Color.FromArgb(30, 30, 30));
            continue;
        }

        var shade = Math.Clamp(60 + (floor.Value + 256) / 4, 40, 230);
        var zone = TerrainZones.Classify(map, x, y);
        var tint = zone.Area switch
        {
            TerrainArea.Base => zone.Side == Team.Radiant ? Color.FromArgb(60, 120, 255) : Color.FromArgb(255, 70, 70),
            TerrainArea.Lane => Color.FromArgb(240, 210, 60),
            TerrainArea.River => Color.FromArgb(60, 220, 240),
            TerrainArea.RoshanPit or TerrainArea.TormentorPit => Color.FromArgb(255, 140, 0),
            _ => zone.Side == Team.Radiant ? Color.FromArgb(70, 200, 90) : Color.FromArgb(170, 90, 220),
        };
        double mix = 0.45;
        var color = Color.FromArgb(
            (int)(shade * (1 - mix) + tint.R * mix), (int)(shade * (1 - mix) + tint.G * mix), (int)(shade * (1 - mix) + tint.B * mix));
        // Narrow ground: dark hatching.
        if (zone.Openness < 0.5 && (px + py) % 4 == 0)
        {
            color = Color.FromArgb(20, 20, 20);
        }

        image.SetPixel(px, py, color);
    }
}

using (var graphics = Graphics.FromImage(image))
using (var pen = new Pen(Color.White, 2))
{
    foreach (var landmark in map.Landmarks.Where(landmark => landmark.Kind is LandmarkKind.LotusPool or LandmarkKind.WisdomShrine
                 or LandmarkKind.Outpost or LandmarkKind.TwinGate or LandmarkKind.Shop or LandmarkKind.BountyRune
                 or LandmarkKind.PowerRune or LandmarkKind.Lantern))
    {
        var r = TerrainZones.ObjectiveRadius / (to - from) * pixels;
        graphics.DrawEllipse(pen, (float)(Scale(landmark.X) - r), (float)(pixels - Scale(landmark.Y) - r), (float)(2 * r), (float)(2 * r));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
image.Save(args[1], ImageFormat.Png);
Console.WriteLine($"{args[1]}: zones of {map.Patch}");

// Old regions against the new zones, on the heroes' real positions.
var table = new Dictionary<(string Old, string New), int>();
foreach (var path in args.Skip(2))
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var player in document.RootElement.GetProperty("timelines").GetProperty("positions").EnumerateArray())
    {
        foreach (var sample in player.GetProperty("samples").EnumerateArray())
        {
            int x = sample[1].GetInt32(), y = sample[2].GetInt32();
            var old = MapRegions.RegionOf(Team.Radiant, x, y) switch
            {
                MapRegion.OwnBase or MapRegion.EnemyBase => "base",
                MapRegion.TopLane => "lane top",
                MapRegion.MiddleLane => "lane middle",
                MapRegion.BottomLane => "lane bottom",
                MapRegion.River => "river",
                _ => "jungle",
            };
            var zone = TerrainZones.Classify(map, x, y);
            var now = zone.Area switch
            {
                TerrainArea.Lane => "lane " + zone.Lane!.Value.ToString().ToLowerInvariant(),
                TerrainArea.RoshanPit or TerrainArea.TormentorPit => "pit",
                _ => zone.Area.ToString().ToLowerInvariant(),
            };
            table[(old, now)] = table.GetValueOrDefault((old, now)) + 1;
        }
    }
}

// Lane width: in the laning phase (1:00-10:00), the share of each laner's positions within the half width of their own
// lane (laning.lanes of the document: the lane where the hero spent most of that time).
var widths = new[] { 400, 500, 600, 700, 800, 1000 };
var inLane = new int[widths.Length];
var laningSamples = 0;
foreach (var path in args.Skip(2))
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var lanes = document.RootElement.GetProperty("laning").GetProperty("lanes").EnumerateArray()
        .ToDictionary(lane => lane.GetProperty("player").GetInt32(), lane => lane.GetProperty("lane").GetString());
    foreach (var player in document.RootElement.GetProperty("timelines").GetProperty("positions").EnumerateArray())
    {
        var lane = lanes.GetValueOrDefault(player.GetProperty("player").GetInt32()) switch
        {
            "top" => DotaAnalyst.Domain.Replays.Lane.Top,
            "middle" => DotaAnalyst.Domain.Replays.Lane.Middle,
            "bottom" => DotaAnalyst.Domain.Replays.Lane.Bottom,
            _ => (DotaAnalyst.Domain.Replays.Lane?)null,
        };
        if (lane is null)
        {
            continue;
        }

        foreach (var sample in player.GetProperty("samples").EnumerateArray().Where(sample => sample[0].GetInt32() is >= 60 and <= 600))
        {
            int x = sample[1].GetInt32(), y = sample[2].GetInt32();
            if (map.BaseAt(x, y) is not null)
            {
                continue;
            }

            laningSamples++;
            var distance = map.CreepPaths.Where(creeps => creeps.Lane == lane).Min(creeps => creeps.DistanceTo(x, y));
            for (var i = 0; i < widths.Length; i++)
            {
                if (distance <= widths[i])
                {
                    inLane[i]++;
                }
            }
        }
    }
}

if (laningSamples > 0)
{
    Console.WriteLine($"laning phase (1:00-10:00, outside the bases): {laningSamples} positions of laners");
    for (var i = 0; i < widths.Length; i++)
    {
        Console.WriteLine($"  within {widths[i],4} of their own lane's creep path: {100.0 * inLane[i] / laningSamples:F1}%");
    }
}

// Openness and trees around every position (and every death) of the documents: to calibrate the thresholds.
{
    var openness = new List<double>();
    var trees = new List<int>();
    var deathOpenness = new List<double>();
    foreach (var path in args.Skip(2))
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var player in document.RootElement.GetProperty("timelines").GetProperty("positions").EnumerateArray())
        {
            foreach (var sample in player.GetProperty("samples").EnumerateArray().Where((_, i) => i % 3 == 0))
            {
                var zone = TerrainZones.Classify(map, sample[1].GetInt32(), sample[2].GetInt32());
                openness.Add(zone.Openness);
                trees.Add(zone.TreesNearby);
            }
        }

        foreach (var kill in document.RootElement.GetProperty("kills").EnumerateArray())
        {
            deathOpenness.Add(TerrainZones.Classify(map, kill.GetProperty("x").GetInt32(), kill.GetProperty("y").GetInt32()).Openness);
        }
    }

    if (openness.Count > 0)
    {
        openness.Sort();
        trees.Sort();
        deathOpenness.Sort();
        string Q<T>(List<T> v) => string.Join("  ", new[] { 0.05, 0.1, 0.25, 0.5 }.Select(p => $"p{p * 100:0}={v[(int)(p * (v.Count - 1))]:0.##}"));
        Console.WriteLine($"\nopenness of positions: {Q(openness)}");
        Console.WriteLine($"openness of deaths:    {Q(deathOpenness)}");
        trees.Reverse();
        Console.WriteLine($"trees within {TerrainZones.TreeRadius} (from the most): {Q(trees)}");
    }
}

// Terrain metrics (MP8) of every hero of each document, to check they make sense.
foreach (var path in args.Skip(2))
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;
    var teams = root.GetProperty("players").EnumerateArray().ToDictionary(
        player => player.GetProperty("player").GetInt32(),
        player => (Team: player.GetProperty("team").GetString() == "radiant" ? Team.Radiant : Team.Dire,
            Hero: player.GetProperty("heroName").GetString()!.Replace("npc_dota_hero_", "")));
    Console.WriteLine($"\nterrain of {Path.GetFileName(path)}: own high / enemy high / river / narrow / deep enemy jungle (time) | deaths on enemy high / below it / narrow / deep jungle (of all)");
    foreach (var player in root.GetProperty("timelines").GetProperty("positions").EnumerateArray())
    {
        var index = player.GetProperty("player").GetInt32();
        var (team, hero) = teams[index];
        var samples = player.GetProperty("samples").EnumerateArray()
            .Select(sample => new DotaAnalyst.Domain.Replays.PositionSample(sample[0].GetInt32(), sample[1].GetInt32(), sample[2].GetInt32())).ToList();
        var deaths = root.GetProperty("kills").EnumerateArray().Where(kill => kill.GetProperty("victim").GetInt32() == index)
            .Select(kill => new DotaAnalyst.Domain.Replays.HeroDeath(1, (int)kill.GetProperty("t").GetDouble(),
                kill.GetProperty("x").GetInt32(), kill.GetProperty("y").GetInt32(), DotaAnalyst.Domain.Replays.KillerKind.Other, null, false)).ToList();
        var t = DotaAnalyst.Domain.Analysis.TerrainMetricsCalculator.Calculate(map, team, samples, deaths);
        Console.WriteLine($"  {hero,-16} {t.OwnHighGroundShare,5:P0} {t.EnemyHighGroundShare,5:P0} {t.RiverLevelShare,5:P0} {t.NarrowGroundShare,5:P0} {t.DeepEnemyJungleShare,5:P0}"
            + $" | {t.DeathsOnEnemyHighGround}/{t.DeathsBelowEnemyHighGround}/{t.DeathsInNarrowGround}/{t.DeathsInDeepEnemyJungle} of {deaths.Count}");
    }
}

if (table.Count > 0)
{
    var total = table.Values.Sum();
    var same = table.Where(pair => pair.Key.Old == pair.Key.New).Sum(pair => pair.Value);
    Console.WriteLine($"positions: {total}, same region: {same} ({100.0 * same / total:F1}%)");
    foreach (var ((old, now), count) in table.OrderByDescending(pair => pair.Value))
    {
        Console.WriteLine($"  old {old,-12} -> new {now,-12} {count,6} ({100.0 * count / total:F1}%)");
    }
}

return 0;
