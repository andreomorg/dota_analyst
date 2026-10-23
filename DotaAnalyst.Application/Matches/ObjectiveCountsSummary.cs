namespace DotaAnalyst.Application.Matches;

/// <summary>The hero's own counters of the map's objectives at the end of the match, as the game keeps them.</summary>
/// <param name="BountyRunes">Bounty runes picked.</param>
/// <param name="PowerRunes">Power runes picked.</param>
/// <param name="WaterRunes">Water runes picked.</param>
/// <param name="WisdomShrines">Wisdom Shrines (XP runes) taken.</param>
/// <param name="Lotuses">Healing Lotuses picked.</param>
/// <param name="Watchers">Watchers activated.</param>
/// <param name="Outposts">Outposts captured.</param>
/// <param name="TormentorKills">Tormentors last hit.</param>
/// <param name="CampsStacked">Neutral camps stacked.</param>
public record ObjectiveCountsSummary(
    int BountyRunes, int PowerRunes, int WaterRunes, int WisdomShrines, int Lotuses, int Watchers, int Outposts,
    int TormentorKills, int CampsStacked);
