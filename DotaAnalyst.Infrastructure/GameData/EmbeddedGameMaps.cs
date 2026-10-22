using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Infrastructure.GameData;

// The maps embedded in this assembly (tools/MapExtractor), read once and shared. ForMatch picks the map by the match's
// official patch (Valve's patch notes kept with the application, MP7): only a match of the map's patch gets it, so a
// match of the next patch gets none (unknown) until that patch has its own file. For the static mappings of the
// replay documents, which have no services.
public static class EmbeddedGameMaps
{
    private const string Resource = "DotaAnalyst.Maps.dota-7.41.map.json.gz";

    private static readonly Lazy<GameMap?> Loaded = new(Load);

    // The 7.41 map; null when the file is missing or unreadable.
    public static GameMap? Map => Loaded.Value;

    public static GameMap? ForMatch(DateTimeOffset? startedAt) =>
        Map is { } map && GamePatch.For(PatchNotesList.Embedded, startedAt) is { } patch && patch.Major == map.Patch ? map : null;

    private static GameMap? Load()
    {
        try
        {
            using var stream = typeof(EmbeddedGameMaps).Assembly.GetManifestResourceStream(Resource)
                ?? throw new InvalidDataException($"{Resource} is not embedded.");
            return GameMapFile.Read(stream);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
