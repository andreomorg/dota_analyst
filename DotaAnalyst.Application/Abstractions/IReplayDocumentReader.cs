using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

// Reads a replay document handed in (a fresh parse of the replay parser's POST /v1/match, not kept anywhere) the same
// way a kept one is read. For the internal measuring routes only. Throws InvalidDataException when the document is
// unreadable or has no match ID.
public interface IReplayDocumentReader
{
    (MatchId MatchId, ArchivedReplay Replay) Read(string document);
}
