using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Catalogs;

// One row of the "catalog_entries" table: a hero or item name. Heroes and items have the same shape
// (ID + name), so they share one table, told apart by Catalog ("heroes", "items").
public class CatalogEntryRecord
{
    public string Catalog { get; set; } = "";

    public int Id { get; set; }

    public string Name { get; set; } = "";

    // When the catalog was loaded from OpenDota (the same for every row of a catalog).
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class CatalogEntryRecordConfiguration : IEntityTypeConfiguration<CatalogEntryRecord>
{
    public void Configure(EntityTypeBuilder<CatalogEntryRecord> builder)
    {
        // Composite key: the same ID exists once per catalog (hero 1 and item 1 are different things).
        builder.HasKey(entry => new { entry.Catalog, entry.Id });
        builder.Property(entry => entry.Catalog).HasMaxLength(32);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
    }
}
