using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.GameData;

namespace DotaAnalyst.Tests.Infrastructure;

// Excerpts of the game files of build 98fab972 (dotabuff/d2vpkr, Client 6962, patch 7.41).
public sealed class GameFileMappingsTests
{
    private const string Lion = """
        "DOTAHeroes"
        {
            "npc_dota_hero_lion"
            {
                "HeroID"  "26"
                "Role"    "Support,Disabler,Nuker,Initiator"
                "AttributePrimary"   "DOTA_ATTRIBUTE_INTELLECT"
                "AttackCapabilities" "DOTA_UNIT_CAP_RANGED_ATTACK"
                "Ability1"  "lion_impale"
                "Ability2"  "lion_mana_drain"
                "Ability3"  "lion_to_hell_and_back"
                "Ability4"  "generic_hidden"
                "Ability5"  "lion_finger_of_death"
                "Ability6"  ""
                "AbilityTalentStart" "10"
                "Ability10" "special_bonus_unique_lion_6"
                "Ability11" "special_bonus_movement_speed_20"
                "Facets"
                {
                    "lion_essence_eater" { "Icon" "nuke" "Color" "Purple" "Deprecated" "true" }
                }
                "AbilityDefinitions"
                {
                    "lion_impale"
                    {
                        "AbilityBehavior"        "DOTA_ABILITY_BEHAVIOR_POINT | DOTA_ABILITY_BEHAVIOR_UNIT_TARGET | DOTA_ABILITY_BEHAVIOR_IGNORE_BACKSWING"
                        "AbilityUnitTargetTeam"  "DOTA_UNIT_TARGET_TEAM_ENEMY"
                        "AbilityUnitTargetType"  "DOTA_UNIT_TARGET_HERO | DOTA_UNIT_TARGET_BASIC"
                        "AbilityUnitDamageType"  "DAMAGE_TYPE_MAGICAL"
                        "SpellImmunityType"      "SPELL_IMMUNITY_ENEMIES_NO"
                        "SpellDispellableType"   "SPELL_DISPELLABLE_YES_STRONG"
                        "AbilityCastPoint"       "0.3 0.3 0.3 0.3"
                        "AbilityManaCost"        "90 110 130 150"
                        "AbilityValues"
                        {
                            "AbilityCooldown"  "14 13 12 11"
                            "AbilityCastRange" { "value" "650" "special_bonus_unique_lion_2" "+600" }
                            "damage"   { "value" "105 170 235 300" }
                            "width"    { "value" "140" "affected_by_aoe_increase" "1" }
                            "duration" "1.3 1.6 1.9 2.2"
                            "speed"    "2800"
                        }
                    }
                    "lion_mana_drain"
                    {
                        "AbilityBehavior"       "DOTA_ABILITY_BEHAVIOR_UNIT_TARGET | DOTA_ABILITY_BEHAVIOR_CHANNELLED"
                        "AbilityUnitTargetTeam" "DOTA_UNIT_TARGET_TEAM_ENEMY"
                        "AbilityUnitTargetFlags" "DOTA_UNIT_TARGET_FLAG_FOW_VISIBLE | DOTA_UNIT_TARGET_FLAG_NO_INVIS"
                        "SpellDispellableType"  "SPELL_DISPELLABLE_NO"
                        "HasShardUpgrade"       "1"
                        "AbilityCastRange"      "850"
                        "AbilityChannelTime"    "5.1"
                        "AbilityValues"
                        {
                            "movespeed" { "value" "15 20 25 30" "special_bonus_unique_lion_6" "+10" }
                            "shard_bonus_targets" { "value" "2" "RequiresShard" "1" }
                        }
                    }
                    "lion_to_hell_and_back"
                    {
                        "AbilityBehavior" "DOTA_ABILITY_BEHAVIOR_PASSIVE | DOTA_ABILITY_BEHAVIOR_INNATE_UI"
                        "Innate" "1"
                    }
                    "lion_finger_of_death"
                    {
                        "AbilityType"     "ABILITY_TYPE_ULTIMATE"
                        "AbilityBehavior" "DOTA_ABILITY_BEHAVIOR_UNIT_TARGET"
                        "HasScepterUpgrade" "1"
                        "AbilityValues" { "damage" { "value" "600 725 850" "special_bonus_scepter" "+150" } "splash_radius_scepter" { "value" "325" "RequiresScepter" "1" } }
                    }
                }
            }
        }
        """;

