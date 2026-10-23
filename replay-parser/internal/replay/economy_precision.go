package replay

import (
	"context"
	"encoding/json"
	"io"
	"math"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Local development evidence only. Output contains player indexes, never account/name/chat data.
func InspectEconomyPrecision(r io.Reader, w io.Writer) error {
	c := &economyPrecision{previous: map[int]precisionWallet{}, writer: w}
	_, err := analyzeMatch(context.Background(), r, []Collector{&matchCollector{}, &playersCollector{}, c})
	return err
}

type precisionWallet struct {
	At                                          ServerTime
	Reliable, Unreliable, NetWorth, Lost, Spent *int
	Cooldown, CostTime, GoldLimit               *float64
	CustomCost                                  *int
}
type precisionChange struct {
	Player        int
	Seconds       float64
	Before, After precisionWallet
}
type precisionGoldLog struct {
	Player  int
	Seconds float64
	At      ServerTime
	Amount  int64
	Reason  *uint32
}
type economyPrecision struct {
	s        *Session
	previous map[int]precisionWallet
	changes  []precisionChange
	logs     []precisionGoldLog
	writer   io.Writer
}

func (c *economyPrecision) Register(s *Session) {
	c.s = s
	s.OnEntity("CDOTA_Data", func(e *manta.Entity, op manta.EntityOp) {
		if !s.Players.Known() || op.Flag(manta.EntityOpDeleted) {
			return
		}
		for _, p := range s.Players.All() {
			if p.dataClass != e.GetClassName() {
				continue
			}
			prefix := p.dataPrefix
			float := func(name string) *float64 {
				v, ok := number(e, prefix+name)
				if !ok {
					return nil
				}
				return &v
			}
			now := precisionWallet{At: s.Clock.Now(), Reliable: optionalInt(e, prefix+"m_iReliableGold"),
				Unreliable: optionalInt(e, prefix+"m_iUnreliableGold"), NetWorth: optionalInt(e, prefix+"m_iNetWorth"),
				Lost: optionalInt(e, prefix+"m_iGoldLostToDeath"), Spent: optionalInt(e, prefix+"m_iGoldSpentOnBuybacks"),
				Cooldown: float("m_flBuybackCooldownTime"), CostTime: float("m_flBuybackCostTime"), GoldLimit: float("m_flBuybackGoldLimitTime")}
			if resource, _, teamPrefix := s.Players.Resource(p.Index); resource != nil {
				now.CustomCost = optionalInt(resource, teamPrefix+"m_iCustomBuybackCost")
			}
			old, known := c.previous[p.Index]
			if known && (intChanged(old.Lost, now.Lost) || intChanged(old.Spent, now.Spent)) {
				c.changes = append(c.changes, precisionChange{Player: p.Index, Before: old, After: now})
			}
			c.previous[p.Index] = now
		}
	})
	s.OnCombatLog(func(e CombatEntry) {
		if e.Raw.Value == nil {
			return
		}
		p, known := s.Players.ByHeroName(e.Target)
		if !known {
			return
		}
		var reason *uint32
		if e.Raw.GoldReason != nil {
			v := *e.Raw.GoldReason
			reason = &v
		}
		c.logs = append(c.logs, precisionGoldLog{Player: p, At: e.At, Amount: int64(int32(e.Raw.GetValue())), Reason: reason})
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD)
}

func intChanged(a, b *int) bool { return a != nil && b != nil && *a != *b }

func (c *economyPrecision) Fill(m *matchdata.Match) error {
	for i := range c.changes {
		c.changes[i].Seconds = c.s.Clock.Game(c.changes[i].After.At)
	}
	for i := range c.logs {
		c.logs[i].Seconds = c.s.Clock.Game(c.logs[i].At)
	}
	type matchRow struct {
		Player           int
		Seconds          float64
		CounterDelta     int
		LogAmount        int64
		Candidates       int
		BeforeUnreliable *int
		ClampMatches     bool
	}
	rows := []matchRow{}
	for _, change := range c.changes {
		if change.Seconds < 0 || change.Seconds > float64(m.Match.DurationSeconds) || change.Before.Lost == nil || change.After.Lost == nil {
			continue
		}
		delta := *change.After.Lost - *change.Before.Lost
		if delta <= 0 {
			continue
		}
		row := matchRow{Player: change.Player, Seconds: change.Seconds, CounterDelta: delta, BeforeUnreliable: change.Before.Unreliable}
		for _, log := range c.logs {
			if log.Player == row.Player && log.Reason != nil && *log.Reason == 1 && math.Abs(log.Seconds-row.Seconds) <= 1 {
				row.Candidates++
				row.LogAmount += log.Amount
			}
		}
		if row.Candidates == 1 && row.BeforeUnreliable != nil {
			row.ClampMatches = int(math.Min(float64(-row.LogAmount), float64(*row.BeforeUnreliable))) == delta
		}
		rows = append(rows, row)
	}
	return json.NewEncoder(c.writer).Encode(struct {
		Match   matchdata.Info
		Changes []precisionChange
		Deaths  []matchRow
		Logs    []precisionGoldLog
	}{m.Match, c.changes, rows, c.logs})
}
