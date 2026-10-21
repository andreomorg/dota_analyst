using System.Text.Json;

namespace DotaAnalyst.Infrastructure.OpenDota;

public record OpenDotaItem(
    string? Dname, int? Cost = null, IReadOnlyList<string?>? Components = null, string? Qual = null,
    JsonElement Abilities = default, JsonElement Behavior = default, JsonElement Cd = default,
    JsonElement Mc = default, JsonElement Attrib = default, JsonElement Charges = default);
