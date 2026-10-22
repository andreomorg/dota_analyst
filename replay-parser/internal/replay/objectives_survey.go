package replay

// Development evidence for OB1, invoked only by cmd/inspect -objectives-spike.
import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// SurveyObjectives writes local development evidence, including post-game deltas so late
// counter changes can be distinguished from pickups inside the document's match boundary.
func SurveyObjectives(r io.Reader, w io.Writer) error {
	c := &objectivesSurvey{w: w}
	cs := append(newCollectors(), c)
	_, err := analyzeMatch(context.Background(), r, cs)
	return err
}

type surveyDelta struct {
	At            ServerTime
	Player        int
	Kind          string
	Before, After int
}
type objectivesSurvey struct {
	s        *Session
	w        io.Writer
	previous map[string]int
	changes  []surveyDelta
	logs     map[string]int
	sites    map[string][2]int
	events   []surveyLog
	chats    []surveyChat
}
type surveyChat struct {
	T             matchdata.Seconds
	At            ServerTime
	Type          string
	Player, Value int
}
type surveyLog struct {
	T                  matchdata.Seconds
	At                 ServerTime
	Player             int
	Type, Name, Target string
	X, Y               *int
}

func (c *objectivesSurvey) Register(s *Session) {
	c.s = s
	c.previous = map[string]int{}
	c.logs = map[string]int{}
	c.sites = map[string][2]int{}
	s.OnChatEvent(func(e *dota.CDOTAUserMsg_ChatEvent, at ServerTime) {
		if strings.Contains(e.GetType().String(), "RUNE") {
			c.chats = append(c.chats, surveyChat{At: at, Type: e.GetType().String(), Player: int(e.GetPlayerid_1()), Value: int(e.GetValue())})
		}
	})
	s.OnEntity("CDOTA_Data", func(e *manta.Entity, op manta.EntityOp) {
		if !s.Players.Known() || op.Flag(manta.EntityOpDeleted) {
			return
		}
		for _, p := range s.Players.All() {
			if p.dataClass != e.GetClassName() {
				continue
			}
			for _, kind := range []string{"Lotuses", "WisdomShrines", "Watchers"} {
				v, ok := number(e, p.dataPrefix+"m_i"+kind+"Taken")
				if !ok {
					continue
				}
				key := fmt.Sprintf("%d/%s", p.Index, kind)
				old, seen := c.previous[key]
				if (!seen && v != 0) || (seen && int(v) != old) {
					c.changes = append(c.changes, surveyDelta{s.Clock.Now(), p.Index, kind, old, int(v)})
				}
				c.previous[key] = int(v)
			}
		}
	})
	s.OnCombatLog(func(e CombatEntry) {
		names := e.Attacker + " " + e.Target + " " + e.Inflictor
		if e.Type == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE {
			names += " " + s.Name(e.Raw.GetValue())
		}
		interesting := false
		for _, word := range []string{"famango", "lotus", "wisdom", "twin_gate", "portal", "lamp", "lantern"} {
			if strings.Contains(names, word) {
				c.logs[e.Type.String()+"/"+e.Inflictor+"/"+word]++
				interesting = true
			}
		}
		if interesting {
			var x, y *int
			if e.Raw.LocationX != nil && e.Raw.LocationY != nil {
				a, b := int(e.Raw.GetLocationX()), int(e.Raw.GetLocationY())
				x, y = &a, &b
			}
			c.events = append(c.events, surveyLog{At: e.At, Player: e.AttackerPlayer, Type: e.Type.String(), Name: e.Inflictor, Target: e.Target, X: x, Y: y})
		}
	})
	s.OnEntity("CDOTA", func(e *manta.Entity, op manta.EntityOp) {
		name := e.GetClassName() + "/" + s.EntityName(e)
		if strings.Contains(strings.ToLower(name), "lantern") || strings.Contains(strings.ToLower(name), "twin") {
			if x, y, ok := position(e); ok {
				c.sites[fmt.Sprintf("%s/%d", name, e.GetIndex())] = [2]int{int(x), int(y)}
			}
		}
	})
}
func (c *objectivesSurvey) Fill(m *matchdata.Match) error {
	type delta struct {
		T             matchdata.Seconds
		Player        int
		Kind          string
		Before, After int
	}
	ds := []delta{}
	for i := range c.events {
		c.events[i].T = c.s.Clock.Seconds(c.events[i].At)
	}
	for i := range c.chats {
		c.chats[i].T = c.s.Clock.Seconds(c.chats[i].At)
	}
	for _, d := range c.changes {
		ds = append(ds, delta{c.s.Clock.Seconds(d.At), d.Player, d.Kind, d.Before, d.After})
	}
	return json.NewEncoder(c.w).Encode(struct {
		Match   matchdata.Match
		Changes []delta
		Logs    map[string]int
		Sites   map[string][2]int
		Events  []surveyLog
		Chats   []surveyChat
	}{*m, ds, c.logs, c.sites, c.events, c.chats})
}
