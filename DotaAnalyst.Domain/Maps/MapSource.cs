namespace DotaAnalyst.Domain.Maps;

// Where a patch's map came from: the game's map file read by tools/MapExtractor (name, its modification time and
// SHA-256), so a map can be told apart from another extraction of the same patch. Not the game build of a match.
public sealed record MapSource(string File, DateTimeOffset ModifiedUtc, string Sha256);
