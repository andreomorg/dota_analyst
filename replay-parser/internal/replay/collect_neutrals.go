package replay

import (
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// First-observation assignment retains pulled creeps in their original camp until death.
// A 600-unit gate covers spawn scatter while avoiding arbitrary remote assignments.
const neutralCampRadius = 600

type neutralsCollector struct {
	session  *Session
	camps    map[string]*matchdata.NeutralCamp
	entities map[entityKey]*manta.Entity
	assigned map[entityKey]string
}

func (c *neutralsCollector) Register(s *Session) {
	c.session = s
	c.camps = map[string]*matchdata.NeutralCamp{}
	c.entities = map[entityKey]*manta.Entity{}
	c.assigned = map[entityKey]string{}
	s.OnEntity("CDOTA_NeutralSpawner", func(e *manta.Entity, op manta.EntityOp) {
		if op.Flag(manta.EntityOpDeleted) {
			return
		}
		x, y, ok := position(e)
		if !ok {
			return
		}
		ix, iy := int(math.Round(x)), int(math.Round(y))
		id := fmt.Sprintf("camp:%d:%d", ix, iy)
		if c.camps[id] != nil {
			return
		}
		camp := &matchdata.NeutralCamp{ID: id, X: ix, Y: iy}
		if v, ok := number(e, "m_Type"); ok {
			n := int(v)
			camp.SpawnerType = &n
			if n >= 0 && n < 4 {
				kind := []string{"small", "medium", "large", "ancient"}[n]
				camp.Type = &kind
			}
		}
		c.camps[id] = camp
	})
	s.OnEntity("CDOTA_BaseNPC_Creep_Neutral", func(e *manta.Entity, op manta.EntityOp) {
		key := keyOf(e)
		if op.Flag(manta.EntityOpDeleted) {
			delete(c.entities, key)
			delete(c.assigned, key)
			return
		}
		c.entities[key] = e
	})
	s.OnSecond(func(t int) {
		if t >= 0 && t%matchdata.PositionIntervalSeconds == 0 {
			c.sample(t)
		}
	})
}

// Neutral ownership handles are authoritative; m_nPlayerOwnerID also equals 1 on wild units.
func wildNeutral(e *manta.Entity, name string) bool {
	team, ok := number(e, "m_iTeamNum")
	if !ok || team != 4 {
		return false
	}
	if strings.Contains(name, "roshan") || strings.Contains(name, "miniboss") || strings.Contains(name, "tormentor") {
		return false
	}
	for _, prop := range []string{"m_hOwnerEntity", "m_hOwnerNPC"} {
		if v, ok := number(e, prop); ok && v != 16777215 && v != 4294967295 && v != -1 {
			return false
		}
	}
	return true
}

func (c *neutralsCollector) sample(t int) {
	counts := map[string]int{}
	for key, e := range c.entities {
		life, ok := number(e, "m_lifeState")
		if !ok || life != 0 || !wildNeutral(e, creepUnitName(c.session, e)) {
			continue
		}
		id := c.assigned[key]
		if id == "" {
			x, y, ok := position(e)
			if !ok {
				continue
			}
			best := float64(neutralCampRadius * neutralCampRadius)
			for candidate, camp := range c.camps {
				dx, dy := x-float64(camp.X), y-float64(camp.Y)
				d := dx*dx + dy*dy
				if d < best || d == best && (id == "" || candidate < id) {
					best = d
					id = candidate
				}
			}
			if id != "" {
				c.assigned[key] = id
			}
		}
		if id != "" {
			counts[id]++
		}
	}
	for id, camp := range c.camps {
		n := counts[id]
		if len(camp.Alive) == 0 || camp.Alive[len(camp.Alive)-1].Alive != n {
			camp.Alive = append(camp.Alive, matchdata.CampCount{T: t, Alive: n})
		}
	}
}

func (c *neutralsCollector) Fill(m *matchdata.Match) error {
	ids := make([]string, 0, len(c.camps))
	for id := range c.camps {
		ids = append(ids, id)
	}
	sort.Strings(ids)
	for _, id := range ids {
		m.Objectives.NeutralCamps = append(m.Objectives.NeutralCamps, *c.camps[id])
	}
	return nil
}
