// MapExtractor: reads the Dota 2 map file (game/dota/maps/dota.vpk) and writes the compact map of one patch.
//
//   dotnet run --project tools/MapExtractor -- <path to dota.vpk> <patch, e.g. 7.41> <output .map.json.gz>
//
// What it writes (see work/agents/map-survey.md for how each part was found):
// - a grid of 64-unit cells (the map's own gridnav, maps/dota.gnv): the gridnav flag byte of each cell and the height of
//   the floor in it, from the collision mesh (maps/dota/world_physics.vmdl_c: the highest nearly-flat triangle at the
//   cell centre; cells with no floor are walls, cliffs or outside);
// - every tree (ent_dota_tree, all world layers), fog-of-war blocker and revealer node, lantern (Watcher), creep path
//   corner, movement-speed path (river currents) and objective entity with its position;
// - every trigger area that matters (no-ward zones, Roshan/Tormentor pits, neutral camp boxes, shops) as the box of its
//   collision model, placed at the entity's origin.
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using PhysMesh = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: MapExtractor <dota.vpk> <patch> <output .map.json.gz>");
    return 2;
}

var (vpkPath, patch, outputPath) = (args[0], args[1], args[2]);
using var package = new Package();
package.Read(vpkPath);

// The gridnav: a 32-byte header (magic, cell size, two floats, width, height, origin in cells) and one flag byte per
// cell, rows from the bottom (y) up. Flags seen in 7.41: 0x01 walkable, 0x10 cliff/blocked edge, 0x11 walkable special
// area (fountains, Roshan pits), 0x14 outside the map.
var gnv = Read(package, "maps/dota.gnv");
var cell = (int)BitConverter.ToSingle(gnv, 4);
int width = BitConverter.ToInt32(gnv, 16), height = BitConverter.ToInt32(gnv, 20);
int originX = BitConverter.ToInt32(gnv, 24) * cell, originY = BitConverter.ToInt32(gnv, 28) * cell;
var nav = gnv.AsSpan(32, width * height).ToArray();

var heights = FloorHeights(package, width, height, cell, originX, originY);

var entities = new List<KVObject>();
foreach (var entry in package.Entries!["vents_c"])
{
    using var resource = Resource(Read(package, entry.GetFullPath()));
    entities.AddRange(((EntityLump)resource.DataBlock!).GetEntities());
}

// A vector (origin) comes as an array of numbers: joined with spaces, like the game's own text form.
string? Text(KVObject entity, string key) => entity.TryGetValue(key, out var value)
    ? value.IsArray ? string.Join(' ', value.Values.Select(item => item.ToString(System.Globalization.CultureInfo.InvariantCulture))) : value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    : null;
Vector3 Origin(KVObject entity) => Text(entity, "origin") is { } text && text.Split(' ') is [var x, var y, var z]
    ? new Vector3(float.Parse(x, System.Globalization.CultureInfo.InvariantCulture), float.Parse(y, System.Globalization.CultureInfo.InvariantCulture),
        float.Parse(z, System.Globalization.CultureInfo.InvariantCulture))
    : Vector3.Zero;
object Point(KVObject entity) => new[] { (int)MathF.Round(Origin(entity).X), (int)MathF.Round(Origin(entity).Y), (int)MathF.Round(Origin(entity).Z) };
IEnumerable<KVObject> Of(string classname) => entities.Where(entity => Text(entity, "classname") == classname);
static string Name(string? targetName) => (targetName ?? "").Replace("[PR#]", "");

// The yaw (degrees) of an entity: "angles" is pitch, yaw, roll.
float Yaw(KVObject entity) => Text(entity, "angles") is { } text && text.Split(' ') is [_, var yaw, _]
    ? float.Parse(yaw, System.Globalization.CultureInfo.InvariantCulture)
    : 0f;

