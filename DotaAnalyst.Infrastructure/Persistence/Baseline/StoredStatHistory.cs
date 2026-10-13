using System.Text.Json;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Infrastructure.Persistence.Replays;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// The JSON format of a base hero's statistics minute by minute (baseline_metrics.stat_history), separate from the
// domain model like the other stored formats. Keys by name ("lastHits").
internal sealed record StoredStatHistory(IReadOnlyList<int> Seconds, IReadOnlyDictionary<StatKey, IReadOnlyList<int>> Series)
{
    public static string Serialize(HeroStatHistory history) =>
        JsonSerializer.Serialize(new StoredStatHistory(history.Seconds, history.Series), StoredReplayMappings.JsonOptions);

    /// <exception cref="JsonException">Unreadable JSON.</exception>
    /// <exception cref="ArgumentException">Values the domain refuses.</exception>
    public static HeroStatHistory? Deserialize(string? json)
    {
        if (json is null)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredStatHistory>(json, StoredReplayMappings.JsonOptions)
            ?? throw new JsonException("Empty statistics.");
        return new HeroStatHistory(stored.Seconds ?? [], stored.Series ?? new Dictionary<StatKey, IReadOnlyList<int>>());
    }
}
