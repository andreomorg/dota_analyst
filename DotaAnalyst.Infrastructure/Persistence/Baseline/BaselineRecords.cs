using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// One row of "baseline_matches": a match the collector tried (collected or not), so it is never tried twice.
public class BaselineMatchRecord
{
    public long MatchId { get; set; }

    // "pro" or "highRank".
    public string Source { get; set; } = "";

    // "collected", "noReplay" or "failed".
    public string Status { get; set; } = "";

    public DateTimeOffset StartTime { get; set; }

    public int DurationSeconds { get; set; }

    public DateTimeOffset TriedAt { get; set; }
}

// One row of "baseline_metrics": one hero's metrics in a collected match (ten rows per match), what the comparison
// reads by hero, role and source.
public class BaselineMetricsRecord
{
    public long MatchId { get; set; }

    public int HeroId { get; set; }

    public int Role { get; set; }

    public int? OpponentHeroId { get; set; }

    public string Source { get; set; } = "";

    public DateTimeOffset StartTime { get; set; }

    public int DurationSeconds { get; set; }

    // StoredHeroMetrics as JSON (the same format as the replay analyses).
    public string Metrics { get; set; } = "";

    // The hero's statistics minute by minute (StoredStatHistory as JSON); null in rows collected before they were kept
    // (filled from the kept replay document by BaselineHistoryBackfill).
    public string? StatHistory { get; set; }
}

internal sealed class BaselineMatchRecordConfiguration : IEntityTypeConfiguration<BaselineMatchRecord>
{
    public void Configure(EntityTypeBuilder<BaselineMatchRecord> builder)
    {
        builder.HasKey(match => match.MatchId);
        builder.Property(match => match.MatchId).ValueGeneratedNever();
        builder.Property(match => match.Source).HasMaxLength(16);
        builder.Property(match => match.Status).HasMaxLength(16);
        // The daily limit counts collected matches by when they were tried.
        builder.HasIndex(match => new { match.Status, match.TriedAt });
    }
}

internal sealed class BaselineMetricsRecordConfiguration : IEntityTypeConfiguration<BaselineMetricsRecord>
{
    public void Configure(EntityTypeBuilder<BaselineMetricsRecord> builder)
    {
        builder.HasKey(metrics => new { metrics.MatchId, metrics.HeroId });
        builder.Property(metrics => metrics.Source).HasMaxLength(16);
        builder.Property(metrics => metrics.Metrics).HasColumnType("jsonb");
        builder.Property(metrics => metrics.StatHistory).HasColumnType("jsonb");
        // The comparison's query.
        builder.HasIndex(metrics => new { metrics.HeroId, metrics.Role, metrics.Source, metrics.StartTime });
        builder.HasIndex(metrics => new { metrics.HeroId, metrics.Role, metrics.OpponentHeroId, metrics.Source, metrics.StartTime });
    }
}
