using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class InventoryFactsTests
{
    [Fact]
    public void At_TheLastChangeByThen_EachPlaceApart()
    {
        var activity = new ReplayActivity([], [], [], slotChanges:
        [
            new SlotChange(1, 300, 0, "item_falcon_blade"),
            new SlotChange(1, 366, 0, null), new SlotChange(1, 366, 6, "item_falcon_blade"),   // to the backpack
            new SlotChange(1, 370, 6, null), new SlotChange(1, 370, 0, "item_falcon_blade"),   // back (after the anchor)
            new SlotChange(1, 360, 9, "item_gloves"),                                          // in the stash
            new SlotChange(1, 0, SlotChange.TeleportSlot, "item_tpscroll"),
            new SlotChange(2, 366, 0, "other_hero_item"),
        ]);

        var at = InventoryFacts.At(activity, 1, 368);

        Assert.Null(at.Inventory![0]);
        Assert.Equal("item_falcon_blade", at.BackpackSlots![0]);
        Assert.Equal("item_gloves", at.Stash![0]);
        Assert.Equal("item_tpscroll", at.Teleport);
        Assert.Equal(FactAvailability.Available, at.Backpack);
        Assert.Equal("item_falcon_blade", InventoryFacts.At(activity, 1, 370).Inventory![0]);
    }

    [Fact]
    public void At_DocumentWithoutBackpack_IsUnknownNotEmpty()
    {
        var at = InventoryFacts.At(new ReplayActivity([], [], [], slotChanges: [new SlotChange(1, 0, 0, "item_tango")]), 1, 10);

        Assert.Equal(FactAvailability.Unavailable, at.Backpack);
        Assert.Null(at.BackpackSlots);
        Assert.Equal("item_tango", at.Inventory![0]);
    }

    [Fact]
    public void At_WithoutInventories_IsUnknown()
    {
        Assert.Equal(FactAvailability.Unavailable, InventoryFacts.At(null, 1, 10).Availability);
    }
}
