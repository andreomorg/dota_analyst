using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotaAnalyst.Infrastructure.GameData;

internal static class HeroProfileCatalogKey
{
    // catalog_entries.catalog is varchar(32). The full build identity is also verified in every stored entry.
    internal static string For(string buildId) => "hp1:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(buildId))).ToLowerInvariant()[..28];

    // The stored entries' JSON, written and read the same way. Enums as text ("strength"), so reordering an enum never
    // changes what a saved row means; numbers written before still read (the converter accepts both).
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
