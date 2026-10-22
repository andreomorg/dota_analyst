using DotaAnalyst.Domain.Heroes;
namespace DotaAnalyst.Application.Abstractions;

// Materialized descriptions/profiles of one immutable build; null falls back to its game files.
public interface IHeroProfileStore
{
    Task<HeroDetails?> FindAsync(string buildId, int heroId, CancellationToken cancellationToken);
}
