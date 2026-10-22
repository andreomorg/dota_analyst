using System.IO.Compression;
using System.Text.Json;
using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.GameData;

// Reads the compact map of a patch written by tools/MapExtractor (GameData/Maps/dota-<patch>.map.json.gz, embedded in
// this assembly) and turns it into the Domain's GameMap. The file's format stays here: the Domain only sees GameMap.
public static class GameMapFile
{
    // The map file's own format (version 1).
    private const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The map's team numbers.
    private const string RadiantTeam = "2";
    private const string DireTeam = "3";

    private static readonly Dictionary<string, LandmarkKind> LandmarkKinds = new(StringComparer.Ordinal)
    {
        ["npc_dota_tower"] = LandmarkKind.Tower,
        ["npc_dota_barracks"] = LandmarkKind.Barracks,
        ["npc_dota_fort"] = LandmarkKind.Ancient,
        ["npc_dota_building"] = LandmarkKind.Building,
        ["npc_dota_neutral_spawner"] = LandmarkKind.NeutralCamp,
        ["ent_dota_shop"] = LandmarkKind.Shop,
        ["ent_dota_fountain"] = LandmarkKind.Fountain,
        ["npc_dota_watch_tower"] = LandmarkKind.Outpost,
        ["npc_dota_unit_twin_gate"] = LandmarkKind.TwinGate,
        ["npc_dota_lotus_pool"] = LandmarkKind.LotusPool,
        ["npc_dota_xp_fountain"] = LandmarkKind.WisdomShrine,
        ["dota_item_rune_spawner_bounty"] = LandmarkKind.BountyRune,
        ["dota_item_rune_spawner_powerup"] = LandmarkKind.PowerRune,
        ["ent_dota_neutral_item_stash"] = LandmarkKind.NeutralStash,
        ["info_player_start_goodguys"] = LandmarkKind.HeroSpawn,
        ["info_player_start_badguys"] = LandmarkKind.HeroSpawn,
    };

    // Throws InvalidDataException when the file is not a map this code can read.
    public static GameMap Read(Stream compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        StoredMap? stored;
        try
        {
            using var json = new GZipStream(compressed, CompressionMode.Decompress);
            stored = JsonSerializer.Deserialize<StoredMap>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The map file is not valid JSON.", ex);
        }

        if (stored is not { Version: Version, Patch: { Length: > 0 } patch, Grid: { } grid })
        {
            throw new InvalidDataException("The map file has an unknown version or no patch or grid.");
        }

        try
        {
            var floorBytes = Convert.FromBase64String(grid.Floor);
            var floor = new short[floorBytes.Length / 2];
            Buffer.BlockCopy(floorBytes, 0, floor, 0, floor.Length * 2);
            var mapGrid = new MapGrid(grid.Cell, grid.Width, grid.Height, grid.OriginX, grid.OriginY,
                Convert.FromBase64String(grid.Nav), floor);

            var landmarks = stored.Objectives
                .Where(objective => LandmarkKinds.ContainsKey(objective.Kind))
                .Select(objective => new MapLandmark(LandmarkKinds[objective.Kind], objective.Name, TeamOf(objective), objective.At[0], objective.At[1]))
                .Concat(stored.Lanterns.Select(at => new MapLandmark(LandmarkKind.Lantern, "", null, at[0], at[1])))
                .ToList();

            return new GameMap(patch, mapGrid, Points(stored.Trees), Points(stored.FogBlockers), Areas(stored.NoWardZones),
                Areas(stored.BossPits), Areas(stored.CampBoxes), Areas(stored.ShopAreas), CreepPaths(stored.CreepPath), landmarks,
                Points(stored.FogRevealers ?? []), Points(stored.Currents ?? []),
                stored.Source is { File: { Length: > 0 } file, Sha256: { Length: > 0 } sha } source
                    ? new MapSource(file, source.ModifiedUtc, sha) : null);
        }
        // A part missing from the JSON (null) or a short array is as unreadable as bad base64.
        catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException or NullReferenceException)
        {
            throw new InvalidDataException("The map file has an unreadable part.", ex);
        }
    }

    private static Team? TeamOf(StoredObjective objective) => objective.Kind switch
    {
        "info_player_start_goodguys" => Team.Radiant,
        "info_player_start_badguys" => Team.Dire,
        _ => objective.Team switch { RadiantTeam => Team.Radiant, DireTeam => Team.Dire, _ => null },
    };

    private static List<MapPoint> Points(IEnumerable<int[]> points) => points.Select(at => new MapPoint(at[0], at[1])).ToList();

    private static List<MapArea> Areas(IEnumerable<StoredArea> areas) => areas
        .Select(area => new MapArea(area.Name, area.X, area.Y, area.Yaw, area.Local[0], area.Local[1], area.Local[2], area.Local[3]))
        .ToList();

    // Each lane's path starts at lane_<lane>_pathcorner_<team>_1 and follows "next" until a corner with none (or that
    // points to itself: the last corner by the enemy Ancient).
    private static List<CreepPath> CreepPaths(IReadOnlyList<StoredCorner> corners)
    {
        var byName = corners.GroupBy(corner => corner.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var paths = new List<CreepPath>();
        foreach (var (laneName, lane) in new[] { ("top", Lane.Top), ("mid", Lane.Middle), ("bot", Lane.Bottom) })
        {
            foreach (var (teamName, team) in new[] { ("goodguys", Team.Radiant), ("badguys", Team.Dire) })
            {
                var points = new List<MapPoint>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var name = $"lane_{laneName}_pathcorner_{teamName}_1";
                while (byName.TryGetValue(name, out var corner) && seen.Add(name))
                {
                    points.Add(new MapPoint(corner.At[0], corner.At[1]));
                    name = corner.Next;
                }

                if (points.Count > 0)
                {
                    paths.Add(new CreepPath(lane, team, points));
                }
            }
        }

        return paths;
    }

    private sealed record StoredMap(int Version, string Patch, StoredGrid? Grid, List<int[]> Trees, List<int[]> FogBlockers,
        List<int[]> Lanterns, List<StoredCorner> CreepPath, List<StoredArea> NoWardZones, List<StoredArea> BossPits,
        List<StoredArea> CampBoxes, List<StoredArea> ShopAreas, List<StoredObjective> Objectives,
        List<int[]>? FogRevealers = null, List<int[]>? Currents = null, StoredSource? Source = null);

    private sealed record StoredSource(string? File, DateTimeOffset ModifiedUtc, string? Sha256);

    private sealed record StoredGrid(int Cell, int Width, int Height, int OriginX, int OriginY, string Nav, string Floor);

    private sealed record StoredCorner(string Name, string Next, int[] At);

    private sealed record StoredArea(string Name, int X, int Y, int Yaw, int[] Local);

    private sealed record StoredObjective(string Kind, string Name, string? Team, int[] At);
}
