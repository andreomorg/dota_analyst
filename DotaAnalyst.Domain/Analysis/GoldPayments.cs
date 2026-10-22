using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Individual log observations only. A family being present does not certify that it logs every kind of payment.
public sealed record GoldReasonPayments(uint? RawReason, int Observations, GoldChanges Changes);
public sealed record GoldPaymentWindow(bool Available, IReadOnlyList<GoldReasonPayments> Reasons,
    bool CompleteLedgerEstablished = false, bool BalanceDeltasVerified = false);

public static class GoldPayments
{
    public static GoldPaymentWindow Window(int heroId, double from, double to, IReadOnlyList<GoldEvent>? events)
    {
        // Reuse the same hero/window validation and checked arithmetic as the accounting contract.
        var window = GoldAccounting.Window(heroId, from, to, events, null);
        if (events is null) return new(false, []);
        var counts = events.Where(e => e.HeroId == heroId && e.Seconds >= from && e.Seconds <= to)
            .GroupBy(e => e.Reason).ToList();
        return new(true, window.Sources.Select(source => new GoldReasonPayments(source.RawReason,
            counts.Single(group => group.Key == source.RawReason).Count(), source.Changes)).ToList());
    }
}
