package replay

import (
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// buildingsCollector keeps sparse health readings so pressure is observable before a building falls.
type buildingsCollector struct {
	session   *Session
	entities  map[entityKey]*manta.Entity
	buildings map[string]*matchdata.BuildingHealth
}

func (c *buildingsCollector) Register(s *Session) {
	c.session = s
	c.entities = map[entityKey]*manta.Entity{}
	c.buildings = map[string]*matchdata.BuildingHealth{}
	for _, class := range []string{"CDOTA_BaseNPC_Tower", "CDOTA_BaseNPC_Barracks", "CDOTA_BaseNPC_Fort"} {
		s.OnEntity(class, func(e *manta.Entity, op manta.EntityOp) {
			if op.Flag(manta.EntityOpDeleted) {
				delete(c.entities, keyOf(e))
				return
			}
			c.entities[keyOf(e)] = e
		})
	}
	s.OnSecond(func(second int) {
		if second >= 0 && second%matchdata.PositionIntervalSeconds == 0 {
			c.sample(second)
		}
	})
}

func (c *buildingsCollector) sample(second int) {
	for _, e := range c.entities {
		name := canonicalBuildingName(c.session.EntityName(e))
		b := c.buildings[name]
		if b == nil {
			team, ok := number(e, "m_iTeamNum")
			if !ok || teamOf(int(team)) == "" {
				continue
			}
			var valid bool
			b, valid = buildingIdentity(name, teamOf(int(team)))
			if !valid {
				continue
			}
			x, y, positioned := position(e)
			if !positioned {
				continue
			}
			// Buildings are stationary; retain the first known position in the hero coordinate system.
			b.X, b.Y = int(math.Round(x)), int(math.Round(y))
			c.buildings[name] = b
		}
		if len(b.Health) > 0 && b.Health[len(b.Health)-1].Health == 0 {
			continue
		}
		health, ok := number(e, "m_iHealth")
		maxHealth, maxOK := number(e, "m_iMaxHealth")
		if !ok || !maxOK {
			continue
		}
		reading := matchdata.BuildingHealthSample{T: second, Health: int(health), MaxHealth: int(maxHealth)}
		if len(b.Health) > 0 {
			last := b.Health[len(b.Health)-1]
			if last.Health == reading.Health && last.MaxHealth == reading.MaxHealth {
				continue
			}
		}
		b.Health = append(b.Health, reading)
	}
}

// buildingIdentity uses the same internal names as objectives, excluding unrelated building classes.
func buildingIdentity(name string, team matchdata.Team) (*matchdata.BuildingHealth, bool) {
	b := &matchdata.BuildingHealth{Building: name, Team: team}
	switch {
	case strings.Contains(name, "_tower"):
		b.Kind = "tower"
		var tier int
		if _, err := fmt.Sscanf(strings.Split(name, "_tower")[1], "%d", &tier); err != nil || tier < 1 || tier > 4 {
			return nil, false
		}
		b.Tier = &tier
		if tier == 4 {
			b.Building = strings.TrimSuffix(strings.TrimSuffix(name, "_top"), "_bot")
		}
	case strings.Contains(name, "_rax_"):
		b.Kind = "barracks"
	case name == "npc_dota_goodguys_fort" || name == "npc_dota_badguys_fort":
		b.Kind = "ancient"
	default:
		return nil, false
	}
	for suffix, lane := range map[string]string{"_top": "top", "_mid": "middle", "_bot": "bottom"} {
		if strings.HasSuffix(name, suffix) && (b.Tier == nil || *b.Tier != 4) {
			value := lane
			b.Lane = &value
		}
	}
	return b, true
}

func (c *buildingsCollector) Fill(m *matchdata.Match) error {
	// The Ancient can end the clock before the next sample, and dead entities can disappear.
	// Use the already-collected death event only to terminate the series at the next sampling boundary.
	for _, death := range m.Objectives.Buildings {
		var b *matchdata.BuildingHealth
		if strings.HasSuffix(death.Building, "_tower4") {
			// Both T4s have the same combat-log name; their observed zero identifies the corresponding death.
			for _, candidate := range c.buildings {
				if candidate.Building != death.Building || len(candidate.Health) == 0 {
					continue
				}
				last := candidate.Health[len(candidate.Health)-1]
				if last.Health == 0 && math.Abs(float64(last.T)-float64(death.T)) <= matchdata.PositionIntervalSeconds {
					b = candidate
					break
				}
			}
		} else {
			b = c.buildings[death.Building]
		}
		if b == nil || len(b.Health) == 0 {
			continue
		}
		t := int(math.Ceil(float64(death.T)/matchdata.PositionIntervalSeconds)) * matchdata.PositionIntervalSeconds
		for len(b.Health) > 0 && b.Health[len(b.Health)-1].T >= t {
			b.Health = b.Health[:len(b.Health)-1]
		}
		if len(b.Health) > 0 {
			last := b.Health[len(b.Health)-1]
			if last.Health != 0 {
				b.Health = append(b.Health, matchdata.BuildingHealthSample{T: t, Health: 0, MaxHealth: last.MaxHealth})
			}
		}
	}
	names := make([]string, 0, len(c.buildings))
	for name := range c.buildings {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		m.Objectives.BuildingHealth = append(m.Objectives.BuildingHealth, *c.buildings[name])
	}
	return nil
}

// EntityNames uses map identifiers rather than CombatLogNames; normalize their verified spelling.
func canonicalBuildingName(name string) string {
	if strings.HasPrefix(name, "dota_") {
		return "npc_" + name
	}
	parts := strings.Split(name, "_")
	if len(parts) == 4 && (parts[0] == "good" || parts[0] == "bad") && parts[1] == "rax" {
		side := "goodguys"
		if parts[0] == "bad" {
			side = "badguys"
		}
		return "npc_dota_" + side + "_" + parts[2] + "_rax_" + parts[3]
	}
	return ""
}
