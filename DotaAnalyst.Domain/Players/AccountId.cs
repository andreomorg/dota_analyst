namespace DotaAnalyst.Domain.Players;

// Value object: a Dota account ID (32-bit Steam account number). It can only be created valid.
public readonly record struct AccountId
{
    // SteamID64 = account ID + this offset (individual accounts in the public universe).
    public const long SteamId64Offset = 76561197960265728;

    public long Value { get; }

    private AccountId(long value) => Value = value;

    // Accepts either a Dota account ID or a SteamID64.
    public static bool TryCreate(long accountIdOrSteamId64, out AccountId accountId)
    {
        var value = accountIdOrSteamId64 >= SteamId64Offset
            ? accountIdOrSteamId64 - SteamId64Offset
            : accountIdOrSteamId64;

        if (value is > 0 and <= uint.MaxValue)
        {
            accountId = new AccountId(value);
            return true;
        }

        accountId = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
