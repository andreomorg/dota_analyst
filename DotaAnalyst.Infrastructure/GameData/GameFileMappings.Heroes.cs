using System.Globalization;
using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Infrastructure.GameData;

public static partial class GameFileMappings
{
    private static HeroProfile ToHeroProfile(KeyValue hero, KeyValue? defaults)
    {
        HeroNumber? Number(string key)
        {
            var own = hero[key];
            var raw = own is not null ? own.Value : defaults?.Text(key);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? new HeroNumber(number, own is null ? HeroValueOrigin.HeroBase : HeroValueOrigin.HeroFile) : null;
        }

        string? Text(string key) => hero[key] is { } own ? own.Value : defaults?.Text(key);
        var enabled = Text("Enabled") switch { "1" => true, "0" => false, _ => (bool?)null };
        return new HeroProfile(hero.Key, enabled,
            new(Number("AttributeBaseStrength"), Number("AttributeStrengthGain"), Number("AttributeBaseAgility"),
                Number("AttributeAgilityGain"), Number("AttributeBaseIntelligence"), Number("AttributeIntelligenceGain")),
            new(Number("AttackDamageMin"), Number("AttackDamageMax"), Text("AttackDamageType"), Number("BaseAttackSpeed"),
                Number("AttackRate"), Number("AttackAnimationPoint"), Number("AttackRange"), Number("AttackRangeBuffer"), Number("ProjectileSpeed")),
            new(Number("ArmorPhysical"), Number("MagicalResistance")),
            new(Number("StatusHealth"), Number("StatusMana"), Number("StatusHealthRegen"), Number("StatusManaRegen")),
            new(Number("MovementSpeed"), Number("MovementTurnRate"), Number("VisionDaytimeRange"), Number("VisionNighttimeRange")));
    }
}
