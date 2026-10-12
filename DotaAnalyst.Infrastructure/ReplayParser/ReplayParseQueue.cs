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
// - Not parsed twice in a row: a match the parser just answered about is refused (Unavailable) for
//   RecentlyParsedFor. Normally its result is in the database and no visit even tries; this only matters when
//   saving the result failed, where the page's polling would otherwise parse the same replay every few seconds.
// - Lost on restart (e.g. Render putting the API to sleep): nothing was saved for those matches, so the next
//   visit simply queues them again.
public sealed class ReplayParseQueue(ReplayParserCircuitBreaker circuitBreaker, TimeProvider timeProvider) : IReplayParseQueue
{
    public const int Capacity = 20;

    public static readonly TimeSpan RecentlyParsedFor = TimeSpan.FromMinutes(10);

    private readonly Channel<ReplayParseRequest> channel = Channel.CreateBounded<ReplayParseRequest>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // Guards "queued" and "recentlyParsed" together with the write to the channel, so a check and an add never
    // interleave.
    private readonly Lock gate = new();
    private readonly HashSet<MatchId> queued = [];

    // Match -> when it was parsed. Small: entries older than RecentlyParsedFor are removed on every Complete.
    private readonly Dictionary<MatchId, DateTimeOffset> recentlyParsed = [];

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

            if (recentlyParsed.TryGetValue(matchId, out var parsedAt)
                && timeProvider.GetUtcNow() - parsedAt < RecentlyParsedFor)
            {
                return ReplayEnqueueResult.Unavailable;
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

    // For the worker: the request was handled or dropped; the match can be queued again. parsed: the parser
    // answered about it (see RecentlyParsedFor); false when it was dropped, the parser was busy or unavailable.
    internal void Complete(MatchId matchId, bool parsed)
    {
        lock (gate)
        {
            queued.Remove(matchId);

            var now = timeProvider.GetUtcNow();
            foreach (var (match, parsedAt) in recentlyParsed)
            {
                if (now - parsedAt >= RecentlyParsedFor)
                {
                    // Removing while enumerating a Dictionary is allowed since .NET Core 3.0.
                    recentlyParsed.Remove(match);
                }
            }

            if (parsed)
            {
                recentlyParsed[matchId] = now;
            }
        }
    }
}
