using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Baseline;

// Use case (on the collector's machine): turns the raw base (every collected match, local database) into the finished
// base — each hero/role/source's distributions — and writes it to the local site's database and to the production
// mirror. Production only ever reads finished numbers.
public class PublishBaselineHandler(
    IBaselineRepository repository,
    IBaselineDistributionStore localStore,
    IBaselineMirror mirror,
    TimeProvider timeProvider,
    ILogger<PublishBaselineHandler> logger)
{
    // How far back the published base goes (older matches are from other patches).
    public static readonly TimeSpan Window = TimeSpan.FromDays(60);

    /// <returns>How many hero/role/source sets were published.</returns>
    public async Task<int> PublishAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var since = now - Window;
        var sets = new List<BaselineDistributionSet>();
        foreach (var (heroId, role, source) in await repository.GetGroupsAsync(since, cancellationToken))
        {
            var samples = await repository.GetSamplesAsync(heroId, role, source, since, cancellationToken);
            sets.Add(new BaselineDistributionSet(heroId, role, source, samples.Count, BaselineComparer.Distribute(samples)));
        }

        await localStore.ReplaceAllAsync(sets, now, cancellationToken);
        if (mirror.IsConfigured)
        {
            await mirror.ReplaceAllAsync(sets, now, cancellationToken);
        }

        logger.LogInformation(
            "Published the comparison base: {Sets} hero/role/source sets{Mirror}", sets.Count, mirror.IsConfigured ? " (and the mirror)" : "");
        return sets.Count;
    }
}
