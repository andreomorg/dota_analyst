using DotaAnalyst.Infrastructure.GameData;

namespace DotaAnalyst.Tests.Infrastructure;

// Excerpts of the game files of build 98fab972 (dotabuff/d2vpkr, Client 6962, patch 7.41).
public sealed class KeyValuesTests
{
    [Fact]
    public void HeroAbilityBlock_ReadsValuesBlocksAndTalentBonuses()
    {
        var entries = KeyValues.Parse("""
            "DOTAAbilities"
            {
                //=================================================================
                // Lion: Earth Spike
                "lion_impale"
                {
                    "AbilityBehavior"     "DOTA_ABILITY_BEHAVIOR_POINT | DOTA_ABILITY_BEHAVIOR_UNIT_TARGET"
                    "AbilityManaCost"     "90 110 130 150"
                    "AbilityValues"
                    {
                        "AbilityCastRange"
                        {
                            "value"         "650"
                            "special_bonus_unique_lion_2"   "+600"
                        }
                        "duration"          "1.3 1.6 1.9 2.2"
                    }
                }
            }
            """);

        var impale = Assert.Single(entries)["lion_impale"]!;
        Assert.Equal("90 110 130 150", impale.Text("abilitymanacost"));
        var range = impale["AbilityValues"]!["AbilityCastRange"]!;
        Assert.True(range.IsBlock);
        Assert.Equal("+600", range.Text("special_bonus_unique_lion_2"));
        Assert.Equal("1.3 1.6 1.9 2.2", impale["AbilityValues"]!.Text("duration"));
    }

    [Fact]
    public void Translation_ReadsEscapesAndRepeatedKeysInOrder()
    {
        var entries = KeyValues.Parse("""
            "lang"
            {
                "Language" "English"
                "Tokens"
                {
                    "DOTA_Tooltip_ability_lion_impale_Note0"   "The spikes move at %speed% units per second."
                    "quote"    "Say \"hi\"\nnext line \x"
                    "dup" "first"
                    "dup" "second"
                }
            }
            """);

        var tokens = entries[0]["Tokens"]!;
        Assert.Equal("The spikes move at %speed% units per second.", tokens.Text("DOTA_Tooltip_ability_lion_impale_Note0"));
        Assert.Equal("Say \"hi\"\nnext line \\x", tokens.Text("quote"));
        Assert.Equal(["first", "second"], tokens.Children.Where(child => child.Key == "dup").Select(child => child.Value));
    }

    [Fact]
    public void BaseDirectives_BareTokensAndConditionals_AreRead()
    {
        var entries = KeyValues.Parse("""
            #base "heroes/npc_dota_hero_lion.txt"
            "DOTAHeroes"
            {
                Version 1
                "flag" "1" [$WIN32]
                "next" "2"
            }
            """);

        Assert.Equal(("#base", "heroes/npc_dota_hero_lion.txt"), (entries[0].Key, entries[0].Value));
        Assert.Equal("1", entries[1].Text("Version"));
        Assert.Equal("1", entries[1].Text("flag"));
        Assert.Equal("2", entries[1].Text("next"));
    }

    // A whole build on disk (GAME_DATA_DIR = a folder with items.txt, npc_abilities.txt, abilities_english.txt and
    // heroes/*.txt): every file reads. Without the variable the test has nothing to read and passes.
    [Fact]
    public void WholeBuildOnDisk_ReadsEveryFile()
    {
        if (Environment.GetEnvironmentVariable("GAME_DATA_DIR") is not { Length: > 0 } folder)
        {
            return;
        }

        var files = Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories);
        Assert.True(files.Length > 100);
        foreach (var file in files)
        {
            var entries = KeyValues.Parse(File.ReadAllText(file));
            Assert.NotEmpty(entries);
        }

        var tokens = KeyValues.Parse(File.ReadAllText(Path.Combine(folder, "abilities_english.txt")))[0]["Tokens"]!;
        Assert.Equal("Earth Spike", tokens.Text("DOTA_Tooltip_ability_lion_impale"));
        // A hero file: DOTAHeroes > the hero (stats, kit, facets) > AbilityDefinitions > each ability.
        var lion = KeyValues.Parse(File.ReadAllText(Path.Combine(folder, "heroes", "npc_dota_hero_lion.txt")))[0]["npc_dota_hero_lion"]!;
        Assert.Equal("lion_impale", lion.Text("Ability1"));
        Assert.Equal("+600", lion["AbilityDefinitions"]!["lion_impale"]!["AbilityValues"]!["AbilityCastRange"]!
            .Text("special_bonus_unique_lion_2"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" \"b\" }")]
    [InlineData("\"a\" \"unclosed")]
    [InlineData("\"a\"")]
    public void BrokenText_Throws(string text) => Assert.Throws<InvalidDataException>(() => KeyValues.Parse(text));
}
