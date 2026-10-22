package replay

import (
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
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

// neutralItemsCollector supplements the camp collector without changing its state or output.
// Messages supply the tier; replicated slots supply names without a patch-specific ability-ID catalog.
type neutralItemsCollector struct {
	session  *Session
	messages []neutralCraftMessage
	observed [playerCount][]neutralItemObservation
	samples  [playerCount]matchdata.List[matchdata.MadstoneSample]
	last     [playerCount][4]int
	seen     [playerCount]bool
}

type neutralCraftMessage struct {
	at                              ServerTime
	player, tier                    int
	artifactLevel, enhancementLevel *int
}

type neutralItemObservation struct {
	at    ServerTime
	name  string
	level *int
	slot  int
}

func (c *neutralItemsCollector) Register(s *Session) {
	c.session = s
	s.Parser.Callbacks.OnCDOTAUserMsg_FoundNeutralItem(func(m *dota.CDOTAUserMsg_FoundNeutralItem) error {
		player := int(m.GetPlayerId())
		if m.PlayerId == nil || player < 0 || player >= playerCount || m.ItemTier == nil || m.GetItemTier() > 4 {
			return nil
		}
		c.messages = append(c.messages, neutralCraftMessage{at: s.Clock.Now(), player: player, tier: int(m.GetItemTier()) + 1, artifactLevel: neutralLevel(m.TrinketLevel), enhancementLevel: neutralLevel(m.EnhancementLevel)})
		return nil
	})
	s.OnSecond(func(second int) {
		c.readNeutralItems()
		if second%matchdata.PositionIntervalSeconds == 0 {
			c.readMadstones(second)
		}
	})
	s.OnEnd(func(int) { c.readNeutralItems() })
	// The artifact's purchase time can be rewritten shortly after crafting. Capture
	// the creation snapshot as well as seconds, before that transient evidence disappears.
	s.OnEntity("CDOTA_Item", func(e *manta.Entity, op manta.EntityOp) {
		if !op.Flag(manta.EntityOpDeleted) {
			active, _ := e.GetBool("m_bIsNeutralActiveDrop")
			passive, _ := e.GetBool("m_bIsNeutralPassiveDrop")
			if active || passive {
				c.readNeutralItems()
			}
		}
	})
}

// neutralLevel keeps missing/invalid levels unknown instead of inventing level zero.
func neutralLevel(level *int32) *int {
	if level == nil || *level <= 0 {
		return nil
	}
	v := int(*level)
	return &v
}

// readNeutralItems stamps purchase times while their pause counter is still current.
// Items seen on later seconds are deduplicated by slot, name and original server purchase time.
func (c *neutralItemsCollector) readNeutralItems() {
	s := c.session
	for _, p := range s.Players.All() {
		hero := s.Players.Hero(p.Index)
		if hero == nil {
			continue
		}
		for _, slot := range []int{neutralSlot, enhancementSlot} {
			handle, ok := hero.GetUint32(itemSlotNames[slot])
			if !ok {
				continue
			}
			item := s.Parser.FindEntityByHandle(uint64(handle))
			if item == nil {
				continue
			}
			purchased, ok := number(item, "m_flPurchaseTime")
			if !ok || purchased <= 0 {
				continue
			}
			name := s.EntityName(item)
			if !strings.HasPrefix(name, "item_") {
				continue
			}
			level, ok := item.GetInt32("m_iLevel")
			var lp *int
			if ok {
				lp = neutralLevel(&level)
			}
			duplicate := false
			for _, old := range c.observed[p.Index] {
				if old.slot == slot && old.name == name && math.Abs(old.at.Seconds-purchased) < 0.01 {
					duplicate = true
					break
				}
			}
			if !duplicate {
				c.observed[p.Index] = append(c.observed[p.Index], neutralItemObservation{at: s.Clock.At(purchased), name: name, level: lp, slot: slot})
			}
		}
	}
}

// readMadstones only emits complete replicated readings, including the first all-zero reading.
func (c *neutralItemsCollector) readMadstones(second int) {
	for _, p := range c.session.Players.All() {
		entity, prefix := c.session.Players.TeamData(p.Index)
		if entity == nil {
			continue
		}
		var values [4]int
		complete := true
		for i, key := range []string{"m_nCurrentMadstone", "m_nAcquiredMadstone", "m_iNeutralTokensFound", "m_iNeutralKillGold"} {
			v, ok := number(entity, prefix+key)
			if !ok {
				complete = false
				break
			}
			values[i] = int(v)
		}
		if !complete {
			continue
		}
		if madstonesChanged(c.seen[p.Index], c.last[p.Index], values) {
			c.samples[p.Index] = append(c.samples[p.Index], matchdata.MadstoneSample{T: second, Held: values[0], Acquired: values[1], Found: values[2], NeutralGold: values[3]})
			c.last[p.Index] = values
			c.seen[p.Index] = true
		}
	}
}

// madstonesChanged retains the first reading even when every counter is zero.
func madstonesChanged(seen bool, before, now [4]int) bool { return !seen || before != now }

// matchNeutralCraft pairs the same player's artifact/enhancement purchase within one server second.
// Message time and tier take precedence; no item name is guessed from a numeric ability ID.
func matchNeutralCraft(message neutralCraftMessage, observations []neutralItemObservation) (matchdata.NeutralCraft, bool) {
	craft := matchdata.NeutralCraft{Tier: message.tier, ArtifactLevel: message.artifactLevel, EnhancementLevel: message.enhancementLevel}
	best := math.Inf(1)
	var artifactAt float64
	for _, o := range observations {
		delta := math.Abs(o.at.Seconds - message.at.Seconds)
		if o.slot == neutralSlot && delta <= 1 && delta < best {
			craft.Artifact = o.name
			craft.ArtifactLevel = message.artifactLevel
			if craft.ArtifactLevel == nil {
				craft.ArtifactLevel = o.level
			}
			artifactAt = o.at.Seconds
			best = delta
		}
	}
	if craft.Artifact == "" {
		return craft, false
	}
	best = math.Inf(1)
	for _, o := range observations {
		delta := math.Abs(o.at.Seconds - message.at.Seconds)
		if o.slot == enhancementSlot && delta <= 1 && math.Abs(o.at.Seconds-artifactAt) <= 1 && delta < best {
			name := o.name
			craft.Enhancement = &name
			craft.EnhancementLevel = message.enhancementLevel
			if craft.EnhancementLevel == nil {
				craft.EnhancementLevel = o.level
			}
			best = delta
		}
	}
	return craft, true
}

func (c *neutralItemsCollector) Fill(m *matchdata.Match) error {
	var crafts [playerCount]matchdata.List[matchdata.NeutralCraft]
	end := math.Inf(1)
	if at, ok := c.session.Clock.GameEnd(); ok {
		end = c.session.Clock.Game(at)
	}
	for _, message := range c.messages {
		t := c.session.Clock.Game(message.at)
		if t > end {
			continue
		}
		if craft, ok := matchNeutralCraft(message, c.observed[message.player]); ok {
			craft.T = matchdata.Seconds(t)
			crafts[message.player] = append(crafts[message.player], craft)
		}
	}
	for _, p := range c.session.Players.All() {
		sort.SliceStable(crafts[p.Index], func(i, j int) bool { return crafts[p.Index][i].T < crafts[p.Index][j].T })
		m.Items.NeutralCrafts = append(m.Items.NeutralCrafts, matchdata.PlayerNeutralCrafts{Player: p.Index, Crafts: crafts[p.Index]})
		m.Timelines.Madstones = append(m.Timelines.Madstones, matchdata.PlayerMadstones{Player: p.Index, Samples: c.samples[p.Index]})
	}
	return nil
}
