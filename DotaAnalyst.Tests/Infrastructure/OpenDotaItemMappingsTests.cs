using System.Text.Json;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure;

public class OpenDotaItemMappingsTests
{
    [Fact]
    public void ToItems_JoinsIdsToDisplayNamesThroughKeys()
    {
        // Arrange: same shapes as GET /constants/item_ids and GET /constants/items.
        var itemIds = Deserialize<Dictionary<int, string>>("""{ "1": "blink", "36": "magic_wand" }""");
        var items = Deserialize<Dictionary<string, OpenDotaItem>>(
            """
            {
              "blink": { "id": 1, "dname": "Blink Dagger", "cost": 2250 },
              "magic_wand": { "id": 36, "dname": "Magic Wand", "cost": 450 }
            }
            """);

        // Act
        var result = OpenDotaMappings.ToItems(itemIds, items);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(new Item(1, "Blink Dagger"), result[1]);
        Assert.Equal(new Item(36, "Magic Wand"), result[36]);
    }

    [Fact]
    public void ToItems_SkipsKeysWithoutDataOrDisplayName()
    {
        var itemIds = new Dictionary<int, string>
        {
            [1] = "blink",
            [2] = "removed_item",
            [3] = "recipe_without_name",
            [4] = "blank_name",
        };
        var items = new Dictionary<string, OpenDotaItem>
        {
            ["blink"] = new("Blink Dagger"),
            ["recipe_without_name"] = new(Dname: null),
            ["blank_name"] = new(" "),
        };

        var result = OpenDotaMappings.ToItems(itemIds, items);

        Assert.Equal([1], result.Keys);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, OpenDotaClient.JsonOptions)!;
}
