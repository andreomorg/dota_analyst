using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Tests.Domain;

public class AccountIdTests
{
    [Theory]
    [InlineData(87278757, 87278757)]                   // already an account ID
    [InlineData(76561198047544485, 87278757)]          // SteamID64 of the same account
    [InlineData(4294967295, 4294967295)]               // largest 32-bit account ID
    public void TryCreate_AcceptsAccountIdOrSteamId64(long id, long expected)
    {
        Assert.True(AccountId.TryCreate(id, out var accountId));
        Assert.Equal(expected, accountId.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967296)]                           // too large for an account ID, too small for a SteamID64
    [InlineData(76561197960265728)]                    // SteamID64 offset itself = account ID 0
    [InlineData(long.MaxValue)]
    public void TryCreate_RejectsInvalidIds(long id)
    {
        Assert.False(AccountId.TryCreate(id, out _));
    }
}
