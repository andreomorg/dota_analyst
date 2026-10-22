package replay

import (
	"fmt"
	"math"
	"math/bits"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// mapCollector reads the map's changing state (MAPA MP3): which trees stand (timelines.trees), temporary trees
// (timelines.tempTrees), Roshan and Tormentor positions and lives (objectives.bossTracks), Watcher activations
// (vision.lanterns) and every GOLD entry with its location (combat.goldEvents). Spike on 9045593156 and 9042407280
// (cmd/inspect -map-spike, -tree-index-spike): see "Estado do mapa" in replay-parser/CLAUDE.md.
type mapCollector struct {
	session *Session

	treeWordNames []string // m_bWorldTreeState.0000 ...
	treeWords     []uint64 // last reading
	treeCount     int      // trees in the map: the highest bit on at the first reading + 1
	trees         matchdata.TreeTimeline

	tempTrees []timedTempTree
	bosses    map[entityKey]*bossTrack
	lanterns  []lanternEvent
	gold      []goldEvent
}

type timedTempTree struct {
	at      ServerTime
	x, y    int
	present bool
}

type bossTrack struct {
	firstSeen int
	order     int
	track     matchdata.BossTrack
}

type lanternEvent struct {
	at     ServerTime
	player int // -1 until resolved by hero name in Fill
	hero   string
	x, y   *int
}

type goldEvent struct {
	at     ServerTime
	hero   string
	amount int64
	reason *uint32
	x, y   *int
}

// The tree state has 256 words of 64 bits in 7.41 (16,384 bits for 2,475 trees); read up to this many.
const maxTreeWords = 1024

// A boss sample is kept when the unit moved more than this (map units) since the last one kept.
const bossMoveThreshold = 200

func (c *mapCollector) Register(s *Session) {
	c.session = s
	c.bosses = map[entityKey]*bossTrack{}
	for i := 0; i < maxTreeWords; i++ {
		c.treeWordNames = append(c.treeWordNames, fmt.Sprintf("m_bWorldTreeState.%04d", i))
	}

	s.OnSecond(func(t int) {
		if t%matchdata.PositionIntervalSeconds != 0 {
			return
		}
		c.readTrees(s, t)
		c.readBosses(s, t)
	})

	s.OnEntity("CDOTA_TempTree", func(e *manta.Entity, op manta.EntityOp) {
		created, deleted := op.Flag(manta.EntityOpCreated), op.Flag(manta.EntityOpDeleted)
		if !created && !deleted {
			return
		}
		x, y, ok := position(e)
		if !ok {
			return
		}
		c.tempTrees = append(c.tempTrees, timedTempTree{s.Clock.Now(), int(math.Round(x)), int(math.Round(y)), created && !deleted})
	})

	s.OnCombatLog(func(e CombatEntry) {
		if s.ended || e.Inflictor != "ability_lamp_use" {
			return
		}
		event := lanternEvent{at: e.At, player: e.AttackerPlayer, hero: e.Attacker}
		// The log has no location: the hero's position now.
		if player, ok := s.Players.ByHeroName(e.Attacker); ok {
			if x, y, positioned := heroPosition(s.Players.Hero(player)); positioned {
				event.x, event.y = &x, &y
			}
		}
		c.lanterns = append(c.lanterns, event)
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ABILITY)

	s.OnCombatLog(func(e CombatEntry) {
		if s.ended || e.Raw.Value == nil {
			return
		}
		event := goldEvent{at: e.At, hero: e.Target, amount: int64(int32(e.Raw.GetValue()))}
		if e.Raw.GoldReason != nil {
			reason := *e.Raw.GoldReason
			event.reason = &reason
		}
		if e.Raw.LocationX != nil && e.Raw.LocationY != nil {
			x, y := int(math.Round(float64(e.Raw.GetLocationX()))), int(math.Round(float64(e.Raw.GetLocationY())))
			event.x, event.y = &x, &y
		}
		c.gold = append(c.gold, event)
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD)
}

// readTrees compares the tree bits with the last reading. The first reading with any bit on sets the number of trees
// and the ones already down; until then the state is still loading (all zero).
func (c *mapCollector) readTrees(s *Session, t int) {
	data := s.Entity("CDOTA_DataRadiant")
	if data == nil {
		return
	}
	words := make([]uint64, 0, len(c.treeWords))
	for _, name := range c.treeWordNames {
		word, ok := data.GetUint64(name)
		if !ok {
			break
		}
		words = append(words, word)
	}
	c.addTreeReading(words, t)
}

// addTreeReading takes one reading of the tree words at second t.
func (c *mapCollector) addTreeReading(words []uint64, t int) {
	if c.treeWords == nil {
		highest := -1
		for i, word := range words {
			if word != 0 {
				highest = i*64 + 63 - bits.LeadingZeros64(word)
			}
		}
		if highest < 0 {
			return
		}
		c.treeCount = highest + 1
		for index := 0; index < c.treeCount; index++ {
			if words[index/64]&(1<<(index%64)) == 0 {
				c.trees.DeadAtStart = append(c.trees.DeadAtStart, index)
			}
		}
		c.treeWords = words
		return
	}
	for w := 0; w < len(words) && w < len(c.treeWords) && w*64 < c.treeCount; w++ {
		changed := words[w] ^ c.treeWords[w]
		for changed != 0 {
			bit := bits.TrailingZeros64(changed)
			changed &^= 1 << bit
			if index := w*64 + bit; index < c.treeCount {
				c.trees.Changes = append(c.trees.Changes, matchdata.TreeChange{T: t, Index: index, Alive: words[w]&(1<<bit) != 0})
			}
		}
	}
	c.treeWords = words
}

func (c *mapCollector) readBosses(s *Session, t int) {
	units := s.Parser.FilterEntity(func(e *manta.Entity) bool {
		class := e.GetClassName()
		return class == "CDOTA_Unit_Roshan" || class == "CDOTA_Unit_Miniboss"
	})
	for _, e := range units {
		x, y, positioned := position(e)
		life, known := number(e, "m_lifeState")
		if !positioned || !known {
			continue
		}
		key := keyOf(e)
		boss := c.bosses[key]
		if boss == nil {
			kind := "roshan"
			if e.GetClassName() == "CDOTA_Unit_Miniboss" {
				kind = "tormentor"
			}
			boss = &bossTrack{firstSeen: t, order: len(c.bosses), track: matchdata.BossTrack{Kind: kind}}
			c.bosses[key] = boss
		}
		sample := matchdata.BossSample{T: t, X: int(math.Round(x)), Y: int(math.Round(y)), Alive: life == 0}
		samples := boss.track.Samples
		if len(samples) == 0 || bossChanged(samples[len(samples)-1], sample) {
			boss.track.Samples = append(boss.track.Samples, sample)
		}
	}
}

// Compared with the last sample kept, so a slow walk still produces samples.
func bossChanged(a, b matchdata.BossSample) bool {
	dx, dy := float64(a.X-b.X), float64(a.Y-b.Y)
	return a.Alive != b.Alive || dx*dx+dy*dy > bossMoveThreshold*bossMoveThreshold
}

// heroPosition is a hero entity's position rounded to whole map units.
func heroPosition(hero *manta.Entity) (int, int, bool) {
	if hero == nil {
		return 0, 0, false
	}
	x, y, ok := position(hero)
	return int(math.Round(x)), int(math.Round(y)), ok
}

func (c *mapCollector) Fill(m *matchdata.Match) error {
	clock := c.session.Clock
	duration := float64(m.Match.DurationSeconds)
	inMatch := func(at ServerTime) bool { return clock.Game(at) <= duration }

	m.Timelines.Trees = c.trees
	for _, tree := range c.tempTrees {
		if inMatch(tree.at) {
			m.Timelines.TempTrees = append(m.Timelines.TempTrees, matchdata.TempTree{T: clock.Seconds(tree.at), X: tree.x, Y: tree.y, Present: tree.present})
		}
	}

	bosses := make([]*bossTrack, 0, len(c.bosses))
	for _, boss := range c.bosses {
		bosses = append(bosses, boss)
	}
	sort.Slice(bosses, func(i, j int) bool {
		if bosses[i].firstSeen != bosses[j].firstSeen {
			return bosses[i].firstSeen < bosses[j].firstSeen
		}
		return bosses[i].order < bosses[j].order
	})
	for _, boss := range bosses {
		m.Objectives.BossTracks = append(m.Objectives.BossTracks, boss.track)
	}

	for _, lantern := range c.lanterns {
		player := lantern.player
		if player < 0 {
			index, ok := c.session.Players.ByHeroName(lantern.hero)
			if !ok {
				continue
			}
			player = index
		}
		if inMatch(lantern.at) {
			m.Vision.Lanterns = append(m.Vision.Lanterns, matchdata.LanternUse{T: clock.Seconds(lantern.at), Player: player, X: lantern.x, Y: lantern.y})
		}
	}

	for _, gold := range c.gold {
		player, ok := c.session.Players.ByHeroName(gold.hero)
		if !ok || !inMatch(gold.at) {
			continue
		}
		m.Combat.GoldEvents = append(m.Combat.GoldEvents, matchdata.GoldEvent{
			T: clock.Seconds(gold.at), Player: player, Amount: gold.amount, Reason: gold.reason, X: gold.x, Y: gold.y,
		})
	}
	return nil
}
