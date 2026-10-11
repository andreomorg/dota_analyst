using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Players;

// One row of the "player_searches" table: a searched player. Nothing about who searched is kept.
public class PlayerSearchRecord
{
    public long AccountId { get; set; }

    public DateTimeOffset FirstSearchedAt { get; set; }

    public DateTimeOffset LastSearchedAt { get; set; }

    public int SearchCount { get; set; }
}

internal sealed class PlayerSearchRecordConfiguration : IEntityTypeConfiguration<PlayerSearchRecord>
{
    public void Configure(EntityTypeBuilder<PlayerSearchRecord> builder)
    {
        builder.HasKey(search => search.AccountId);
        builder.Property(search => search.AccountId).ValueGeneratedNever();
    }
}
