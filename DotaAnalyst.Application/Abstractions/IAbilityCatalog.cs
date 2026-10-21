using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Application.Abstractions;

public interface IAbilityCatalog
{
    Task<IReadOnlyDictionary<string, AbilityDetails>> GetAbilitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<int, HeroKit>> GetKitsAsync(CancellationToken cancellationToken);
    Task<string?> GetPatchAsync(CancellationToken cancellationToken);
}
