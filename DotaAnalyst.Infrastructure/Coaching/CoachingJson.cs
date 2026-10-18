using System.Text.Json;

namespace DotaAnalyst.Infrastructure.Coaching;

public static class CoachingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
