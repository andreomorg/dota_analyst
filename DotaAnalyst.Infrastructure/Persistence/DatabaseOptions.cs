namespace DotaAnalyst.Infrastructure.Persistence;

// "Database" section of the configuration. The connection string itself is ConnectionStrings:Database
// (a secret: user-secrets locally, environment variable in Docker and on Render).
public class DatabaseOptions
{
    public const string SectionName = "Database";

    public const string ConnectionStringName = "Database";

    // Apply pending migrations when the app starts. Off in the API tests, which have no database.
    public bool MigrateOnStartup { get; set; } = true;
}
