package replay

import (
	"fmt"
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

type couriersCollector struct {
	tracks map[entityKey]*matchdata.CourierTimeline
}

func (c *couriersCollector) Register(s *Session) {
	c.tracks = map[entityKey]*matchdata.CourierTimeline{}
	s.OnSecond(func(t int) {
		if t%2 != 0 {
			return
		}
		for _, e := range s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTA_Unit_Courier" }) {
			team, known := number(e, "m_iTeamNum")
			x, y, pos := position(e)
			life, aliveKnown := number(e, "m_lifeState")
			if !known || !pos || !aliveKnown || teamOf(int(team)) == "" {
				continue
			}
			key := keyOf(e)
			track := c.tracks[key]
			if track == nil {
				track = &matchdata.CourierTimeline{ID: fmt.Sprintf("courier:%d:%d", key.index, key.serial), Team: teamOf(int(team))}
				c.tracks[key] = track
			}
			v := matchdata.CourierSample{T: t, X: int(math.Round(x)), Y: int(math.Round(y))}
			if life == 0 {
				v.Alive = 1
			}
			if b, ok := e.GetBool("m_bFlyingCourier"); ok {
				v.Flying = &b
			}
			if len(track.Samples) == 0 || courierChanged(track.Samples[len(track.Samples)-1], v) {
				track.Samples = append(track.Samples, v)
			}
		}
	})
}

// Compare against the last emitted point so a sequence of short moves eventually produces a sample.
func courierChanged(a, b matchdata.CourierSample) bool {
	flyingChanged := (a.Flying == nil) != (b.Flying == nil)
	if a.Flying != nil && b.Flying != nil {
		flyingChanged = *a.Flying != *b.Flying
	}
	dx, dy := float64(a.X-b.X), float64(a.Y-b.Y)
	return a.Alive != b.Alive || flyingChanged || dx*dx+dy*dy > 200*200
}

func (c *couriersCollector) Fill(m *matchdata.Match) error {
	for _, v := range c.tracks {
		m.Timelines.Couriers = append(m.Timelines.Couriers, *v)
	}
	sort.Slice(m.Timelines.Couriers, func(i, j int) bool { return m.Timelines.Couriers[i].ID < m.Timelines.Couriers[j].ID })
	return nil
}
