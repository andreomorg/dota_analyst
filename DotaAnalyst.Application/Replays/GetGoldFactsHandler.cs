using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

public sealed record GoldFacts(string MatchId, int HeroId, double DurationSeconds, GoldWindow Window)
{
    // A recorded sample at most two seconds old, never a promised current balance or buyback reserve.
    public GoldBalance? SampledBalance { get; init; }
    public BuybackWindow? Buybacks { get; init; }
    public GoldPaymentWindow? Payments { get; init; }
}
public sealed record GoldFactsResult(ReplayFactsStatus Status, GoldFacts? Facts, string? Problem = null);

// Observed changes only. Never loads catalogs, starts a parse, refreshes storage or reconstructs rewards from formulas.
public sealed class GetGoldFactsHandler(IArchivedReplaySource replays)
{
    public const int MaxWindowSeconds = 300;

    public async Task<GoldFactsResult> HandleAsync(MatchId matchId, int heroId, double seconds,
        int windowSeconds, CancellationToken cancellationToken)
    {
        if (heroId <= 0 || !double.IsFinite(seconds) || seconds < 0 || windowSeconds is < 0 or > MaxWindowSeconds)
            return new(ReplayFactsStatus.Invalid, null, $"heroId > 0, finite seconds ≥ 0 and windowSeconds 0-{MaxWindowSeconds}.");

        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay)
            return new(ReplayFactsStatus.NoDocument, null);

        if (replay.Metadata?.DurationSeconds is not { } duration || !double.IsFinite(duration) || duration < 0)
            return new(ReplayFactsStatus.Invalid, null, "The match duration is unknown.");
        if (seconds > duration)
            return new(ReplayFactsStatus.Invalid, null, "seconds must not exceed the real match duration.");
        if (!replay.Analysis.Players.Any(player => player.HeroId == heroId))
            return new(ReplayFactsStatus.Invalid, null, "The hero is not in the match.");

        var window = GoldAccounting.Window(heroId, Math.Max(0, seconds - windowSeconds), seconds,
            replay.Facts?.GoldEvents, replay.Activity?.Economy);
        var balance = replay.Facts?.GoldBalances?.Where(sample => sample.HeroId == heroId && sample.Seconds <= seconds
                && seconds - sample.Seconds <= 2).MaxBy(sample => sample.Seconds);
        return new(ReplayFactsStatus.Found, new(matchId.ToString(), heroId, duration, window)
        {
            SampledBalance = balance,
            Buybacks = BuybackEconomy.Window(heroId, window.From, seconds, replay.Activity?.Buybacks),
            Payments = GoldPayments.Window(heroId, window.From, seconds, replay.Facts?.GoldEvents),
        });
    }
}
