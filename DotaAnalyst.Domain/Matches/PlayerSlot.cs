namespace DotaAnalyst.Domain.Matches;

public static class PlayerSlot
{
    // Player slots 0-127 belong to Radiant, 128-255 to Dire.
    private const int FirstDireSlot = 128;

    public static Team ToTeam(int playerSlot) => playerSlot < FirstDireSlot ? Team.Radiant : Team.Dire;
}
