package replay

import (
	"fmt"
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

type mapObjectivesCollector struct {
	session  *Session
	captures []outpostEvent
	spawners map[string]*matchdata.TormentorState
}
type outpostEvent struct {
	at       ServerTime
	id, team int
}

func (c *mapObjectivesCollector) Register(s *Session) {
	c.session = s
	c.spawners = map[string]*matchdata.TormentorState{}
	s.Parser.Callbacks.OnCDOTAUserMsg_OutpostCaptured(func(m *dota.CDOTAUserMsg_OutpostCaptured) error {
		if m.OutpostEntindex != nil && m.TeamId != nil {
			c.captures = append(c.captures, outpostEvent{s.Clock.Now(), int(m.GetOutpostEntindex()), int(m.GetTeamId())})
		}
		return nil
	})
	s.OnSecond(func(t int) {
		if t%2 != 0 {
			return
		}
		for _, e := range s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTA_MinibossSpawner" }) {
			x, y, ok := position(e)
			team, known := number(e, "m_iTeamNum")
			if ok && known && teamOf(int(team)) != "" {
				k := fmt.Sprintf("%d:%d", int(math.Round(x)), int(math.Round(y)))
				if c.spawners[k] == nil {
					side := teamOf(int(team))
					c.spawners[k] = &matchdata.TormentorState{Side: &side, X: int(math.Round(x)), Y: int(math.Round(y))}
				}
			}
		}
		units := s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTA_Unit_Miniboss" })
		for _, e := range units {
			x, y, ok := position(e)
			if !ok {
				continue
			}
			found := false
			for _, sp := range c.spawners {
				if math.Hypot(x-float64(sp.X), y-float64(sp.Y)) <= 600 {
					found = true
					break
				}
			}
			// This replay moves the neutral unit to a second site with no replicated spawner.
			// Keep that site's actual coordinates and unknown side instead of dropping its living state.
			if !found {
				k := fmt.Sprintf("%d:%d", int(math.Round(x)), int(math.Round(y)))
				c.spawners[k] = &matchdata.TormentorState{X: int(math.Round(x)), Y: int(math.Round(y))}
			}
		}
		for siteID, sp := range c.spawners {
			var state *int
			matches := 0
			for _, e := range units {
				x, y, ok := position(e)
				life, known := number(e, "m_lifeState")
				if ok && known && uniqueTormentorSite(x, y, c.spawners) == siteID {
					matches++
					v := 0
					if life == 0 {
						v = 1
					}
					state = &v
				}
			}
			// Missing or ambiguous entities are unknown, never inferred deaths or scheduled spawns.
			if matches != 1 {
				state = nil
			}
			if len(sp.Samples) == 0 || !sameOptionalInt(sp.Samples[len(sp.Samples)-1].Alive, state) {
				sp.Samples = append(sp.Samples, matchdata.TormentorSample{T: t, Alive: state})
			}
		}
	})
}

func sameOptionalInt(a, b *int) bool { return a == nil && b == nil || a != nil && b != nil && *a == *b }

// A unit near overlapping sites cannot establish a distinct side's state.
func uniqueTormentorSite(x, y float64, sites map[string]*matchdata.TormentorState) string {
	result := ""
	for id, site := range sites {
		if math.Hypot(x-float64(site.X), y-float64(site.Y)) <= 600 {
			if result != "" {
				return ""
			}
			result = id
		}
	}
	return result
}

func (c *mapObjectivesCollector) Fill(m *matchdata.Match) error {
	for _, e := range c.captures {
		if end, ended := c.session.Clock.GameEnd(); ended && e.at.Seconds > end.Seconds {
			continue
		}
		if team := teamOf(e.team); team != "" {
			m.Objectives.OutpostCaptures = append(m.Objectives.OutpostCaptures, matchdata.OutpostCaptureEvent{T: c.session.Clock.Seconds(e.at), ID: e.id, Team: team})
		}
	}
	for _, sp := range c.spawners {
		m.Objectives.TormentorStates = append(m.Objectives.TormentorStates, *sp)
	}
	sort.Slice(m.Objectives.TormentorStates, func(i, j int) bool {
		a, b := m.Objectives.TormentorStates[i], m.Objectives.TormentorStates[j]
		if a.X != b.X {
			return a.X < b.X
		}
		return a.Y < b.Y
	})
	return nil
}
