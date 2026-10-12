using System.Threading.Channels;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// A replay waiting to be parsed.
public sealed record ReplayParseRequest(MatchId MatchId, Uri ReplayUrl);

// Replays waiting to be parsed, in memory (one for the whole app: Singleton). Requests add to it without waiting;
// ReplayParseWorker takes them one at a time.
// - A Channel is a thread-safe queue made for "many producers, one consumer that awaits new items".
// - Bounded: at most Capacity waiting; more than that is refused (Full) instead of piling up. The visitor sees
//   "unavailable" and a later visit tries again.
// - No duplicates: the set "queued" holds what is waiting or being parsed, until the worker calls Complete.
// - Lost on restart (e.g. Render putting the API to sleep): nothing was saved for those matches, so the next
//   visit simply queues them again.
public sealed class ReplayParseQueue(ReplayParserCircuitBreaker circuitBreaker) : IReplayParseQueue
{
    public const int Capacity = 20;

    private readonly Channel<ReplayParseRequest> channel = Channel.CreateBounded<ReplayParseRequest>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // Guards "queued" together with the write to the channel, so a check and an add never interleave.
    private readonly Lock gate = new();
    private readonly HashSet<MatchId> queued = [];

    public ReplayEnqueueResult TryEnqueue(MatchId matchId, Uri replayUrl)
    {
        // The parser stopped answering recently: don't even queue (the worker would drop it anyway).
        if (circuitBreaker.IsOpen)
        {
            return ReplayEnqueueResult.Unavailable;
        }

        lock (gate)
        {
            if (queued.Contains(matchId))
            {
                return ReplayEnqueueResult.AlreadyQueued;
            }

            // TryWrite never waits: false when Capacity items are already waiting.
            if (!channel.Writer.TryWrite(new ReplayParseRequest(matchId, replayUrl)))
            {
                return ReplayEnqueueResult.Full;
            }

            queued.Add(matchId);
            return ReplayEnqueueResult.Enqueued;
        }
    }

    public bool IsQueued(MatchId matchId)
    {
        lock (gate)
        {
            return queued.Contains(matchId);
        }
    }

    // For the worker: waits for the next request (ends when cancelled).
    internal IAsyncEnumerable<ReplayParseRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);

    // For the worker: the request was handled (result saved) or dropped; the match can be queued again.
    internal void Complete(MatchId matchId)
    {
        lock (gate)
        {
            queued.Remove(matchId);
        }
    }
}