// The box of a trigger: "local" is the model's own box (around the origin, before turning) and the exact area is it turned
// by the entity's yaw (the pits are turned 182 and 342 degrees); min/max is the world box around that turned area.
object Area(KVObject entity)
{
    var origin = Origin(entity);
    var yaw = Yaw(entity);
    var (min, max) = Text(entity, "model") is { Length: > 0 } model ? ModelBounds(package, model) : (Vector3.Zero, Vector3.Zero);
    var turn = Matrix3x2.CreateRotation(yaw * MathF.PI / 180f);
    var corners = new[] { new Vector2(min.X, min.Y), new Vector2(max.X, min.Y), new Vector2(min.X, max.Y), new Vector2(max.X, max.Y) }
        .Select(corner => Vector2.Transform(corner, turn)).ToList();
    return new
    {
        name = Name(Text(entity, "targetname")),
        x = (int)MathF.Round(origin.X),
        y = (int)MathF.Round(origin.Y),
        yaw = (int)MathF.Round(yaw),
        local = new[] { (int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Ceiling(max.X), (int)MathF.Ceiling(max.Y) },
        min = new[] { (int)MathF.Floor(origin.X + corners.Min(c => c.X)), (int)MathF.Floor(origin.Y + corners.Min(c => c.Y)) },
        max = new[] { (int)MathF.Ceiling(origin.X + corners.Max(c => c.X)), (int)MathF.Ceiling(origin.Y + corners.Max(c => c.Y)) },
    };
}

var objectiveClasses = new[]
{
    "npc_dota_tower", "npc_dota_barracks", "npc_dota_fort", "npc_dota_building", "npc_dota_neutral_spawner", "ent_dota_shop",
    "ent_dota_fountain", "npc_dota_watch_tower", "npc_dota_unit_twin_gate", "npc_dota_lotus_pool", "npc_dota_xp_fountain",
    "dota_item_rune_spawner_bounty", "dota_item_rune_spawner_powerup", "ent_dota_neutral_item_stash", "info_player_start_goodguys",
    "info_player_start_badguys",
};

var map = new
{
    version = 1,
    patch,
    source = new
    {
        file = Path.GetFileName(vpkPath),
        modifiedUtc = File.GetLastWriteTimeUtc(vpkPath),
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(vpkPath))).ToLowerInvariant(),
    },
    grid = new
    {
        cell,
        width,
        height,
        originX,
        originY,
        // One byte per cell, rows from the bottom up (base64).
        nav = Convert.ToBase64String(nav),
        // Floor height of each cell, 16-bit little-endian, short.MinValue = no floor (base64).
        floor = Convert.ToBase64String(heights.SelectMany(value => BitConverter.GetBytes(value)).ToArray()),
    },
    trees = entities.Where(entity => Text(entity, "classname") == "ent_dota_tree").Select(Point).ToList(),
    fogBlockers = Of("ent_fow_blocker_node").Select(Point).ToList(),
    fogRevealers = Of("ent_fow_revealer").Select(Point).ToList(),
    lanterns = Of("npc_dota_lantern").Select(Point).ToList(),
    currents = Of("dota_movespeed_modifier_path").Select(Point).ToList(),
    creepPath = Of("path_corner").Select(entity => new { name = Name(Text(entity, "targetname")), next = Name(Text(entity, "target")), at = Point(entity) }).ToList(),
    noWardZones = Of("trigger_no_wards").Select(Area).ToList(),
    bossPits = Of("trigger_boss_attackable").Select(Area).ToList(),
    campBoxes = Of("trigger_multiple").Where(entity => Name(Text(entity, "targetname")).StartsWith("neutralcamp", StringComparison.Ordinal)).Select(Area).ToList(),
    shopAreas = Of("trigger_shop").Select(Area).ToList(),
    objectives = entities.Where(entity => objectiveClasses.Contains(Text(entity, "classname")))
        .Select(entity => new { kind = Text(entity, "classname"), name = Name(Text(entity, "targetname")), team = Text(entity, "teamnumber"), at = Point(entity) })
        .ToList(),
};

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
await using (var file = File.Create(outputPath))
await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
{
    await JsonSerializer.SerializeAsync(gzip, map);
}

