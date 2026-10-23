namespace DotaAnalyst.Application.Matches;

/// <summary>Fight items in the team fights the hero was in.</summary>
/// <param name="Fights">Team fights the hero was in.</param>
/// <param name="KeyItemReady">Fights started holding a ready key item (BKB, Blink, Force Staff, Glimmer...); null without inventories.</param>
/// <param name="KeyItemUsed">Of those, the fights where one was used; null without inventories.</param>
/// <param name="BkbReady">Fights started holding a ready Black King Bar; null without inventories.</param>
/// <param name="BkbUsed">Of those, the fights where it was used; null without inventories.</param>
public record ItemUsageSummary(int Fights, int? KeyItemReady, int? KeyItemUsed, int? BkbReady, int? BkbUsed);
