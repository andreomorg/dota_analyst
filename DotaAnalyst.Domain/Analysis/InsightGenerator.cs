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
    // Several deaths in the enemy's jungle, more than the enemy of the same role had in yours (added last, so the
    // numbers of the others stay the same).
    DeathsInEnemyJungle,
    // Away from most of the team's fights while the enemy of the same role was in them.
    LowFightPresence,
    // Died again soon after coming back, more than the enemy of the same role.
    QuickRedeaths,
    // Took much more damage from enemy heroes in the laning phase than the enemy of the same role (and than dealt).
    LostLaneTrades,
    // A support whose team had an observer up for little of the match, well less than the enemy team.
    LowObserverUptime,
    // Behind the enemy of the same role at 20:00, by more (relatively) than at 10:00: the gap grew after the lane.
    GapGrewAfterLane,
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
    public const int MinDeathsInEnemyJungle = 2;
    public const int MinTeamFights = 3;
    public const double LowFightPresenceShare = 0.5;
    public const double FightPresenceGap = 0.2;
    public const int MinQuickRedeaths = 2;
    // Everyone takes 1,500-4,500 from heroes by 10:00 in real matches (13/10/2026): below this, trades don't matter.
    public const int MinLaneDamageTaken = 1500;
    public const double LaneDamageTakenVersusCounterpart = 1.5;
    // Teams keep an observer up 65-98% of real matches (13/10/2026): below 70% is little.
    public const double LowObserverUptimeShare = 0.7;
    public const double ObserverUptimeGap = 0.15;
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

        if (player.DeathsByRegion?.EnemyJungle is { } jungleDeaths && jungleDeaths >= MinDeathsInEnemyJungle
            && jungleDeaths > (counterpart.DeathsByRegion?.EnemyJungle ?? 0))
        {
            insights.Add(new Insight(InsightKind.DeathsInEnemyJungle, jungleDeaths, counterpart.DeathsByRegion?.EnemyJungle));
        }

        if (player.NetWorthAt10 is { } mine && counterpart.NetWorthAt10 is { } theirs && mine <= BehindAtTenRatio * theirs)
        {
            insights.Add(new Insight(InsightKind.BehindAtTen, mine, theirs));
        }

        var isSupport = player.Role >= 4;
        // Fewer observers only matters when the team also had less vision (a well-covered team is not a mistake).
        if (isSupport && player.ObserversPlaced <= counterpart.ObserversPlaced - FewerObservers
            && !(player.TeamObserverUptime >= counterpart.TeamObserverUptime))
        {
            insights.Add(new Insight(InsightKind.FewObservers, player.ObserversPlaced, counterpart.ObserversPlaced));
        }

        if (player.ObserversPlaced >= MinObserversForLifetime
            && player.ObserverAverageLifetimeSeconds is { } lifetime && lifetime < ShortObserverLifeSeconds)
        {
            insights.Add(new Insight(InsightKind.ShortObserverLife, lifetime, counterpart.ObserverAverageLifetimeSeconds));
        }

        if (player is { TeamFights: >= MinTeamFights and var fights, FightsNearby: { } near }
            && counterpart.FightsNearby is { } theirNear && counterpart.TeamFights is { } theirFights)
        {
            var myPresence = (double)near / fights;
            var theirPresence = theirFights == 0 ? 0 : (double)theirNear / theirFights;
            if (myPresence < LowFightPresenceShare && theirPresence >= myPresence + FightPresenceGap)
            {
                insights.Add(new Insight(InsightKind.LowFightPresence, myPresence, theirPresence));
            }
        }

        if (player.QuickRedeaths is >= MinQuickRedeaths and var quick && quick > (counterpart.QuickRedeaths ?? 0))
        {
            insights.Add(new Insight(InsightKind.QuickRedeaths, quick, counterpart.QuickRedeaths));
        }

        if (player.LaneDamageTaken is { } taken && player.LaneDamageDealt is { } dealt
            && counterpart.LaneDamageTaken is { } theirTaken
            && taken >= MinLaneDamageTaken && taken > dealt && taken >= LaneDamageTakenVersusCounterpart * theirTaken)
        {
            insights.Add(new Insight(InsightKind.LostLaneTrades, taken, theirTaken));
        }

        if (isSupport && player.TeamObserverUptime is { } uptime && counterpart.TeamObserverUptime is { } theirUptime
            && uptime < LowObserverUptimeShare && theirUptime >= uptime + ObserverUptimeGap)
        {
            insights.Add(new Insight(InsightKind.LowObserverUptime, uptime, theirUptime));
        }

        // Net worth ratio to the counterpart at 10:00 and 20:00 (only matches that reached 20:00).
        if (player is { NetWorthAt10: { } mine10 and > 0, NetWorthAt20: { } mine20 }
            && counterpart is { NetWorthAt10: { } theirs10 and > 0, NetWorthAt20: { } theirs20 and > 0 })
        {
            var ratio10 = (double)mine10 / theirs10;
            var ratio20 = (double)mine20 / theirs20;
            if (ratio20 <= BehindAtTenRatio && ratio20 < ratio10)
            {
                insights.Add(new Insight(InsightKind.GapGrewAfterLane, mine20, theirs20));
            }
        }

        return insights;
    }
}
