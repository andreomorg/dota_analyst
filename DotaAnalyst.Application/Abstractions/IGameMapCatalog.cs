using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Application.Abstractions;

// The static map of the patch a match was played on, read from the game's own map file. Null when no map of that
// patch is known here (a match from before the covered patch, or without a start time): what depends on the map is
// then unknown. Never throws.
public interface IGameMapCatalog
{
    GameMap? ForMatch(DateTimeOffset? startedAt);
}