    private const string Abilities = """
        "DOTAAbilities"
        {
            "special_bonus_movement_speed_20"
            {
                "AbilityType" "ABILITY_TYPE_ATTRIBUTES"
                "AbilityBehavior" "DOTA_ABILITY_BEHAVIOR_PASSIVE"
                "AbilityValues" { "value" { "value" "20" } }
            }
        }
        """;

    private const string Items = """
        "DOTAAbilities"
        {
            "item_black_king_bar"
            {
                "AbilityBehavior" "DOTA_ABILITY_BEHAVIOR_NO_TARGET | DOTA_ABILITY_BEHAVIOR_IMMEDIATE"
                "AbilityCooldown" "95"
                "AbilityManaCost" "50"
                "ItemCost" "4050"
                "AbilityValues" { "duration" "9" }
            }
            "item_recipe_black_king_bar"
            {
                "ItemCost" "1050"
                "ItemRecipe" "1"
                "ItemResult" "item_black_king_bar"
                "ItemRequirements" { "01" "item_ogre_axe;item_mithril_hammer" }
            }
            "item_tango" { "ItemCost" "90" "ItemQuality" "consumable" "ItemInitialCharges" "3" }
        }
        """;

    private const string Translation = """
        "lang" { "Tokens" {
            "DOTA_Tooltip_ability_lion_impale"              "Earth Spike"
            "DOTA_Tooltip_ability_lion_impale_Description"  "Rock spikes burst from the earth along a straight path."
            "DOTA_Tooltip_ability_lion_impale_Lore"         "The Demon Witch exercises his demonic covenant."
            "DOTA_Tooltip_ability_lion_impale_Note0"        "The spikes move at %speed% units per second."
            "DOTA_Tooltip_ability_lion_impale_duration"     "STUN DURATION:"
            "DOTA_Tooltip_ability_lion_mana_drain"          "Mana Drain"
            "DOTA_Tooltip_ability_lion_mana_drain_movespeed" "%MOVEMENT SLOW/BONUS:"
            "DOTA_Tooltip_ability_lion_mana_drain_shard_description" "Affects up to %shard_bonus_targets% additional enemies.<br>Debuff Immune."
            "DOTA_Tooltip_ability_lion_finger_of_death"     "Finger of Death"
            "DOTA_Tooltip_ability_lion_finger_of_death_scepter_description" "Gains area of effect damage."
            "DOTA_Tooltip_ability_special_bonus_unique_lion_6"  "+{s:bonus_movespeed}% Mana Drain Slow"
            "DOTA_Tooltip_ability_special_bonus_movement_speed_20" "+{s:value} Movement Speed"
            "DOTA_Tooltip_Facet_lion_essence_eater" "Essence Eater"
            "DOTA_Tooltip_ability_item_black_king_bar_Description" "<h1>Active: Avatar</h1> Grants spell immunity for %duration% seconds."
        } }
        """;

    private static GameCatalog Catalog() => GameFileMappings.ToCatalog(new GameFiles(new Dictionary<string, string>
    {
        ["scripts/npc/heroes/npc_dota_hero_lion.txt"] = Lion,
        [GameFiles.Abilities] = Abilities,
        [GameFiles.Items] = Items,
        [GameFiles.Translation] = Translation,
    }));

