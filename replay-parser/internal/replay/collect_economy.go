package replay

import (
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

type economyEvent struct {
	at       ServerTime
	hero     string
	resource int
	reason   *uint32
	amount   int64
}
type economyCollector struct {
	session *Session
	events  []economyEvent
}

func (c *economyCollector) Register(s *Session) {
	c.session = s
	s.OnCombatLog(func(e CombatEntry) {
		if s.ended || e.Raw.Value == nil {
			return
		}
		resource := 0
		reason := e.Raw.GoldReason
		if e.Type == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_XP {
			resource = 1
			reason = e.Raw.XpReason
		}
		var copied *uint32
		if reason != nil {
			v := *reason
			copied = &v
		}
		// Valve transports signed gold deltas in an unsigned protobuf field.
		amount := int64(int32(e.Raw.GetValue()))
		c.events = append(c.events, economyEvent{e.At, e.Target, resource, copied, amount})
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_XP)
}

type economyKey struct {
	t, player, resource int
	reason              uint32
	known               bool
}

func (c *economyCollector) Fill(m *matchdata.Match) error {
	sums := map[economyKey]int64{}
	for _, e := range c.events {
		p, ok := c.session.Players.ByHeroName(e.hero)
		if !ok {
			continue
		}
		t := c.session.Clock.Game(e.at)
		if t > float64(m.Match.DurationSeconds) {
			continue
		}
		k := economyKey{t: int(math.Floor(t/2)) * 2, player: p, resource: e.resource, known: e.reason != nil}
		if e.reason != nil {
			k.reason = *e.reason
		}
		sums[k] += e.amount
	}
	for k, v := range sums {
		var reason *uint32
		if k.known {
			r := k.reason
			reason = &r
		}
		m.Combat.Economy = append(m.Combat.Economy, matchdata.EconomyBucket{T: k.t, Player: k.player, Resource: k.resource, Reason: reason, Amount: v})
	}
	sort.Slice(m.Combat.Economy, func(i, j int) bool {
		a, b := m.Combat.Economy[i], m.Combat.Economy[j]
		if a.T != b.T {
			return a.T < b.T
		}
		if a.Player != b.Player {
			return a.Player < b.Player
		}
		if a.Resource != b.Resource {
			return a.Resource < b.Resource
		}
		if a.Reason == nil {
			return b.Reason != nil
		}
		return b.Reason != nil && *a.Reason < *b.Reason
	})
	return nil
}
