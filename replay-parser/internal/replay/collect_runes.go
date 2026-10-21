package replay

import (
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

type runeState struct{ kind, x, y int }
type runesCollector struct {
	previous map[entityKey]runeState
	events   matchdata.List[matchdata.RuneOnMap]
}

func (c *runesCollector) Register(s *Session) {
	c.previous = map[entityKey]runeState{}
	s.OnSecond(func(t int) {
		if t%2 != 0 {
			return
		}
		current := map[entityKey]runeState{}
		for _, e := range s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTA_Item_Rune" }) {
			kind, ok := number(e, "m_iRuneType")
			x, y, pos := position(e)
			if ok && pos {
				current[keyOf(e)] = runeState{int(kind), int(math.Round(x)), int(math.Round(y))}
			}
		}
		c.events = append(c.events, runeChanges(t, c.previous, current)...)
		c.previous = current
	})
}

// runeChanges compares identity including serial, so reused indices cannot hide replacement runes.
func runeChanges(t int, previous, current map[entityKey]runeState) matchdata.List[matchdata.RuneOnMap] {
	var events matchdata.List[matchdata.RuneOnMap]
	for key, v := range previous {
		if now, ok := current[key]; !ok || now != v {
			events = append(events, matchdata.RuneOnMap{T: t, RuneType: v.kind, X: v.x, Y: v.y, Appeared: 0})
		}
	}
	for key, v := range current {
		if old, ok := previous[key]; !ok || old != v {
			events = append(events, matchdata.RuneOnMap{T: t, RuneType: v.kind, X: v.x, Y: v.y, Appeared: 1})
		}
	}
	sort.Slice(events, func(i, j int) bool {
		a, b := events[i], events[j]
		if a.Appeared != b.Appeared {
			return a.Appeared < b.Appeared
		}
		if a.RuneType != b.RuneType {
			return a.RuneType < b.RuneType
		}
		if a.X != b.X {
			return a.X < b.X
		}
		return a.Y < b.Y
	})
	return events
}
func (c *runesCollector) Fill(m *matchdata.Match) error {
	m.Objectives.RunesOnMap = c.events
	return nil
}
