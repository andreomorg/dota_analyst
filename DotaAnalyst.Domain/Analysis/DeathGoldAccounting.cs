using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

public sealed record LossCounterExplanation(double Seconds, int CounterLoss, long? NominalLogLoss,
    int? BeforeUnreliable, double PreviousAt, bool? ConsistentWithUnreliableCap);
public sealed record DeathGoldWindow(bool CounterAvailable, bool LogAvailable, long? CounterLoss,
    IReadOnlyList<LossCounterExplanation> Changes, int? UnassociatedLossLogs);

public static class DeathGoldAccounting
{
    public static DeathGoldWindow Window(int heroId, double from, double to,
        IReadOnlyList<GoldLossObservation>? changes, IReadOnlyList<GoldEvent>? events)
    {
        // Validate the same closed window as the economy API without interpreting log amounts as balance deltas.
        GoldAccounting.Window(heroId, from, to, null, null);
        var rows = changes?.Where(c => c.HeroId == heroId && c.Seconds >= from && c.Seconds <= to).ToList();
        var logs = events?.Where(e => e.HeroId == heroId && e.Reason == 1 && e.Amount < 0 && e.Seconds >= from && e.Seconds <= to).ToList();
        var explained = new List<LossCounterExplanation>();
        var associated = new HashSet<GoldEvent>();
        foreach (var row in rows ?? [])
        {
            var candidates = logs?.Where(log => Math.Abs(log.Seconds - row.Seconds) <= 1).ToList();
            // Symmetric uniqueness: the log must not also be a candidate for another counter transition.
            var log = candidates is { Count: 1 } && changes!.Count(other => other.HeroId == heroId && other.Seconds <= to
                && Math.Abs(other.Seconds - candidates[0].Seconds) <= 1) == 1 ? candidates[0] : null;
            long? nominal = log is null ? null : checked(-log.Amount);
            if (log is not null) associated.Add(log);
            var consistent = nominal is { } n && row.BeforeUnreliable is { } gold
                ? (bool?)(Math.Min(n, gold) == row.Lost) : null;
            explained.Add(new(row.Seconds, row.Lost, nominal, row.BeforeUnreliable, row.PreviousAt, consistent));
        }
        return new(changes is not null, events is not null,
            rows?.Aggregate(0L, (sum, row) => checked(sum + row.Lost)), explained,
            logs is null ? null : logs.Count - associated.Count);
    }
}
