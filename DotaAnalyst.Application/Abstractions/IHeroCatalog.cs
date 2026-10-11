using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Application.Abstractions;

public interface IHeroCatalog
{
    // Heroes by ID. Returns an empty dictionary (never throws) when the list is unavailable,
    // so missing hero names don't hide the matches.
    Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken);
}
