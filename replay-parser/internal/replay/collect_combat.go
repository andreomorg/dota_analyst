package replay

import "github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"

// combatCollector fills the kills, buybacks and combat sections.
//
// TODO(sprint 10, card 04): kills with location, buybacks, damage/healing aggregates + per minute.
//   - kills: OnCombatLog(DEATH) with Raw.GetIsTargetHero() && !Raw.GetIsTargetIllusion(); the victim's position from
//     Players.Hero(entry.TargetPlayer) in the same hook (the combat log has no location); assists from
//     Raw.GetAssistPlayers() (already player indexes); Reincarnated = Raw.GetWillReincarnate().
//   - buybacks: OnCombatLog(BUYBACK), player index in Raw.GetValue().
//   - combat: OnCombatLog(DAMAGE, CRITICAL_DAMAGE, HEAL) aggregated per source -> target -> inflictor, heroes by
//     player index, other units grouped by matchdata.UnitKind; perMinute = hero-vs-hero damage per game-clock minute
//     (convert entry.At in Fill, not in the hook: the horn may not be known yet).
type combatCollector struct{}

func (c *combatCollector) Register(*Session) {}

func (c *combatCollector) Fill(*matchdata.Match) error { return nil }
