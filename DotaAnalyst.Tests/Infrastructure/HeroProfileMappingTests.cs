using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.GameData;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class HeroProfileMappingTests
{
    [Fact]
    public void DuplicateHeroIdsAreRejectedAndMissingBaseDoesNotInventNumbers()
    {
        const string hero = "\"DOTAHeroes\" { \"npc_dota_hero_test\" { \"HeroID\" \"1\" \"AttributePrimary\" \"DOTA_ATTRIBUTE_STRENGTH\" \"AttackCapabilities\" \"DOTA_UNIT_CAP_MELEE_ATTACK\" } }";
        var texts = new Dictionary<string, string> { [GameFiles.HeroesFolder + "one.txt"] = hero };
        Assert.Null(GameFileMappings.ToCatalog(new(texts)).Heroes![1].Profile!.Defense.Armor);
        texts[GameFiles.HeroesFolder + "two.txt"] = hero;
        Assert.Throws<InvalidDataException>(() => GameFileMappings.ToCatalog(new(texts)));
    }

    [Fact]
    public void ProfileResolvesDefaultsButNeverReplacesMalformedOverrideOrExposesDummy()
    {
        var files = new GameFiles(new Dictionary<string, string>
        {
            [GameFiles.HeroesFolder + "npc_dota_hero_base.txt"] = """
                "DOTAHeroes" { "npc_dota_hero_base" { "ArmorPhysical" "-1" "MagicalResistance" "25" "BaseAttackSpeed" "100" "AttackRate" "1.7" } }
                """,
            [GameFiles.HeroesFolder + "hero.txt"] = """
                "DOTAHeroes" {
                  "npc_dota_hero_test" { "HeroID" "1" "Enabled" "1" "AttributePrimary" "DOTA_ATTRIBUTE_STRENGTH"
                    "AttackCapabilities" "DOTA_UNIT_CAP_MELEE_ATTACK" "AttributeBaseStrength" "0" "AttackRate" "bad" }
                  "npc_dota_hero_target_dummy" { "HeroID" "127" "Enabled" "0" "AttributePrimary" "DOTA_ATTRIBUTE_STRENGTH" "AttackCapabilities" "DOTA_UNIT_CAP_MELEE_ATTACK" }
                }
                """,
        });
        var catalog = GameFileMappings.ToCatalog(files);
        var profile = catalog.Heroes![1].Profile!;
        Assert.Equal(new HeroNumber(-1, HeroValueOrigin.HeroBase), profile.Defense.Armor);
        Assert.Equal(new HeroNumber(0, HeroValueOrigin.HeroFile), profile.Attributes.Strength);
        Assert.Null(profile.Attack.BaseAttackTime);
        Assert.Null(profile.Attributes.Intelligence);
        Assert.False(catalog.Heroes.ContainsKey(127));
        Assert.False(catalog.Kits!.ContainsKey(127));
    }
}