    [Fact]
    public void Ability_HasEveryCharacteristic()
    {
        var impale = Catalog().Abilities!["lion_impale"];

        Assert.Equal("Earth Spike", impale.Name);
        Assert.Equal("Rock spikes burst from the earth along a straight path.", impale.Description);
        Assert.StartsWith("The Demon Witch", impale.Lore);
        Assert.Equal(["The spikes move at 2800 units per second."], impale.Profile!.Notes);
        Assert.Equal(["POINT", "UNIT_TARGET", "IGNORE_BACKSWING"], impale.Profile.Behaviors);
        Assert.Equal(["Point Target", "Unit Target"], impale.Behavior);
        Assert.Equal(AbilityTargetTeam.Enemy, impale.TargetTeam);
        Assert.Equal(["Hero", "Basic"], impale.TargetTypes);
        Assert.Equal(AbilityDamageType.Magical, impale.DamageType);
        Assert.Equal(SpellImmunity.EnemiesNo, impale.Profile.SpellImmunity);
        Assert.False(impale.PiercesSpellImmunity);
        Assert.Equal(Dispellable.StrongOnly, impale.Dispellable);
        Assert.Equal([14d, 13, 12, 11], impale.Cooldown);
        Assert.Equal([90d, 110, 130, 150], impale.ManaCost);
        Assert.Equal([0.3d, 0.3, 0.3, 0.3], impale.Profile.CastPoint);
        Assert.Equal(650, impale.CastRangeAt(1));
        Assert.Equal(new ValueBonus("special_bonus_unique_lion_2", BonusOperation.Add, [600]),
            impale.Value("AbilityCastRange")!.Rules!.Bonuses.Single(), new BonusComparer());
        Assert.True(impale.Value("width")!.Rules!.AffectedByAoeIncrease);
        Assert.Equal(140, impale.RadiusAt(1));
        Assert.Equal("STUN DURATION:", impale.Value("duration")!.Header);
        Assert.True(impale.AffectsEnemies);
        Assert.False(impale.AffectsAllies);
        Assert.True(impale.IsAreaOfEffect);
        Assert.False(impale.IsSingleTarget);
        Assert.False(impale.IsUltimate);
        Assert.Equal(300, impale.MainAmount(AbilityValueKind.Damage, 4));
    }

    [Fact]
    public void ChanneledShardAndUltimateAbilities_AreRead()
    {
        var catalog = Catalog();
        var drain = catalog.Abilities!["lion_mana_drain"];
        Assert.True(drain.IsChanneled);
        Assert.True(drain.IsSingleTarget);
        Assert.Equal([5.1d], drain.Profile!.ChannelTime);
        Assert.Equal(850, drain.CastRangeAt(1));
        Assert.Equal(["FOW_VISIBLE", "NO_INVIS"], drain.Profile.TargetFlags);
        Assert.Equal(Dispellable.No, drain.Dispellable);
        Assert.True(drain.Value("movespeed")!.IsPercent);
        Assert.Equal("MOVEMENT SLOW/BONUS:", drain.Value("movespeed")!.Header);
        Assert.True(drain.Value("shard_bonus_targets")!.Rules!.RequiresShard);
        Assert.Equal("Affects up to 2 additional enemies.\nDebuff Immune.", drain.Profile.ShardDescription);

        var innate = catalog.Abilities["lion_to_hell_and_back"];
        Assert.True(innate.IsInnate);
        Assert.True(innate.IsPassive);

        var finger = catalog.Abilities["lion_finger_of_death"];
        Assert.True(finger.IsUltimate);
        Assert.Equal(750, finger.Value("damage")!.Rules!.BonusFrom(ValueBonus.Scepter)!.Apply(600, 1));
        Assert.True(finger.Value("splash_radius_scepter")!.Rules!.RequiresScepter);
    }

    [Fact]
    public void Kit_HasTalentsWithNumbersFacetsScepterShardAndProfile()
    {
        var catalog = Catalog();
        var kit = catalog.Kits![26];

        Assert.Equal(["lion_impale", "lion_mana_drain", "lion_to_hell_and_back", "lion_finger_of_death"], kit.Abilities);
        Assert.Equal(new Talent("special_bonus_unique_lion_6", 1, "+10% Mana Drain Slow"), kit.Talents[0]);
        Assert.Equal(new Talent("special_bonus_movement_speed_20", 1, "+20 Movement Speed"), kit.Talents[1]);
        Assert.Equal(("Essence Eater", "nuke", "Purple", true), (kit.Facets[0].Title, kit.Facets[0].Icon, kit.Facets[0].Color, kit.Facets[0].Deprecated));
        Assert.Equal(new AghanimUpgrade("Gains area of effect damage.", "Finger of Death", false), kit.Scepter);
        Assert.Equal("Mana Drain", kit.Shard!.AbilityName);
        // Only the draft profile: the hero's base stats and description (Profile/Description) are the H* tasks' to test.
        var lion = catalog.Heroes![26];
        Assert.Equal((HeroAttribute.Intelligence, HeroAttackType.Ranged), (lion.PrimaryAttribute, lion.AttackType));
        Assert.Equal(new HashSet<HeroRole> { HeroRole.Support, HeroRole.Disabler, HeroRole.Nuker, HeroRole.Initiator }, lion.Roles);
    }

