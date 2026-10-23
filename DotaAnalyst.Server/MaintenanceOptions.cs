namespace DotaAnalyst.Server;

/// <summary>Independent machine access to replay maintenance and facts.</summary>
public sealed class MaintenanceOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
}
