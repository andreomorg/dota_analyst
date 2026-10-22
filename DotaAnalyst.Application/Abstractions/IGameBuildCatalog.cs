using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Application.Abstractions;

// The game data (abilities, kits, items, hero profiles) of the exact build a match was played on, read from the game's
// own files. Null when the build is unknown: no start time, a match outside the covered builds, or the builds not
// synchronized on this machine — the caller then uses the current catalogs (IAbilityCatalog, IItemCatalog).
public interface IGameBuildCatalog
{
    Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? startedAt, CancellationToken cancellationToken);
}