    [Fact]
    public void Items_HaveCostComponentsActiveAndCharges()
    {
        var items = Catalog().Items!;
        var bkb = items["black_king_bar"];
        Assert.Equal(4050, bkb.Cost);
        Assert.Equal(["ogre_axe", "mithril_hammer"], bkb.Components);
        Assert.Equal((95d, 50d), (bkb.Cooldown!.Value, bkb.ManaCost!.Value));
        Assert.Equal(new ItemAbility(ItemAbilityKind.Active, "Avatar", "Grants spell immunity for 9 seconds."),
            Assert.Single(bkb.Abilities));
        Assert.True(bkb.HasActive);
        Assert.True(items["tango"].IsConsumable);
        Assert.True(items["tango"].HasCharges);
        Assert.True(items["recipe_black_king_bar"].IsRecipe);
    }

    [Fact]
    public void NeutralRules_ReadTiersArtifactsEnhancementsAndMadstones()
    {
        var neutrals = GameFileMappings.ToNeutralRules(KeyValues.Parse("""
            "neutral_items"
            {
                "madstone_limits" { "madstone_no_limit_time" "70:00" }
                "neutral_tiers"
                {
                    "1"
                    {
                        "start_time" "0:00" "trinket_options" "4" "enhancement_options" "4" "craft_cost" "6" "xp_bonus" "0"
                        "items" { "item_occult_bracelet" "1" "item_pogo_stick_wrong_tier_check" "1" }
                        "enhancements"
                        {
                            "global"   { "item_enhancement_quickened" "1" "item_enhancement_vital" "1" }
                            "strength" { "item_enhancement_brawny" "1" "item_enhancement_tough" "1" }
                            "agility"  { "item_enhancement_alert" "1" "item_enhancement_brawny" "1" }
                        }
                    }
                    "2"
                    {
                        "start_time" "15:00" "trinket_options" "5" "enhancement_options" "5" "craft_cost" "10" "xp_bonus" "0"
                        "items" { "item_pogo_stick" "1" }
                        "enhancements" { "strength" { "item_enhancement_brawny" "2" } }
                    }
                }
            }
            """)[0], KeyValues.Parse("""
            "DOTAAbilities" { "item_madstone_bundle" { "AbilityValues" { "madstone_primary" "2 3 4" "madstone_secondary" "1 2 3" } } }
            """)[0])!;

        Assert.Equal(4200, neutrals.MadstoneNoLimitAtSeconds);
        Assert.Equal([2d, 3, 4], neutrals.MadstonePrimary);
        Assert.Equal([1d, 2, 3], neutrals.MadstoneSecondary);
        var first = neutrals.Tiers[0];
        Assert.Equal((1, 0, 4, 4, 6), (first.Tier, first.OpensAtSeconds, first.ArtifactOptions, first.EnhancementOptions, first.CraftCost));
        Assert.Equal(["enhancement_quickened", "enhancement_vital", "enhancement_brawny", "enhancement_tough"],
            first.EnhancementsFor(HeroAttribute.Strength).Select(e => e.Key));
        Assert.Equal(["enhancement_quickened", "enhancement_vital"], first.EnhancementsFor(HeroAttribute.Intelligence).Select(e => e.Key));
        Assert.Equal(new NeutralEnhancement("enhancement_brawny", 2), neutrals.Tiers[1].EnhancementsFor(HeroAttribute.Strength).Single());
        Assert.Equal(1, neutrals.OpenAt(600)!.Tier);
        Assert.Equal(2, neutrals.OpenAt(900)!.Tier);
        Assert.Equal(2, neutrals.NextAfter(600)!.Tier);
        Assert.Null(neutrals.NextAfter(900));
        Assert.Null(neutrals.OpenAt(-30));
        Assert.Equal(2, neutrals.TierOf("item_pogo_stick"));
        Assert.Equal(NeutralKind.Artifact, neutrals.KindOf("occult_bracelet"));
        Assert.Equal(NeutralKind.Enhancement, neutrals.KindOf("item_enhancement_alert"));
        Assert.Null(neutrals.KindOf("black_king_bar"));
    }

