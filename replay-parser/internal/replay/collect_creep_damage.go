package replay

import (
	"math"
	"sort"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

// No current attack target was replicated on inspected lane/siege entities. Damage
// events are retained as hits, never promoted to distinct creep counts or intentions.
type creepDamageCollector struct {
	session *Session
	events  []creepDamageEvent
}
type creepDamageEvent struct {
	at     ServerTime
	lane   string
	team   matchdata.Team
	player int
}

func (c *creepDamageCollector) Register(s *Session) {
	c.session = s
	s.OnCombatLog(func(e CombatEntry) {
		if waveCreepKind(e.Attacker) < 0 || e.TargetPlayer < 0 || !e.Raw.GetIsTargetHero() || e.Raw.GetIsTargetIllusion() || e.Raw.GetValue() == 0 {
			return
		}
		// Hero-owned damage sources are not ordinary lane waves.
		if _, owned := s.Players.ByHeroName(e.Source); owned {
			return
		}
		hero := s.Players.Hero(e.TargetPlayer)
		if hero == nil {
			return
		}
		x, y, ok := position(hero)
		if !ok {
			return
		}
		lane := waveLane(x, y)
		if lane == "" {
			return
		}
		team := matchdata.Dire
		if strings.Contains(e.Attacker, "goodguys") {
			team = matchdata.Radiant
		}
		c.events = append(c.events, creepDamageEvent{e.At, lane, team, e.TargetPlayer})
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE)
}

// Closing the interval avoids exposing any damage later than the row's timestamp.
func creepDamageSecond(t float64) int { return int(math.Ceil(t/2)) * 2 }

func (c *creepDamageCollector) Fill(m *matchdata.Match) error {
	type key struct {
		t      int
		lane   string
		team   matchdata.Team
		player int
	}
	counts := map[key]int{}
	for _, e := range c.events {
		t := float64(c.session.Clock.Seconds(e.at))
		end := creepDamageSecond(t)
		if t < 0 || t > float64(m.Match.DurationSeconds) || float64(end) > float64(m.Match.DurationSeconds) {
			continue
		}
		counts[key{end, e.lane, e.team, e.player}]++
	}
	keys := make([]key, 0, len(counts))
	for k := range counts {
		keys = append(keys, k)
	}
	sort.Slice(keys, func(i, j int) bool {
		a, b := keys[i], keys[j]
		if a.t != b.t {
			return a.t < b.t
		}
		if a.lane != b.lane {
			return a.lane < b.lane
		}
		if a.team != b.team {
			return a.team < b.team
		}
		return a.player < b.player
	})
	for _, k := range keys {
		m.Timelines.CreepHeroDamage = append(m.Timelines.CreepHeroDamage, matchdata.CreepHeroDamage{T: k.t, Lane: k.lane, CreepTeam: k.team, TargetPlayer: k.player, Hits: counts[k]})
	}
	return nil
}
