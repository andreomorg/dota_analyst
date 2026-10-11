using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class PlayerSlotTests
{
    [Theory]
    [InlineData(0, Team.Radiant)]
    [InlineData(4, Team.Radiant)]
    [InlineData(127, Team.Radiant)]
    [InlineData(128, Team.Dire)]
    [InlineData(132, Team.Dire)]
    public void ToTeam_UsesSlot128AsFirstDireSlot(int playerSlot, Team expected)
    {
        Assert.Equal(expected, PlayerSlot.ToTeam(playerSlot));
    }
}
