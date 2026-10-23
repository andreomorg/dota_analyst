package replay

import (
	"fmt"
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

type lossObservation struct {
	at, previousAt        ServerTime
	player, before, after int
	beforeGold, afterGold *int
}
type lossBaseline struct {
	at         ServerTime
	lost, gold *int
}
type economyPrecisionCollector struct {
	s        *Session
	states   []matchdata.EconomyState
	losses   []lossObservation
	previous map[int]lossBaseline
	units    map[entityKey]*manta.Entity
	farm     []matchdata.FarmSnapshot
}

func (c *economyPrecisionCollector) Register(s *Session) {
	c.s = s
	c.previous = map[int]lossBaseline{}
	c.units = map[entityKey]*manta.Entity{}
	s.OnEntity("CDOTA_Data", func(e *manta.Entity, op manta.EntityOp) {
		if !s.Players.Known() || op.Flag(manta.EntityOpDeleted) {
			return
		}
		for _, p := range s.Players.All() {
			if p.dataClass != e.GetClassName() {
				continue
			}
			now := lossBaseline{at: s.Clock.Now(), lost: optionalInt(e, p.dataPrefix+"m_iGoldLostToDeath"), gold: optionalInt(e, p.dataPrefix+"m_iUnreliableGold")}
			old, ok := c.previous[p.Index]
			if ok && old.lost != nil && now.lost != nil && *old.lost != *now.lost {
				c.losses = append(c.losses, lossObservation{now.at, old.at, p.Index, *old.lost, *now.lost, old.gold, now.gold})
			}
			c.previous[p.Index] = now
		}
	})
	track := func(e *manta.Entity, op manta.EntityOp) {
		if op.Flag(manta.EntityOpDeleted) {
			delete(c.units, keyOf(e))
		} else {
			c.units[keyOf(e)] = e
		}
	}
	s.OnEntity("CDOTA_BaseNPC_Creep_Lane", track)
	s.OnEntity("CDOTA_BaseNPC_Creep_Siege", track)
	s.OnEntity("CDOTA_BaseNPC_Creep_Neutral", track)
	s.OnSecond(func(t int) {
		if t < 0 || t%2 != 0 {
			return
		}
		for _, p := range s.Players.All() {
			e, prefix := s.Players.TeamData(p.Index)
			if e == nil {
				continue
			}
			state := matchdata.EconomyState{T: t, Player: p.Index, NetWorth: optionalInt(e, prefix+"m_iNetWorth"), Reliable: optionalInt(e, prefix+"m_iReliableGold"), Unreliable: optionalInt(e, prefix+"m_iUnreliableGold")}
			// Clock domains through pauses are not validated for this replicated timer. Keep it unknown once a
			// pause was observed instead of treating server elapsed time as a proven cooldown clock.
			if value, ok := number(e, prefix+"m_flBuybackCooldownTime"); ok && len(s.Clock.game.pauses) == 0 {
				remaining := math.Max(0, value-s.Clock.Now().Seconds)
				state.CooldownRemaining = &remaining
			}
			if r, _, tp := s.Players.Resource(p.Index); r != nil {
				state.CustomCost = optionalInt(r, tp+"m_iCustomBuybackCost")
			}
			c.states = append(c.states, state)
		}
		if t%10 == 0 {
			c.sampleFarm(t)
		}
	})
}
func (c *economyPrecisionCollector) sampleFarm(t int) {
	snapshot := matchdata.FarmSnapshot{T: t}
	for key, e := range c.units {
		life, known := number(e, "m_lifeState")
		health, hpKnown := number(e, "m_iHealth")
		team, teamKnown := number(e, "m_iTeamNum")
		if !known || life != 0 || !hpKnown || health <= 0 || !teamKnown {
			continue
		}
		name := creepUnitName(c.s, e)
		if team == 4 {
			if !wildNeutral(e, name) {
				continue
			}
		} else if team != 2 && team != 3 {
			continue
		}
		x, y, ok := position(e)
		if !ok {
			continue
		}
		snapshot.Units = append(snapshot.Units, matchdata.FarmUnit{ID: fmt.Sprintf("%d:%d", key.index, key.serial), Name: name, Team: int(team), X: int(math.Round(x)), Y: int(math.Round(y)), Health: int(health),
			MinGold: optionalInt(e, "m_iGoldBountyMin"), MaxGold: optionalInt(e, "m_iGoldBountyMax"), SeenRadiant: c.seen(e, "CDOTA_DataRadiant"), SeenDire: c.seen(e, "CDOTA_DataDire")})
	}
	sort.Slice(snapshot.Units, func(i, j int) bool { return snapshot.Units[i].ID < snapshot.Units[j].ID })
	c.farm = append(c.farm, snapshot)
}
func (c *economyPrecisionCollector) seen(e *manta.Entity, class string) *int {
	data := c.s.Entity(class)
	if data == nil {
		return nil
	}
	return visibleBit(func(word int) (uint64, bool) {
		v, ok := data.Get(fmt.Sprintf("m_bNPCVisibleState.%04d", word)).(uint64)
		return v, ok
	}, int(e.GetIndex()))
}
func (c *economyPrecisionCollector) Fill(m *matchdata.Match) error {
	end := float64(m.Match.DurationSeconds)
	changes := make(matchdata.List[matchdata.GoldLossChange], 0, len(c.losses))
	for _, state := range c.states {
		if float64(state.T) <= end {
			m.Timelines.EconomyStates = append(m.Timelines.EconomyStates, state)
		}
	}
	for _, loss := range c.losses {
		t := c.s.Clock.Game(loss.at)
		if t < 0 || t > end {
			continue
		}
		changes = append(changes, matchdata.GoldLossChange{T: matchdata.Seconds(t), Player: loss.player, Before: loss.before, After: loss.after, BeforeUnreliable: loss.beforeGold, AfterUnreliable: loss.afterGold, PreviousAt: matchdata.Seconds(c.s.Clock.Game(loss.previousAt))})
	}
	m.Timelines.GoldLossChanges = &changes
	for _, snapshot := range c.farm {
		if float64(snapshot.T) <= end {
			m.Timelines.FarmSnapshots = append(m.Timelines.FarmSnapshots, snapshot)
		}
	}
	return nil
}
