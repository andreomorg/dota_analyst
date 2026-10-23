using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>Where the document and the game data came from: the replay's duration and engine build, the parser's version
/// when known, whether the match ended in the replay, and the game build (catalog) used for the hero's nominal data.</summary>
public sealed record ReplayFactsMetadata(double DurationSeconds, uint? EngineBuild, string? ProducerVersion, bool? MatchEnded, string? CatalogBuild);
