using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// Purchases as the replay logs them: the parts and the recipe, then the assembled item when the parts come together
// (seen in match 9040879414: item_recipe_buckler at 883.4, item_buckler at 907.6).
public class ItemBuildEvaluatorTests
{
    // A slice of OpenDota's item constants.
    private static readonly IReadOnlyDictionary<string, ItemDetails> Catalog = new[]
    {
        Item("tango", 90, consumable: true),
        Item("ward_observer", 0, consumable: true),
        Item("branches", 50),
        Item("magic_stick", 200),
        Item("recipe_magic_wand", 60),
        Item("magic_wand", 460, "magic_stick", "branches", "branches"),
        Item("ogre_axe", 1000),
        Item("mithril_hammer", 1600),
        Item("recipe_black_king_bar", 1450),
        Item("black_king_bar", 4050, "mithril_hammer", "ogre_axe"),
        Item("blink", 2250),
        Item("eagle", 2800),
        Item("recipe_swift_blink", 1750),
        Item("swift_blink", 6800, "blink", "eagle"),
        Item("bottle", 675),
        Item("boots", 500),
        Item("headdress", 425),
        Item("chainmail", 550),
        Item("recipe_mekansm", 800),
        Item("mekansm", 1775, "headdress", "chainmail"),
        Item("energy_booster", 800),
        Item("arcane_boots", 1300, "boots", "energy_booster"),
        Item("recipe_guardian_greaves", 1450),
        Item("guardian_greaves", 4450, "arcane_boots", "mekansm"),
    }.ToDictionary(item => item.Key);

    [Fact]
    public void StartingItems_AreGroupedWithTheirCount()
    {
        var build = Evaluate(Buy(-90, "tango"), Buy(-90, "branches"), Buy(-90, "branches"), Buy(-89, "branches"), Buy(30, "ward_observer"));

        Assert.Equal([new StartingItem("tango", 1), new StartingItem("branches", 3)], build.StartingItems);
        Assert.Empty(build.Items);
    }

    [Fact]
    public void AnAssembledItem_ShowsWhenItsPartsCameTogether_NotItsParts()
    {
        // Out of order on purpose: the evaluator sorts by time.
        var build = Evaluate(
            Buy(1150, "black_king_bar"), Buy(600, "ogre_axe"), Buy(800, "recipe_black_king_bar"), Buy(1120, "mithril_hammer"));

        Assert.Equal([new BuildItem("black_king_bar", 1150, Assembled: true)], build.Items);
    }

    [Fact]
    public void ExpensiveItemsBoughtWhole_ShowWhileTheyStayWhole()
    {
        var build = Evaluate(
            Buy(400, "bottle"), Buy(500, "boots"), Buy(900, "blink"),
            Buy(1500, "eagle"), Buy(1600, "recipe_swift_blink"), Buy(1600, "swift_blink"));

        // Boots are too cheap on their own; the Blink became a part of the Swift Blink (and so did the Eagle).
        Assert.Equal(
            [new BuildItem("bottle", 400, Assembled: false), new BuildItem("swift_blink", 1600, Assembled: true)],
            build.Items);
    }

    [Fact]
    public void AssembledItemsThatBecomeAPart_StayAsAStepOfTheBuild()
    {
        var build = Evaluate(
            Buy(300, "boots"), Buy(500, "energy_booster"), Buy(505, "arcane_boots"),
            Buy(700, "headdress"), Buy(800, "chainmail"), Buy(1000, "recipe_mekansm"), Buy(1001, "mekansm"),
            Buy(1500, "recipe_guardian_greaves"), Buy(1530, "guardian_greaves"));

        Assert.Equal(
            ["arcane_boots", "mekansm", "guardian_greaves"],
            build.Items.Select(item => item.ItemKey));
    }

    [Fact]
    public void APartIsUsedUpOnce_TheSecondCopyStays()
    {
        // Two Blinks (one for an ally, say): the Swift Blink uses only one.
        var build = Evaluate(
            Buy(600, "blink"), Buy(700, "blink"), Buy(1500, "eagle"), Buy(1600, "recipe_swift_blink"), Buy(1600, "swift_blink"));

        Assert.Equal(
            [new BuildItem("blink", 600, Assembled: false), new BuildItem("swift_blink", 1600, Assembled: true)],
            build.Items);
    }

    [Fact]
    public void RecipesAndUnknownItems_NeverShow()
    {
        var build = Evaluate(Buy(100, "item_from_a_new_patch"), Buy(200, "recipe_magic_wand"), Buy(900, "blink"));

        Assert.Equal(["blink"], build.Items.Select(item => item.ItemKey));
    }

    private static ItemBuild Evaluate(params ItemPurchase[] purchases) =>
        ItemBuildEvaluator.Evaluate(purchases, Catalog);

    private static ItemPurchase Buy(int seconds, string key) => new(31, seconds, key);

    private static ItemDetails Item(string key, int cost, params string[] components) =>
        new(key, cost, components, isConsumable: false);

    private static ItemDetails Item(string key, int cost, bool consumable) => new(key, cost, [], consumable);
}
