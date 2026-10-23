namespace DotaAnalyst.Application.Matches;

/// <summary>Each of the hero's deaths at its moment.</summary>
/// <param name="Deaths">Deaths (Aegis deaths included).</param>
/// <param name="EscapeItemReady">Deaths with an escape or save item ready (in the inventory, no cooldown) a second before; null without inventories.</param>
/// <param name="BkbReady">Deaths with Black King Bar ready a second before; null without inventories.</param>
/// <param name="ToThreeOrMore">Deaths to 3 or more enemy heroes (killer and assists).</param>
/// <param name="WhileDisabled">Deaths with a stun, hex, silence or root on the hero; null without debuffs.</param>
/// <param name="DisabledSecondsBefore">Seconds disabled in the 10 s before each death, summed; null without debuffs.</param>
public record DeathContextSummary(int Deaths, int? EscapeItemReady, int? BkbReady, int ToThreeOrMore, int? WhileDisabled,
    double? DisabledSecondsBefore);
