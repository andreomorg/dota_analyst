using System.Globalization;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Infrastructure.GameData;

// The game files of one build, by their path inside the game's "dota" folder (scripts/npc/heroes/npc_dota_hero_lion.txt).
public sealed record GameFiles(IReadOnlyDictionary<string, string> Texts)
{
    public const string Abilities = "scripts/npc/npc_abilities.txt";
    public const string Items = "scripts/npc/items.txt";
    public const string Translation = "resource/localization/abilities_english.txt";
    public const string NeutralItems = "scripts/npc/neutral_items.txt";
    public const string HeroesFolder = "scripts/npc/heroes/";

    // The paths a build needs besides the hero files.
    public static readonly IReadOnlyList<string> CommonPaths = [Abilities, Items, Translation, NeutralItems];

    public static bool IsNeeded(string path) =>
        CommonPaths.Contains(path) || (path.StartsWith(HeroesFolder, StringComparison.Ordinal) && path.EndsWith(".txt", StringComparison.Ordinal));
}

// Turns the game's own files (Valve KeyValues, KeyValues.Parse) into the Domain's game data: every ability with its
// full profile (AbilityProfile), each hero's kit, profile and talents with numbers, and every item. The English
// translation gives names, descriptions (with %value% placeholders filled with the levels), lore, notes, value labels
// and the Scepter/Shard texts. Anything unexpected in one entry only loses that entry's field, never the build.
public static partial class GameFileMappings
{
    private const string AbilityPrefix = "DOTA_ABILITY_BEHAVIOR_";
    private const string TooltipPrefix = "DOTA_Tooltip_ability_";
    private const string ItemPrefix = "item_";

    public static GameCatalog ToCatalog(GameFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var translation = Translation(files);
        var definitions = new Dictionary<string, KeyValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var ability in Root(files, GameFiles.Abilities)?.Children.Where(child => child.IsBlock) ?? [])
        {
            definitions[ability.Key] = ability;
        }

        var heroes = files.Texts.Where(file => file.Key.StartsWith(GameFiles.HeroesFolder, StringComparison.Ordinal))
            .SelectMany(file => KeyValues.Parse(file.Value).Where(entry => entry.IsBlock).SelectMany(entry => entry.Children))
            .Where(hero => hero.IsBlock && hero.Key.StartsWith("npc_dota_hero_", StringComparison.Ordinal) && Integer(hero.Text("HeroID")) is > 0)
            .ToList();
        foreach (var ability in heroes.SelectMany(hero => hero["AbilityDefinitions"]?.Children ?? []).Where(child => child.IsBlock))
        {
            definitions[ability.Key] = ability;
        }

        var abilities = definitions.ToDictionary(pair => pair.Key, pair => ToAbility(pair.Value, translation), StringComparer.OrdinalIgnoreCase);
        var kits = new Dictionary<int, HeroKit>();
        var heroDetails = new Dictionary<int, HeroDetails>();
        var heroBase = files.Texts.Where(file => file.Key == GameFiles.HeroesFolder + "npc_dota_hero_base.txt")
            .SelectMany(file => KeyValues.Parse(file.Value).Where(entry => entry.IsBlock).SelectMany(entry => entry.Children))
            .FirstOrDefault(hero => hero.Key == "npc_dota_hero_base");
        foreach (var hero in heroes)
        {
            // Retain old fixtures without Enabled, but never expose explicitly disabled technical units.
            if (hero.Text("Enabled") == "0") continue;
            var id = Integer(hero.Text("HeroID"))!.Value;
            if (kits.ContainsKey(id)) throw new InvalidDataException($"Duplicate hero ID {id} in the game build.");
            kits[id] = ToKit(id, hero, abilities, translation);
            if (ToHeroDetails(hero, heroBase) is { } details)
            {
                heroDetails[id] = details with { Profile = ToHeroProfile(hero, heroBase) };
            }
        }

