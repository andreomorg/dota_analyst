using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Replay parsing turned off (no valid ReplayParser:BaseUrl, or no database to keep the results): nothing is ever
// queued or parsed, and analyses of unparsed matches say replayStatus "unavailable" (results saved earlier are
// still shown when there is a database). The rest of the site works exactly as before the feature existed.

public sealed class NoReplayParser : IReplayParser
{
    public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken) =>
        throw new ReplayParserUnavailableException("Replay parsing is not configured.");
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "It is a queue; the name matches IReplayParseQueue.")]
public sealed class DisabledReplayParseQueue : IReplayParseQueue
{
    public ReplayEnqueueResult TryEnqueue(MatchId matchId, Uri replayUrl) => ReplayEnqueueResult.Unavailable;

    public bool IsQueued(MatchId matchId) => false;
}

// Says once, when the app starts, that replay parsing is off and why, or that its configuration looks wrong (like
// DatabaseMigrator does for the database). The message never contains a secret.
internal sealed class ReplayParsingStartupWarning(string message, ILogger<ReplayParsingStartupWarning> logger) : IHostedService
{
    public string Message => message;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("{Warning}", message);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