    // A whole build on disk (GAME_DATA_DIR, same layout as KeyValuesTests): every hero has a kit and the counts are
    // those of the build. Without the variable the test is visibly skipped.
    [GameDataFact]
    public void WholeBuildOnDisk_MapsEveryHeroAbilityAndItem()
    {
        var folder = Environment.GetEnvironmentVariable("GAME_DATA_DIR")!;

        var texts = Directory.GetFiles(Path.Combine(folder, "heroes"), "*.txt")
            .ToDictionary(file => GameFiles.HeroesFolder + Path.GetFileName(file), File.ReadAllText);
        texts[GameFiles.Abilities] = File.ReadAllText(Path.Combine(folder, "npc_abilities.txt"));
        texts[GameFiles.Items] = File.ReadAllText(Path.Combine(folder, "items.txt"));
        texts[GameFiles.Translation] = File.ReadAllText(Path.Combine(folder, "abilities_english.txt"));
        // .kv, not .txt, in the folder: KeyValuesTests reads every .txt there.
        if (File.Exists(Path.Combine(folder, "neutral_items.kv")))
        {
            texts[GameFiles.NeutralItems] = File.ReadAllText(Path.Combine(folder, "neutral_items.kv"));
        }

        var catalog = GameFileMappings.ToCatalog(new GameFiles(texts));
        var report = string.Join("\n",
            $"heroes={catalog.Kits!.Count} profiles={catalog.Heroes!.Count} abilities={catalog.Abilities!.Count} items={catalog.Items!.Count}",
            $"kit abilities without data={catalog.Kits.Values.SelectMany(kit => kit.Abilities).Count(key => !catalog.Abilities.ContainsKey(key))}",
            $"ultimates={catalog.Abilities.Values.Count(a => a.Profile!.IsUltimate == true)} innate={catalog.Abilities.Values.Count(a => a.IsInnate)}",
            $"talents with text={catalog.Kits.Values.SelectMany(k => k.Talents).Count(t => t.Name is not null)} of {catalog.Kits.Values.Sum(k => k.Talents.Count)}, unresolved={catalog.Kits.Values.SelectMany(k => k.Talents).Count(t => t.Name?.Contains("{s:") == true)}",
            $"scepter={catalog.Kits.Values.Count(k => k.Scepter is not null)} shard={catalog.Kits.Values.Count(k => k.Shard is not null)}",
            $"descriptions with % left={catalog.Abilities.Values.Count(a => a.Description?.Contains('%') == true && System.Text.RegularExpressions.Regex.IsMatch(a.Description, "%[a-z_]+%"))}",
            $"items with active={catalog.Items.Values.Count(i => i.HasActive)} with components={catalog.Items.Values.Count(i => i.Components.Count > 0)}",
            $"lion talents: {string.Join(" | ", catalog.Kits[26].Talents.Select(t => t.Name))}",
            $"lion shard: {catalog.Kits[26].Shard?.Description}",
            $"disables through spell immunity: {string.Join(", ", catalog.Kits.Values.SelectMany(k => k.Abilities).Select(key => catalog.Abilities[key])
                .Where(a => a.PiercesSpellImmunity == true && AbilityEffects.Of(a).Any(AbilityEffects.IsDisable)).Select(a => a.Key).Take(12))}",
            $"global ranges: {catalog.Abilities.Values.Count(a => a.CastRangeAt(1) >= 25000)}",
            $"neutral tiers: {string.Join(" | ", catalog.Neutrals?.Tiers.Select(t => $"T{t.Tier} {t.OpensAtSeconds / 60}min cost {t.CraftCost} artifacts {t.Artifacts.Count} (in catalog {t.Artifacts.Count(catalog.Items.ContainsKey)})") ?? [])}");
        // .log, not .txt: the folder's .txt files are all read as game files (KeyValuesTests).
        File.WriteAllText(Path.Combine(folder, "mapping-report.log"), report);

        Assert.True(catalog.Kits.Count >= 127);
        Assert.Equal(catalog.Kits.Count, catalog.Heroes.Count);
        Assert.True(catalog.Items["black_king_bar"].HasActive);
    }

    private sealed class BonusComparer : IEqualityComparer<ValueBonus>
    {
        public bool Equals(ValueBonus? x, ValueBonus? y) =>
            x is not null && y is not null && x.Source == y.Source && x.Operation == y.Operation && x.Amount.SequenceEqual(y.Amount);

        public int GetHashCode(ValueBonus obj) => obj.Source.GetHashCode();
    }
}
