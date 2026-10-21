using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// What the player was telling the hero to do (HeroOrders of the hero's samples).
public sealed partial class HeroSituationBuilder
{
    // The orders of the last five samples.
    public const int IntentWindowSeconds = 10;

    private Intent IntentOf(int heroId, Team team, PositionSample self, int seconds)
    {
        // Each sample carries the orders since the one before: the samples in (seconds - window, seconds].
        var orders = (positionsOf.GetValueOrDefault(heroId)?.Samples ?? [])
            .Where(sample => sample.Seconds > seconds - IntentWindowSeconds && sample.Seconds <= seconds)
            .Select(sample => sample.Orders)
            .ToList();
        if (orders.Count == 0 || orders.Any(order => order is null))
        {
            return Intent.Unknown;
        }

        var last = orders.LastOrDefault(order => order!.MoveX is not null && order.MoveY is not null);
        var destination = last is { MoveX: { } x, MoveY: { } y } ? new PositionSample(seconds, x, y) : (PositionSample?)null;
        return new Intent(
            Moves: orders.Sum(order => order!.Moves),
            Attacks: orders.Sum(order => order!.Attacks),
            Casts: orders.Sum(order => order!.Casts),
            DestinationDistance: destination is { } to ? Distance(self, to) : null,
            DestinationAdvance: destination is { } ahead
                ? FromOwnSide(team, ahead.X + ahead.Y) - FromOwnSide(team, self.X + self.Y)
                : null,
            DestinationOnEnemySide: destination is { } target ? MatchMetricsCalculator.IsEnemySide(team, target.X, target.Y) : null);
    }
}