Console.WriteLine($"{patch}: grid {width}x{height} of {cell} from ({originX}, {originY}); floor in {heights.Count(value => value != short.MinValue)} cells; " +
    $"{map.trees.Count} trees, {map.fogBlockers.Count} fog blockers, {map.noWardZones.Count} no-ward zones, {map.bossPits.Count} pits, " +
    $"{map.campBoxes.Count} camp boxes, {map.creepPath.Count} path corners, {map.objectives.Count} objectives -> {outputPath} ({new FileInfo(outputPath).Length / 1024} KB)");
return 0;

static byte[] Read(Package package, string path)
{
    var entry = package.FindEntry(path) ?? throw new FileNotFoundException($"{path} is not in the map file");
    package.ReadEntry(entry, out var bytes);
    return bytes;
}

static Resource Resource(byte[] bytes)
{
    var resource = new Resource();
    resource.Read(new MemoryStream(bytes));
    return resource;
}

// The floor of each cell: the highest nearly-flat triangle (|normal.z| >= 0.6) of the world collision mesh at the cell's
// centre. Walls and cliffs are steep, so a cell on one has no floor.
static short[] FloorHeights(Package package, int width, int height, int cell, int originX, int originY)
{
    using var resource = Resource(Read(package, "maps/dota/world_physics.vmdl_c"));
    var physics = resource.Blocks.OfType<PhysAggregateData>().Single();
    var floor = new float[width * height];
    Array.Fill(floor, float.NaN);
    // Only the "default" collision mesh: the others are conditionally solid (blockers that come and go).
    var mesh = (PhysMesh)physics.Parts[0].Shape.Meshes[0].Shape;
    var vertices = mesh.GetVertices().ToArray();
    foreach (var triangle in mesh.GetTriangles())
    {
        Vector3 a = vertices[triangle.X], b = vertices[triangle.Y], c = vertices[triangle.Z];
        if (MathF.Abs(Vector3.Normalize(Vector3.Cross(b - a, c - a)).Z) < 0.6f)
        {
            continue;
        }

        int x0 = Math.Max(0, (int)MathF.Floor((MathF.Min(a.X, MathF.Min(b.X, c.X)) - originX) / cell));
        int x1 = Math.Min(width - 1, (int)MathF.Floor((MathF.Max(a.X, MathF.Max(b.X, c.X)) - originX) / cell));
        int y0 = Math.Max(0, (int)MathF.Floor((MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - originY) / cell));
        int y1 = Math.Min(height - 1, (int)MathF.Floor((MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - originY) / cell));
        var area = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(area) < 1e-6f)
        {
            continue;
        }

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                float px = originX + (x + 0.5f) * cell, py = originY + (y + 0.5f) * cell;
                var l1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / area;
                var l2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / area;
                var l3 = 1 - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f)
                {
                    continue;
                }

                var z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                var index = y * width + x;
                if (float.IsNaN(floor[index]) || z > floor[index])
                {
                    floor[index] = z;
                }
            }
        }
    }

    return floor.Select(value => float.IsNaN(value) ? short.MinValue : (short)MathF.Round(value)).ToArray();
}

// The box of a brush entity's collision model (trigger areas), in the model's own space.
static (Vector3 Min, Vector3 Max) ModelBounds(Package package, string model)
{
    var path = model.EndsWith("_c", StringComparison.Ordinal) ? model : model + "_c";
    if (package.FindEntry(path) is null)
    {
        return (Vector3.Zero, Vector3.Zero);
    }

    using var resource = Resource(Read(package, path));
    // Hulls and meshes know their own box.
    var points = resource.Blocks.OfType<PhysAggregateData>()
        .SelectMany(physics => physics.Parts)
        .SelectMany(part => part.Shape.Hulls.SelectMany(hull => new[] { hull.Shape.Min, hull.Shape.Max })
            .Concat(part.Shape.Meshes.SelectMany(mesh => new[] { mesh.Shape.Min, mesh.Shape.Max })))
        .ToList();
    return points.Count == 0 ? (Vector3.Zero, Vector3.Zero)
        : (new Vector3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            new Vector3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
}
