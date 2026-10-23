namespace DotaAnalyst.Domain.Matches;

// Value object: a Dota match ID. It can only be created valid.
public readonly record struct MatchId
{
    public long Value { get; }

    private MatchId(long value) => Value = value;

    public static bool TryCreate(long value, out MatchId matchId)
    {
        if (value > 0)
        {
            matchId = new MatchId(value);
            return true;
        }

        matchId = default;
        return false;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
