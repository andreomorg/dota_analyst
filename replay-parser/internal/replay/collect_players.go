package replay

import "github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"

// playersCollector fills the players and timelines sections.
//
// TODO(sprint 10, card 03): players + timelines.
//   - players: one matchdata.Player per Session.Players.All() (identity is already there: index, steam ID, name, team,
//     team slot, hero id and name); rank (PR m_iRankTier), pick order (m_unPickOrder), randomed (m_bHasRandomed) and
//     Final, read in OnEnd from Players.Resource (K/D/A, level) and Players.TeamData (gold, XP, LH, DN, damage, gold
//     by source/spent, counters; see the inventory in card-01-spike.md).
//   - timelines.perMinute: in OnSecond, when second%60 == 0, append each player's numbers; in OnEnd, the last point.
//   - timelines.positions: in OnSecond, when second%matchdata.PositionIntervalSeconds == 0, the position of each living
//     hero (Players.Hero(i), m_lifeState == 0, position(hero)).
type playersCollector struct{}

func (c *playersCollector) Register(*Session) {}

func (c *playersCollector) Fill(m *matchdata.Match) error {
	m.Timelines.PositionIntervalSeconds = matchdata.PositionIntervalSeconds
	return nil
}
