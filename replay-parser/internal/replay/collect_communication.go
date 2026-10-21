package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
	"sort"
)

type communicationEvent struct {
	at           ServerTime
	player, x, y int
	id           *uint32
}
type communicationCollector struct {
	session      *Session
	pings, wheel []communicationEvent
}

func (c *communicationCollector) Register(s *Session) {
	c.session = s
	s.Parser.Callbacks.OnCDOTAUserMsg_LocationPing(func(m *dota.CDOTAUserMsg_LocationPing) error {
		q := m.GetLocationPing()
		if !s.ended && m.PlayerId != nil && m.GetPlayerId() >= 0 && m.GetPlayerId() < playerCount && q != nil && q.X != nil && q.Y != nil {
			var kind *uint32
			if q.Type != nil {
				v := q.GetType()
				kind = &v
			}
			c.pings = append(c.pings, communicationEvent{s.Clock.Now(), int(m.GetPlayerId()), int(q.GetX()), int(q.GetY()), kind})
		}
		return nil
	})
	// ChatMessage has free-form content and is deliberately not subscribed to.
	s.Parser.Callbacks.OnCDOTAUserMsg_ChatWheel(func(m *dota.CDOTAUserMsg_ChatWheel) error {
		if !s.ended && m.PlayerId != nil && m.GetPlayerId() >= 0 && m.GetPlayerId() < playerCount && m.ChatMessageId != nil {
			id := m.GetChatMessageId()
			c.wheel = append(c.wheel, communicationEvent{at: s.Clock.Now(), player: int(m.GetPlayerId()), id: &id})
		}
		return nil
	})
}
func (c *communicationCollector) Fill(m *matchdata.Match) error {
	for _, e := range c.pings {
		ctime := c.session.Clock.Seconds(e.at)
		m.Vision.Pings = append(m.Vision.Pings, matchdata.MapPing{T: ctime, Player: e.player, X: e.x, Y: e.y, Type: e.id})
	}
	for _, e := range c.wheel {
		ctime := c.session.Clock.Seconds(e.at)
		m.Vision.ChatWheel = append(m.Vision.ChatWheel, matchdata.ChatWheel{T: ctime, Player: e.player, Phrase: *e.id})
	}
	sort.SliceStable(m.Vision.Pings, func(i, j int) bool { return m.Vision.Pings[i].T < m.Vision.Pings[j].T })
	sort.SliceStable(m.Vision.ChatWheel, func(i, j int) bool { return m.Vision.ChatWheel[i].T < m.Vision.ChatWheel[j].T })
	return nil
}
