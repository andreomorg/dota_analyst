using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Application.Abstractions;

// The official patches and sub-patches of the game, from Valve's patch notes. Never throws: when Valve cannot be
// reached the list kept with the application is used (it may miss the newest sub-patch).
public interface IGamePatchCatalog
{
    Task<IReadOnlyList<GamePatch>> GetPatchesAsync(CancellationToken cancellationToken);
}
