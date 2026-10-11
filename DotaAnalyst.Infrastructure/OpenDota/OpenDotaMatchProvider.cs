using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Infrastructure.OpenDota;

public class OpenDotaMatchProvider(OpenDotaClient openDotaClient) : IMatchProvider
{
    public async Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var matches = await openDotaClient.GetRecentMatchesAsync(accountId.Value, cancellationToken);
        return matches.Select(match => match.ToPlayerMatch()).ToList();
    }
}
