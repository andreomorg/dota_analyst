package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// laningCollector fills the laning section: the lane of each player, by the same rule (and the same samples: every
// second between 1:00 and 10:00, living heroes only) as POST /v1/laning, so the API needs a single parse per match.
type laningCollector struct {
	session    *Session
	positions  laning.Aggregator
	lastSecond int
}

func (c *laningCollector) Register(s *Session) {
	c.session = s
	s.OnSecond(func(second int) {
		c.lastSecond = second
		if second < laning.SamplesFromSeconds || second >= laning.EndsAtSeconds {
			return
		}
		for _, player := range s.Players.All() {
			hero := s.Players.Hero(player.Index)
			if hero == nil {
				continue
			}
			if lifeState, ok := number(hero, "m_lifeState"); !ok || lifeState != 0 {
				continue // a dead hero's position says nothing about the lane
			}
			if x, y, ok := position(hero); ok {
				c.positions.AddSample(second, player.Index, x, y)
			}
		}
	})
}

func (c *laningCollector) Fill(m *matchdata.Match) error {
	m.Laning.EndsAtSeconds = min(c.lastSecond, laning.EndsAtSeconds)
	for _, player := range c.session.Players.All() {
		m.Laning.Lanes = append(m.Laning.Lanes,
			matchdata.PlayerLane{Player: player.Index, Lane: string(c.positions.Lane(player.Index))})
	}
	return nil
}
