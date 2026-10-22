package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// Gold is a player resource: keep it even while the hero is dead, unlike position/vitals samples.
type goldBalancesCollector struct {
	samples []matchdata.GoldBalance
}

func (c *goldBalancesCollector) Register(s *Session) {
	s.OnSecond(func(second int) {
		if second < 0 || second%matchdata.PositionIntervalSeconds != 0 {
			return
		}
		for _, player := range s.Players.All() {
			data, prefix := s.Players.TeamData(player.Index)
			var reliable, unreliable *int
			if data != nil {
				reliable = optionalInt(data, prefix+"m_iReliableGold")
				unreliable = optionalInt(data, prefix+"m_iUnreliableGold")
			}
			c.samples = append(c.samples, matchdata.GoldBalance{
				T: second, Player: player.Index, Reliable: reliable, Unreliable: unreliable,
			})
		}
	})
}

func (c *goldBalancesCollector) Fill(m *matchdata.Match) error {
	for _, sample := range c.samples {
		if float64(sample.T) <= float64(m.Match.DurationSeconds) {
			m.Timelines.GoldBalances = append(m.Timelines.GoldBalances, sample)
		}
	}
	return nil
}
