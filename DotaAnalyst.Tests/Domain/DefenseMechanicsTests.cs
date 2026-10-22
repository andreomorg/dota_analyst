using DotaAnalyst.Domain.Heroes;
namespace DotaAnalyst.Tests.Domain;

public sealed class DefenseMechanicsTests
{
    // The hero files of Client 6962 (15/10/2026), the build the rules were reviewed against.
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        ["scripts/npc/heroes/npc_dota_hero_slardar.txt"] = "3f77c59be82478d6468366d26c644b76286ead50",
        ["scripts/npc/heroes/npc_dota_hero_elder_titan.txt"] = "262dddd9425087018b61626785e8dbbd61c68bdb",
        ["scripts/npc/heroes/npc_dota_hero_drow_ranger.txt"] = "462efa9fe61e59644e2c84465daca3a0d03fafee",
    };

    [Fact]
    public void ReductionAndIgnoringBaseArmorAreDistinctAndRequireVerifiedContext()
    {
        var context = new ArmorInteractionContext(Reviewed, 28, 1, 10, 20, 8, 10, true, true, true);
        Assert.Equal(10, DefenseInteractions.ArmorFor("slardar_amplify_damage", context).ArmorForThisAttack);
        Assert.Equal(12, DefenseInteractions.ArmorFor("drow_ranger_marksmanship", context).ArmorForThisAttack);
        Assert.Equal(16, DefenseInteractions.ArmorFor("elder_titan_natural_order", context with { NormalizedMagnitude = 50 }).ArmorForThisAttack);
        Assert.Null(DefenseInteractions.ArmorFor("slardar_amplify_damage", context with { InputsVerified = false }).ArmorForThisAttack);
        Assert.Null(DefenseInteractions.ArmorFor("unknown_armor_ability", context).ArmorForThisAttack);
        Assert.Null(DefenseInteractions.ArmorFor("slardar_amplify_damage", context with { FileVersions = null }).ArmorForThisAttack);
        Assert.Null(DefenseInteractions.ArmorFor("drow_ranger_marksmanship", context with { BaseArmor = -1 }).ArmorForThisAttack);
        Assert.Equal(20, DefenseInteractions.ArmorFor("drow_ranger_marksmanship", context with { ProcOccurred = false }).ArmorForThisAttack);
    }

    [Fact]
    public void For_OnlyWhileTheHeroFileIsTheReviewedVersion()
    {
        // Client 6933 (also 7.41f) had another Drow Ranger file and the same Slardar file.
        var older = new Dictionary<string, string>(Reviewed)
        {
            ["scripts/npc/heroes/npc_dota_hero_drow_ranger.txt"] = "495d35b2",
        };

        Assert.Null(DefenseMechanic.For("drow_ranger_marksmanship", older));
        Assert.NotNull(DefenseMechanic.For("slardar_amplify_damage", older));
        Assert.Null(DefenseMechanic.For("slardar_amplify_damage", new Dictionary<string, string>()));
    }
}
