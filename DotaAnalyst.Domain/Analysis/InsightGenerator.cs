namespace DotaAnalyst.Domain.Analysis;

public enum InsightKind
{
    // Many deaths with no ally hero nearby.
    IsolatedDeaths,
    // A lot of time alone on the enemy's side of the map (more than the enemy of the same role), with deaths there.
    AloneOnEnemySide,
    // Net worth at 10:00 well below the enemy of the same role.
    BehindAtTen,
    // A support that placed far fewer observers than the enemy support of the same role.
    FewObservers,
    // Observers found and destroyed early on average.
    ShortObserverLife,
}

// One thing to improve: what it is and the numbers behind it (the text is the UI's). Counterpart: the same number
// for the enemy of the same role; null when the rule doesn't compare.
public record Insight(InsightKind Kind, double Value, double? Counterpart);

// Domain service: what the player could improve in a match, from their metrics and those of the enemy of the same
// role. Rules calibrated on real matches (work/sprint-17-smart-stats). Time on the enemy's side alone is not a rule:
// it mostly shows which team was winning.
public static class InsightGenerator
{
    public const int MinIsolatedDeaths = 2;
    public const double MinIsolatedShare = 0.4;
    public const double MinAloneShare = 0.10;
    public const double AloneVersusCounterpart = 1.5;
    public const int MinDeathsOnEnemySide = 2;
    public const double BehindAtTenRatio = 0.8;
    public const int FewerObservers = 3;
    // Observers last 360 s in the game; half of it on average means they are found early.
    public const int ShortObserverLifeSeconds = 180;
    public const int MinObserversForLifetime = 2;

    public static IReadOnlyList<Insight> Generate(HeroMetrics player, HeroMetrics counterpart)
    {
        var insights = new List<Insight>();

        if (player.IsolatedDeaths >= MinIsolatedDeaths && player.IsolatedDeaths >= MinIsolatedShare * player.Deaths)
        {
            insights.Add(new Insight(InsightKind.IsolatedDeaths, player.IsolatedDeaths, counterpart.IsolatedDeaths));
        }

        // Only when it cost deaths there: a winning carry farming the enemy's jungle alone is playing well.
        if (player.AloneOnEnemySideShare >= MinAloneShare
            && player.AloneOnEnemySideShare >= AloneVersusCounterpart * counterpart.AloneOnEnemySideShare
            && player.DeathsOnEnemySide >= MinDeathsOnEnemySide)
        {
            insights.Add(new Insight(InsightKind.AloneOnEnemySide, player.AloneOnEnemySideShare, counterpart.AloneOnEnemySideShare));
        }

        if (player.NetWorthAt10 is { } mine && counterpart.NetWorthAt10 is { } theirs && mine <= BehindAtTenRatio * theirs)
        {
            insights.Add(new Insight(InsightKind.BehindAtTen, mine, theirs));
        }

        var isSupport = player.Role >= 4;
        if (isSupport && player.ObserversPlaced <= counterpart.ObserversPlaced - FewerObservers)
        {
            insights.Add(new Insight(InsightKind.FewObservers, player.ObserversPlaced, counterpart.ObserversPlaced));
        }

        if (player.ObserversPlaced >= MinObserversForLifetime
            && player.ObserverAverageLifetimeSeconds is { } lifetime && lifetime < ShortObserverLifeSeconds)
        {
            insights.Add(new Insight(InsightKind.ShortObserverLife, lifetime, counterpart.ObserverAverageLifetimeSeconds));
        }

        return insights;
    }
}
