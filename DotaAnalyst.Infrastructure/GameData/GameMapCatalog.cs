using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Maps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.GameData;

// The map of a match's patch (IGameMapCatalog), from the map files embedded in this assembly. Only 7.41 exists: it
// covers matches from GameData:Since (the release of 7.41) on. A match from before gets no map. The next patch that
// changes the map needs its own file and an end for this one (MP7 brings the official patch times).
public sealed class GameMapCatalog(IOptions<GameDataOptions> options, ILogger<GameMapCatalog> logger) : IGameMapCatalog
{
    // The embedded map (EmbeddedGameMaps, read once for the whole application); its absence is logged once here.
    private readonly Lazy<GameMap?> map = new(() =>
    {
        if (EmbeddedGameMaps.Map is null)
        {
            logger.LogWarning("Could not read the map file; matches get no map");
        }

        return EmbeddedGameMaps.Map;
    });

    public GameMap? ForMatch(DateTimeOffset? startedAt) =>
        startedAt is { } start && start >= options.Value.Since ? map.Value : null;
}
