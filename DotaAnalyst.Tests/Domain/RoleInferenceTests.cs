using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class RoleInferenceTests
{
    private static LaningStats Hero(int heroId, Team team, Lane lane, int netWorth) => new(heroId, team, lane, 0, 0, netWorth, 0, 0);

    [Fact]
    public void AStandardLaning_GivesTheUsualRoles()
    {
        var roles = RoleInference.Infer(
        [
            Hero(1, Team.Radiant, Lane.Bottom, 4000), Hero(2, Team.Radiant, Lane.Bottom, 1800), // safe lane
            Hero(3, Team.Radiant, Lane.Middle, 4200),
            Hero(4, Team.Radiant, Lane.Top, 3500), Hero(5, Team.Radiant, Lane.Top, 2000),       // off lane
            Hero(6, Team.Dire, Lane.Top, 3900), Hero(7, Team.Dire, Lane.Top, 1700),             // Dire's safe lane
            Hero(8, Team.Dire, Lane.Middle, 4100),
            Hero(9, Team.Dire, Lane.Bottom, 3000), Hero(10, Team.Dire, Lane.Roaming, 2100),
        ]);

        Assert.Equal(
            new Dictionary<int, int> { [1] = 1, [2] = 5, [3] = 2, [4] = 3, [5] = 4, [6] = 1, [7] = 5, [8] = 2, [9] = 3, [10] = 4 },
            roles);
    }

    [Fact]
    public void HeroesLeft_TakeTheRolesLeft_RichestFirst()
    {
        // Nobody mid: the jungler (richest of those left) takes the 2, the roamer the 4.
        var roles = RoleInference.Infer(
        [
            Hero(1, Team.Radiant, Lane.Bottom, 4000), Hero(2, Team.Radiant, Lane.Bottom, 1800),
            Hero(3, Team.Radiant, Lane.Top, 3500),
            Hero(4, Team.Radiant, Lane.Jungle, 3700), Hero(5, Team.Radiant, Lane.Roaming, 1500),
        ]);

        Assert.Equal((1, 5, 3, 2, 4), (roles[1], roles[2], roles[3], roles[4], roles[5]));
    }
}
