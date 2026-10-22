namespace DotaAnalyst.Domain.Replays;

// Replay engine identity is independent of the content catalog selected by match date.
public sealed record ReplayMetadata(double DurationSeconds, uint? EngineBuild, string? ProducerVersion, bool? MatchEnded);
