namespace DotaAnalyst.Tests.Infrastructure;

/// <summary>Runs against a local game build only when its directory is configured.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameDataFactAttribute : FactAttribute
{
    public GameDataFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GAME_DATA_DIR")))
        {
            Skip = "GAME_DATA_DIR is not configured; a local game build is required.";
        }
    }
}
