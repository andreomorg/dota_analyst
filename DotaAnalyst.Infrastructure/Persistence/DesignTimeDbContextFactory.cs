using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotaAnalyst.Infrastructure.Persistence;

// Used only by the dotnet-ef tool to create migrations, which compares the model with the last
// migration and never connects: no real connection string (or secret) is needed for that.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, "Host=localhost;Database=dotaanalyst_design_time");
        return new AppDbContext(options.Options);
    }
}
