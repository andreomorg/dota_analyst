package replay

import (
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// mapPickupsCollector observes replicated counter increments, not inventory transfers or item uses.
// The family is published only if every nonzero player/kind total reconciles inside match boundaries.
type mapPickupsCollector struct {
	session *Session
	series  map[mapPickupKey]*mapPickupSeries
}
type mapPickupKey struct {
	player int
	kind   string
}
type mapPickupSeries struct {
	last    int
	invalid bool
	events  []ServerTime
}

func (c *mapPickupsCollector) Register(s *Session) {
	c.session = s
	c.series = map[mapPickupKey]*mapPickupSeries{}
	read := func(class string) {
		if s.ended || !s.Players.Known() {
			return
		}
		for _, p := range s.Players.All() {
			if class != "" && p.dataClass != class {
				continue
			}
			e, prefix := s.Players.TeamData(p.Index)
			if e == nil {
				continue
			}
			for _, field := range []struct{ kind, prop string }{{"lotus", "m_iLotusesTaken"}, {"wisdom", "m_iWisdomShrinesTaken"}} {
				if v, ok := number(e, prefix+field.prop); ok {
					c.observe(mapPickupKey{p.Index, field.kind}, int(v), s.Clock.Now())
				}
			}
		}
	}
	s.OnEntity("CDOTA_Data", func(e *manta.Entity, op manta.EntityOp) {
		if !op.Flag(manta.EntityOpDeleted) {
			read(e.GetClassName())
		}
	})
	// Initializes the zero baseline even when team data was created before player identities were learned.
	s.OnSecond(func(int) { read("") })
	// OnEnd runs after Session.ended is set; read explicitly at that snapshot too.
	s.OnEnd(func(int) {
		for _, p := range s.Players.All() {
			e, prefix := s.Players.TeamData(p.Index)
			if e == nil {
				continue
			}
			for _, f := range []struct{ kind, prop string }{{"lotus", "m_iLotusesTaken"}, {"wisdom", "m_iWisdomShrinesTaken"}} {
				if v, ok := number(e, prefix+f.prop); ok {
					c.observe(mapPickupKey{p.Index, f.kind}, int(v), s.Clock.Now())
				}
			}
		}
	})
}

func (c *mapPickupsCollector) observe(key mapPickupKey, value int, at ServerTime) {
	previous := c.series[key]
	if previous == nil {
		c.series[key] = &mapPickupSeries{last: value, invalid: value != 0}
		return
	}
	if value < previous.last {
		previous.invalid = true
	}
	if !previous.invalid {
		// Multiple pickups replicated together share the observation tick. No invented sub-tick ordering.
		for n := previous.last; n < value; n++ {
			previous.events = append(previous.events, at)
		}
	}
	previous.last = value
}

func (c *mapPickupsCollector) Fill(m *matchdata.Match) error {
	var pickups matchdata.List[matchdata.MapPickup]
	for _, p := range m.Players {
		for _, f := range []struct {
			kind  string
			total int
		}{{"lotus", p.Final.LotusesTaken}, {"wisdom", p.Final.WisdomShrinesTaken}} {
			series := c.series[mapPickupKey{p.Player, f.kind}]
			if series == nil {
				if f.total == 0 {
					continue
				}
				return nil
			}
			if series.invalid || series.last != f.total {
				return nil
			}
			var events matchdata.List[matchdata.MapPickup]
			for _, at := range series.events {
				t := c.session.Clock.Game(at)
				if t >= 0 && t <= float64(m.Match.DurationSeconds) {
					events = append(events, matchdata.MapPickup{T: matchdata.Seconds(t), Player: p.Player, Kind: f.kind})
				}
			}
			if len(events) != f.total {
				return nil
			}
			pickups = append(pickups, events...)
		}
	}
	sort.SliceStable(pickups, func(i, j int) bool {
		a, b := pickups[i], pickups[j]
		if a.T != b.T {
			return a.T < b.T
		}
		if a.Player != b.Player {
			return a.Player < b.Player
		}
		return a.Kind < b.Kind
	})
	m.Objectives.MapPickups = pickups
	return nil
}
