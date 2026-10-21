using System.Globalization;
using System.Text.Json;
using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Flexible external fields stay JSON until mapped, so an unexpected field only loses its own value.
public sealed record OpenDotaAbility
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = [];
    public AbilityDetails ToDetails(string key) => OpenDotaKitMappings.ToAbility(key, JsonSerializer.SerializeToElement(Fields));
}
public sealed record OpenDotaHeroKit
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = [];
    public HeroKit ToKit(int heroId) => OpenDotaKitMappings.ToKit(heroId, JsonSerializer.SerializeToElement(Fields));
}
public sealed record OpenDotaPatch(string? Name);

public static class OpenDotaKitMappings
{
    internal static JsonElement Field(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) ? value : default;
    internal static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static string? Text(JsonElement data, string name) => Text(Field(data, name));
    internal static IEnumerable<JsonElement> Array(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    internal static IReadOnlyList<string> Strings(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!]
        : Array(value).Select(Text).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray();
    internal static IReadOnlyList<double> Numbers(JsonElement value)
    {
        var parts = value.ValueKind switch
        {
            JsonValueKind.Number => new[] { value.GetRawText() },
            JsonValueKind.String => value.GetString()!.Split('/'),
            JsonValueKind.Array => Array(value).SelectMany(v => v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Split('/') : new[] { v.GetRawText() }).ToArray(),
            _ => [],
        };
        var result = new List<double>();
        foreach (var part in parts)
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number)) return [];
            result.Add(number);
        }
        return result;
    }
    internal static IReadOnlyList<AbilityValue> Values(JsonElement value) => Array(value).Select(v =>
    {
        var raw = Field(v, "value");
        var levels = Numbers(raw);
        return new AbilityValue(Text(v, "key") ?? "", Text(v, "header") ?? Text(v, "display"), levels,
            levels.Count > 0 ? null : Text(raw) ?? (raw.ValueKind == JsonValueKind.Undefined ? null : raw.GetRawText()));
    }).ToArray();

    public static AbilityDetails ToAbility(string key, JsonElement data)
    {
        var teams = Strings(Field(data, "target_team"));
        var team = teams.Contains("Both") || (teams.Contains("Enemy") && teams.Contains("Friendly"))
            ? AbilityTargetTeam.Both : teams.Count == 1 ? teams[0] switch
            {
                "Enemy" => AbilityTargetTeam.Enemy, "Friendly" => AbilityTargetTeam.Ally,
                "Custom" => AbilityTargetTeam.Custom, _ => AbilityTargetTeam.None,
            } : AbilityTargetTeam.None;
        return new(key, Text(data, "dname") ?? key, Text(data, "desc"), Strings(Field(data, "behavior")), team,
            Strings(Field(data, "target_type")), Text(data, "dmg_type") switch
            {
                "Physical" => AbilityDamageType.Physical, "Magical" => AbilityDamageType.Magical,
                "Pure" => AbilityDamageType.Pure, _ => AbilityDamageType.None,
            }, Text(data, "bkbpierce") switch { "Yes" => true, "No" => false, _ => null },
            Text(data, "dispellable") switch
            {
                "Yes" => Dispellable.Yes, "No" => Dispellable.No,
                "Strong Dispels Only" => Dispellable.StrongOnly, _ => Dispellable.Unknown,
            }, Numbers(Field(data, "cd")), Numbers(Field(data, "mc")), Values(Field(data, "attrib")));
    }

    public static HeroKit ToKit(int heroId, JsonElement data) => new(heroId,
        Strings(Field(data, "abilities")).Where(key => key != "generic_hidden").ToArray(),
        Array(Field(data, "talents")).Where(v => !string.IsNullOrWhiteSpace(Text(v, "name")))
            .Select(v => new Talent(Text(v, "name")!, Integer(Field(v, "level")))).ToArray(),
        Array(Field(data, "facets")).Where(v => !string.IsNullOrWhiteSpace(Text(v, "name")))
            .Select(v => new Facet(Integer(Field(v, "id")), Text(v, "name")!, Text(v, "title"),
                Text(v, "description"), Field(v, "deprecated").ValueKind == JsonValueKind.True
                    || Text(v, "deprecated") == "true")).ToArray());
    private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : 0;
}