        var itemsRoot = Root(files, GameFiles.Items);
        var items = ToItems(itemsRoot, translation);
        return new GameCatalog(heroDetails, items, abilities, kits, ToNeutralRules(Root(files, GameFiles.NeutralItems), itemsRoot));
    }

    // neutral_items.txt: "neutral_tiers" { "1" { start_time "0:00", trinket_options, enhancement_options, craft_cost,
    // xp_bonus, items { item_x "1" }, enhancements { global/strength/agility/intelligence/universal { item_enhancement_y
    // "<level>" } } } } and "madstone_limits" { madstone_no_limit_time "70:00" }; the Madstone drop comes from
    // item_madstone_bundle in items.txt. Null without the file (builds kept before it, or the OpenDota catalogs).
    internal static NeutralRules? ToNeutralRules(KeyValue? root, KeyValue? items)
    {
        if (root?["neutral_tiers"] is not { } tiers)
        {
            return null;
        }

        static string Key(string key) => key.StartsWith(ItemPrefix, StringComparison.Ordinal) ? key[ItemPrefix.Length..] : key;
        static List<NeutralEnhancement> Enhancements(KeyValue? block) =>
            (block?.Children ?? []).Where(child => !child.IsBlock)
                .Select(child => new NeutralEnhancement(Key(child.Key), Integer(child.Value) ?? 1)).ToList();

        var result = new List<NeutralTier>();
        foreach (var tier in tiers.Children.Where(child => child.IsBlock && Integer(child.Key) is > 0))
        {
            var enhancements = tier["enhancements"];
            var byAttribute = new Dictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>>
            {
                [HeroAttribute.Strength] = Enhancements(enhancements?["strength"]),
                [HeroAttribute.Agility] = Enhancements(enhancements?["agility"]),
                [HeroAttribute.Intelligence] = Enhancements(enhancements?["intelligence"]),
                [HeroAttribute.Universal] = Enhancements(enhancements?["universal"]),
            };
            result.Add(new NeutralTier(
                Integer(tier.Key)!.Value,
                Clock(tier.Text("start_time")) ?? 0,
                Integer(tier.Text("trinket_options")) ?? 0,
                Integer(tier.Text("enhancement_options")) ?? 0,
                Integer(tier.Text("craft_cost")) ?? 0,
                Levels(tier.Text("xp_bonus")) is [var xpBonus, ..] ? xpBonus : 0,
                (tier["items"]?.Children ?? []).Where(child => !child.IsBlock).Select(child => Key(child.Key)).ToList(),
                Enhancements(enhancements?["global"]),
                byAttribute));
        }

        var madstone = items?["item_madstone_bundle"]?["AbilityValues"];
        return new NeutralRules(result.OrderBy(tier => tier.Tier).ToList(), Clock(root["madstone_limits"]?.Text("madstone_no_limit_time")),
            Levels(madstone?.Text("madstone_primary")), Levels(madstone?.Text("madstone_secondary")));
    }

    // "70:00" -> 4200 seconds.
    private static int? Clock(string? text) =>
        text?.Split(':') is [var minutes, var seconds] && Integer(minutes) is { } m && Integer(seconds) is { } s ? m * 60 + s : null;

    // The first block of a file (DOTAAbilities, DOTAHeroes...); null when the build lacks the file.
    private static KeyValue? Root(GameFiles files, string path) =>
        files.Texts.TryGetValue(path, out var text) ? KeyValues.Parse(text).FirstOrDefault(entry => entry.IsBlock) : null;

    private static Dictionary<string, string> Translation(GameFiles files)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Root(files, GameFiles.Translation)?["Tokens"]?.Children.Where(child => !child.IsBlock) ?? [])
        {
            tokens.TryAdd(token.Key, token.Value!);
        }

        return tokens;
    }

    internal static AbilityDetails ToAbility(KeyValue block, IReadOnlyDictionary<string, string> translation)
    {
        var key = block.Key;
        var flags = Flags(block.Text("AbilityBehavior"), AbilityPrefix);
        var values = Values(block, key, translation);
        IReadOnlyList<double> Field(string name) =>
            Levels(block.Text(name)) is { Count: > 0 } top ? top : values.FirstOrDefault(value => value.Key.Equals(name, StringComparison.OrdinalIgnoreCase))?.Levels ?? [];
        string? Tooltip(string suffix) => translation.GetValueOrDefault(TooltipPrefix + key + suffix);
        var type = block.Text("AbilityType");
        var immunity = block.Text("SpellImmunityType") switch
        {
            "SPELL_IMMUNITY_ENEMIES_YES" => SpellImmunity.EnemiesYes,
            "SPELL_IMMUNITY_ENEMIES_NO" => SpellImmunity.EnemiesNo,
            "SPELL_IMMUNITY_ALLIES_YES" => SpellImmunity.AlliesYes,
            "SPELL_IMMUNITY_ALLIES_NO" => SpellImmunity.AlliesNo,
            "SPELL_IMMUNITY_ALLIES_YES_ENEMIES_NO" => SpellImmunity.AlliesYesEnemiesNo,
            _ => SpellImmunity.Unknown,
        };
        var notes = Enumerable.Range(0, 20).Select(index => Tooltip($"_Note{index}")).TakeWhile(note => note is not null)
            .Select(note => Readable(note!, values)).ToList();

        var profile = new AbilityProfile(
            Behaviors: flags,
            TargetFlags: Flags(block.Text("AbilityUnitTargetFlags"), "DOTA_UNIT_TARGET_FLAG_"),
            SpellImmunity: immunity,
            IsUltimate: type == "ABILITY_TYPE_ULTIMATE",
            IsTalent: type == "ABILITY_TYPE_ATTRIBUTES",
            MaxLevel: Integer(block.Text("MaxLevel")),
            IsGrantedByScepter: block.Text("IsGrantedByScepter") == "1",
            IsGrantedByShard: block.Text("IsGrantedByShard") == "1",
            HasScepterUpgrade: block.Text("HasScepterUpgrade") == "1",
            HasShardUpgrade: block.Text("HasShardUpgrade") == "1",
            CastRange: Field("AbilityCastRange"),
            CastPoint: Field("AbilityCastPoint"),
            ChannelTime: Field("AbilityChannelTime"),
            Duration: Field("AbilityDuration"),
            HealthCost: Field("AbilityHealthCost"),
            Charges: Field("AbilityCharges"),
            ChargeRestoreTime: Field("AbilityChargeRestoreTime"),
            LinkedAbility: block.Text("LinkedAbility") is { Length: > 0 } linked ? linked : null,
            IsBreakable: block.Text("IsBreakable") is { } breakable ? breakable == "1" : null,
            Notes: notes,
            ScepterDescription: (Tooltip("_scepter_description") ?? Tooltip("_scepter_Description")) is { } scepter ? Readable(scepter, values) : null,
            ShardDescription: (Tooltip("_shard_description") ?? Tooltip("_shard_Description")) is { } shard ? Readable(shard, values) : null);

        var teams = Flags(block.Text("AbilityUnitTargetTeam"), "DOTA_UNIT_TARGET_TEAM_");
        var team = teams.Contains("BOTH") || (teams.Contains("ENEMY") && teams.Contains("FRIENDLY")) ? AbilityTargetTeam.Both
            : teams.Contains("ENEMY") ? AbilityTargetTeam.Enemy : teams.Contains("FRIENDLY") ? AbilityTargetTeam.Ally
            : teams.Contains("CUSTOM") ? AbilityTargetTeam.Custom : AbilityTargetTeam.None;
        return new AbilityDetails(
            key,
            Tooltip("") ?? key,
            Tooltip("_Description") is { } description ? Readable(description, values) : null,
            flags.Select(DisplayBehavior).OfType<string>().ToList(),
            team,
            Flags(block.Text("AbilityUnitTargetType"), "DOTA_UNIT_TARGET_").Select(Title).ToList(),
            block.Text("AbilityUnitDamageType") switch
            {
                "DAMAGE_TYPE_MAGICAL" => AbilityDamageType.Magical,
                "DAMAGE_TYPE_PHYSICAL" => AbilityDamageType.Physical,
                "DAMAGE_TYPE_PURE" => AbilityDamageType.Pure,
                _ => AbilityDamageType.None,
            },
            immunity switch
            {
                SpellImmunity.EnemiesYes => true,
                SpellImmunity.EnemiesNo or SpellImmunity.AlliesYesEnemiesNo => false,
                _ => null,
            },
            block.Text("SpellDispellableType") switch
            {
                "SPELL_DISPELLABLE_YES" => Dispellable.Yes,
                "SPELL_DISPELLABLE_NO" => Dispellable.No,
                "SPELL_DISPELLABLE_YES_STRONG" => Dispellable.StrongOnly,
                _ => Dispellable.Unknown,
            },
            Field("AbilityCooldown"),
            Field("AbilityManaCost"),
            values,
            IsInnate: block.Text("Innate") == "1",
            Lore: Tooltip("_Lore"),
            BaseDamage: Field("AbilityDamage"),
            Profile: profile);
    }

    // Each entry of AbilityValues: "duration" "1.3 1.6" or a block with "value", the talent bonuses
    // (special_bonus_unique_lion_2 "+600"), special_bonus_scepter/shard, RequiresScepter/Shard and
    // affected_by_aoe_increase. The label comes from the translation ("%MOVEMENT SLOW:" = a percentage).
    private static List<AbilityValue> Values(KeyValue block, string abilityKey, IReadOnlyDictionary<string, string> translation)
    {
        var result = new List<AbilityValue>();
        foreach (var entry in block["AbilityValues"]?.Children ?? [])
        {
            var raw = entry.IsBlock ? entry.Text("value") : entry.Value;
            var levels = Levels(raw);
            var header = translation.GetValueOrDefault(TooltipPrefix + abilityKey + "_" + entry.Key);
            var percent = header?.StartsWith('%') == true;
            ValueRules? rules = null;
            if (entry.IsBlock)
            {
                var bonuses = entry.Children.Where(child => !child.IsBlock && child.Key.StartsWith("special_bonus_", StringComparison.OrdinalIgnoreCase))
                    .Select(child => Bonus(child.Key switch
                    {
                        var k when k.Equals("special_bonus_scepter", StringComparison.OrdinalIgnoreCase) => ValueBonus.Scepter,
                        var k when k.Equals("special_bonus_shard", StringComparison.OrdinalIgnoreCase) => ValueBonus.Shard,
                        var k => k,
                    }, child.Value!))
                    .OfType<ValueBonus>().ToList();
                rules = new ValueRules(bonuses, entry.Text("RequiresScepter") == "1", entry.Text("RequiresShard") == "1",
                    entry.Text("affected_by_aoe_increase") == "1");
            }

            result.Add(new AbilityValue(entry.Key, header?.TrimStart('%', '$'), levels,
                levels.Count > 0 || raw is null ? null : raw, percent, Generated: false, rules));
        }

        return result;
    }

    // "+600" adds, "-2" subtracts, "x2" multiplies, "=5" or "5" replaces; per level when it has several numbers.
    private static ValueBonus? Bonus(string source, string text)
    {
        var trimmed = text.Trim();
        var (operation, numbers) = trimmed switch
        {
            ['x' or 'X', .. var rest] => (BonusOperation.Multiply, rest),
            ['=', .. var rest] => (BonusOperation.Replace, rest),
            ['+', .. var rest] => (BonusOperation.Add, rest),
            ['-', ..] => (BonusOperation.Add, trimmed),
            _ => (BonusOperation.Replace, trimmed),
        };
        var levels = Levels(numbers);
        return levels.Count == 0 ? null : new ValueBonus(source, operation, levels);
    }

    private static HeroKit ToKit(int heroId, KeyValue hero, IReadOnlyDictionary<string, AbilityDetails> abilities,
        IReadOnlyDictionary<string, string> translation)
    {
        var talentStart = Integer(hero.Text("AbilityTalentStart")) ?? 10;
        var slots = hero.Children.Where(child => !child.IsBlock && child.Key.StartsWith("Ability", StringComparison.Ordinal)
                && Integer(child.Key["Ability".Length..]) is not null)
            .Select(child => (Index: Integer(child.Key["Ability".Length..])!.Value, Key: child.Value!))
            .Where(slot => slot.Key.Length > 0 && slot.Key != "generic_hidden")
            .OrderBy(slot => slot.Index).ToList();
        var kitAbilities = slots.Where(slot => slot.Index < talentStart).Select(slot => slot.Key).ToList();
        var heroValues = kitAbilities.Select(key => abilities.GetValueOrDefault(key)).OfType<AbilityDetails>().ToList();
        var talents = slots.Where(slot => slot.Index >= talentStart)
            .Select(slot => new Talent(slot.Key, (slot.Index - talentStart) / 2 + 1, TalentText(slot.Key, heroValues, abilities, translation)))
            .ToList();

        var facets = (hero["Facets"]?.Children ?? []).Where(child => child.IsBlock).Select((facet, index) => new Facet(
            index,
            facet.Key,
            translation.GetValueOrDefault("DOTA_Tooltip_Facet_" + facet.Key),
            translation.GetValueOrDefault("DOTA_Tooltip_Facet_" + facet.Key + "_Description") is { } text
                ? Readable(text, heroValues.SelectMany(ability => ability.Values).ToList()) : null,
            facet.Text("Deprecated") == "true",
            facet.Text("Icon"),
            facet.Text("Color"),
            (facet["Abilities"]?.Children ?? []).Where(child => !child.IsBlock).Select(child => child.Value!).ToList())).ToList();

        AghanimUpgrade? Upgrade(Func<AbilityProfile, bool> granted, Func<AbilityProfile, bool> upgraded, Func<AbilityProfile, string?> text) =>
            heroValues.Concat(facets.SelectMany(facet => facet.Abilities).Select(key => abilities.GetValueOrDefault(key)).OfType<AbilityDetails>())
                .Where(ability => ability.Profile is { } profile && (granted(profile) || upgraded(profile)) && text(profile) is not null)
                .Select(ability => new AghanimUpgrade(text(ability.Profile!)!, ability.Name, granted(ability.Profile!)))
                .FirstOrDefault();
        return new HeroKit(heroId, kitAbilities, talents, facets,
            Upgrade(profile => profile.IsGrantedByScepter, profile => profile.HasScepterUpgrade, profile => profile.ScepterDescription),
            Upgrade(profile => profile.IsGrantedByShard, profile => profile.HasShardUpgrade, profile => profile.ShardDescription));
    }

    // "+{s:bonus_movespeed}% Mana Drain Slow": bonus_X is this talent's bonus on the hero's value X (Mana Drain's
    // movespeed "+10"); any other name is the talent's own value (generic talents: "value" "20").
    private static string? TalentText(string talent, IReadOnlyList<AbilityDetails> heroAbilities,
        IReadOnlyDictionary<string, AbilityDetails> abilities, IReadOnlyDictionary<string, string> translation)
    {
        if (translation.GetValueOrDefault(TooltipPrefix + talent) is not { } text)
        {
            return null;
        }

        var own = abilities.GetValueOrDefault(talent)?.Values ?? [];
        return Placeholder().Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            var valueKey = name.StartsWith("bonus_", StringComparison.OrdinalIgnoreCase) ? name["bonus_".Length..] : name;
            var bonus = heroAbilities.SelectMany(ability => ability.Values)
                .Where(value => value.Key.Equals(valueKey, StringComparison.OrdinalIgnoreCase) || value.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Rules?.BonusFrom(talent)).OfType<ValueBonus>().FirstOrDefault();
            if (bonus is not null)
            {
                return Number(Math.Abs(bonus.Amount[0]));
            }

            return own.FirstOrDefault(value => value.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Levels.Count: > 0 } ownValue
                ? string.Join(" / ", ownValue.Levels.Select(Number))
                : match.Value;
        });
    }

    private static HeroDetails? ToHeroDetails(KeyValue hero, KeyValue? defaults = null)
    {
        string? Text(string key) => hero[key] is { } own ? own.Value : defaults?.Text(key);
        HeroAttribute? attribute = Text("AttributePrimary") switch
        {
            "DOTA_ATTRIBUTE_STRENGTH" => HeroAttribute.Strength,
            "DOTA_ATTRIBUTE_AGILITY" => HeroAttribute.Agility,
            "DOTA_ATTRIBUTE_INTELLECT" => HeroAttribute.Intelligence,
            "DOTA_ATTRIBUTE_ALL" => HeroAttribute.Universal,
            _ => null,
        };
        HeroAttackType? attack = Text("AttackCapabilities") switch
        {
            "DOTA_UNIT_CAP_MELEE_ATTACK" => HeroAttackType.Melee,
            "DOTA_UNIT_CAP_RANGED_ATTACK" => HeroAttackType.Ranged,
            _ => null,
        };
        if (attribute is null || attack is null)
        {
            return null;
        }

        var roles = (Text("Role") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(role => Enum.TryParse<HeroRole>(role, ignoreCase: true, out var parsed) ? parsed : (HeroRole?)null)
            .OfType<HeroRole>().ToHashSet();
        return new HeroDetails(attribute.Value, attack.Value, roles);
    }

    // Items: key without item_, cost, components (from the recipe whose ItemResult is the item: its ItemRequirements,
    // without the recipe, as the OpenDota constants list them), consumable (ItemQuality consumable), behavior, cooldown, mana, values
    // and the active/passive parts from the translated text (<h1>Active: Avatar</h1> ...).
    private static Dictionary<string, ItemDetails> ToItems(KeyValue? root, IReadOnlyDictionary<string, string> translation)
    {
        var blocks = (root?.Children ?? []).Where(child => child.IsBlock && child.Key.StartsWith(ItemPrefix, StringComparison.Ordinal)).ToList();
        var components = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in blocks.Where(block => block.Text("ItemRecipe") == "1" && block.Text("ItemResult") is not null))
        {
            var result = recipe.Text("ItemResult")![ItemPrefix.Length..];
            var parts = (recipe["ItemRequirements"]?.Children ?? []).Select(child => child.Value).OfType<string>().FirstOrDefault()?
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.TrimEnd('*').StartsWith(ItemPrefix, StringComparison.Ordinal) ? part.TrimEnd('*')[ItemPrefix.Length..] : part)
                .ToList() ?? [];
            components.TryAdd(result, parts);
        }

        var items = new Dictionary<string, ItemDetails>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            var ability = ToAbility(block, translation);
            var key = block.Key[ItemPrefix.Length..];
            var description = translation.GetValueOrDefault(TooltipPrefix + block.Key + "_Description");
            items[key] = new ItemDetails(key, Math.Max(0, Integer(block.Text("ItemCost")) ?? 0), components.GetValueOrDefault(key),
                block.Text("ItemQuality") == "consumable")
            {
                Abilities = ItemParts(description, ability.Values),
                Behavior = ability.Behavior,
                Cooldown = ability.Cooldown.Count > 0 ? ability.Cooldown[0] : null,
                ManaCost = ability.ManaCost.Count > 0 ? ability.ManaCost[0] : null,
                Values = ability.Values,
                HasCharges = Integer(block.Text("ItemInitialCharges")) is > 0 || block.Text("ItemRequiresCharges") == "1",
            };
        }

        return items;
    }

    private static List<ItemAbility> ItemParts(string? description, IReadOnlyList<AbilityValue> values)
    {
        if (description is null)
        {
            return [];
        }

        var parts = new List<ItemAbility>();
        foreach (Match part in ItemSection().Matches(description))
        {
            var kind = part.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "active" => ItemAbilityKind.Active,
                "passive" => ItemAbilityKind.Passive,
                "toggle" => ItemAbilityKind.Toggle,
                "use" => ItemAbilityKind.Use,
                "upgrade" => ItemAbilityKind.Upgrade,
                _ => ItemAbilityKind.Other,
            };
            parts.Add(new ItemAbility(kind, part.Groups["title"].Value.Trim(), Readable(part.Groups["text"].Value, values)));
        }

        return parts;
    }

    // %value% and {s:value} become the levels ("105 / 170 / 235 / 300"; a value that only exists with Scepter/Shard,
    // "special_bonus_shard" "2", gives its bonus), %% a percent sign, <br> a new line; other markup is removed.
    internal static string Readable(string text, IReadOnlyList<AbilityValue> values)
    {
        string LevelsOf(string name)
        {
            var value = values.FirstOrDefault(value => value.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            var levels = value?.Levels is { Count: > 0 } own ? own : value?.Rules?.Bonuses is [var bonus, ..] ? bonus.Amount.Select(Math.Abs).ToList() : null;
            return levels is { Count: > 0 }
                ? string.Join(" / ", levels.Distinct().Count() == 1 ? [Number(levels[0])] : levels.Select(Number))
                : name;
        }
        var filled = Percent().Replace(text, match => match.Groups["name"].Length == 0 ? "%" : LevelsOf(match.Groups["name"].Value));
        filled = Placeholder().Replace(filled, match => LevelsOf(match.Groups["name"].Value));
        filled = LineBreak().Replace(filled, "\n");
        return Markup().Replace(filled, "").Trim();
    }

    // "DOTA_ABILITY_BEHAVIOR_POINT | DOTA_ABILITY_BEHAVIOR_AOE" -> [POINT, AOE]; tolerant of missing bars and spaces.
    private static List<string> Flags(string? text, string prefix) =>
        (text ?? "").Split(['|', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(flag => flag.StartsWith(prefix, StringComparison.Ordinal) ? flag[prefix.Length..] : flag)
            .Where(flag => flag.Length > 0 && flag != "NONE").Distinct().ToList();

    // The names the OpenDota constants use, so the rules written for them (IsPassive, AbilityEffects) read both sources.
    private static string? DisplayBehavior(string flag) => flag switch
    {
        "PASSIVE" => "Passive",
        "UNIT_TARGET" => "Unit Target",
        "POINT" => "Point Target",
        "NO_TARGET" => "No Target",
        "AOE" => "AOE",
        "CHANNELLED" => "Channeled",
        "AUTOCAST" => "Autocast",
        "ATTACK" => "Attack Modifier",
        "HIDDEN" => "Hidden",
        "IMMEDIATE" => "Instant Cast",
        "TOGGLE" => "Toggle",
        "AURA" => "Aura",
        "DIRECTIONAL" => "Directional",
        "VECTOR_TARGETING" => "Vector Targeting",
        _ => null,
    };

    // HERO -> Hero, CREEP_HERO -> Creep Hero.
    private static string Title(string flag) =>
        string.Join(' ', flag.Split('_').Select(word => word.Length == 0 ? word : word[0] + word[1..].ToLowerInvariant()));

    // "1.3 1.6 1.9 2.2" -> four levels; empty when any part is not a number.
    internal static IReadOnlyList<double> Levels(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<double>();
        foreach (var part in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part.TrimEnd('f'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                return [];
            }

            result.Add(number);
        }

        return result;
    }

    private static int? Integer(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"%(?<name>[A-Za-z0-9_]*)%")]
    private static partial Regex Percent();

    [GeneratedRegex(@"\{s:(?<name>[A-Za-z0-9_]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"<h1>\s*(?<kind>Active|Passive|Toggle|Use|Upgrade|Aura)\s*:\s*(?<title>[^<]*)</h1>(?<text>.*?)(?=<h1>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItemSection();
}
