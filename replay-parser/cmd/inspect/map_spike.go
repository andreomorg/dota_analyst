package main

import (
	"compress/gzip"
	"encoding/json"
	"flag"
	"fmt"
	"math"
	"math/bits"
	"os"
	"sort"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Where the tree state lives: every class with a property containing "TreeState", how many such properties, their
// value types and how many bits are on, at the first sighting and at the end.
func init() {
	flag.Func("tree-state-spike", "find the tree state properties in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		type summary struct {
			props, on int
			types     map[string]int
			tick      uint32
		}
		first := map[string]summary{}
		last := map[string]summary{}
		read := func(e *manta.Entity) summary {
			s := summary{types: map[string]int{}, tick: p.NetTick}
			for k, v := range e.Map() {
				if !strings.Contains(k, "TreeState") {
					continue
				}
				s.props++
				s.types[fmt.Sprintf("%T", v)]++
				switch n := v.(type) {
				case uint64:
					s.on += bits.OnesCount64(n)
				case uint32:
					s.on += bits.OnesCount32(n)
				case bool:
					if n {
						s.on++
					}
				}
			}
			return s
		}
		p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
			if op.Flag(manta.EntityOpDeleted) {
				return nil
			}
			class := e.GetClassName()
			if _, seen := first[class]; !seen {
				if s := read(e); s.props > 0 {
					first[class] = s
					fmt.Printf("FIRST %s tick=%d props=%d on=%d types=%v\n", class, s.tick, s.props, s.on, s.types)
				} else {
					first[class] = summary{}
				}
			}
			// About once a minute per class.
			if first[class].props > 0 && p.NetTick >= last[class].tick+1800 {
				s := read(e)
				last[class] = s
				if class == "CDOTA_DataRadiant" {
					fmt.Printf("RADIANT tick=%d props=%d on=%d\n", s.tick, s.props, s.on)
				}
			}
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		for class, s := range last {
			fmt.Printf("LATER %s tick=%d props=%d on=%d\n", class, s.tick, s.props, s.on)
		}
		os.Exit(0)
		return nil
	})
}

// Does bit i of m_bWorldTreeState belong to tree i of the map file (tools/MapExtractor order)? For every bit that turns
// off, the distance from that tree to the nearest hero: a right mapping puts a hero next to most cut trees. Also the
// same distances with the tree list shifted by one, as a control.
func init() {
	flag.Func("tree-index-spike", "check bit i = map tree i with a local .dem and the map file (replay.dem,map.json.gz) and exit", func(arg string) error {
		paths := strings.Split(arg, ",")
		if len(paths) != 2 {
			return fmt.Errorf("want replay.dem,map.json.gz")
		}
		trees, err := mapTrees(paths[1])
		if err != nil {
			return err
		}
		f, err := os.Open(paths[0])
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		// Candidate orders of the same trees: the file's, and sorted by position both ways.
		orders := map[string][][3]int{"file": trees}
		byXY := append([][3]int(nil), trees...)
		sort.SliceStable(byXY, func(a, b int) bool {
			if byXY[a][0] != byXY[b][0] {
				return byXY[a][0] < byXY[b][0]
			}
			return byXY[a][1] < byXY[b][1]
		})
		orders["x,y"] = byXY
		byYX := append([][3]int(nil), trees...)
		sort.SliceStable(byYX, func(a, b int) bool {
			if byYX[a][1] != byYX[b][1] {
				return byYX[a][1] < byYX[b][1]
			}
			return byYX[a][0] < byYX[b][0]
		})
		orders["y,x"] = byYX
		// The extractor read the default layer (2306 trees) then the bases (103 + 66): bases first as another guess.
		if len(trees) == 2475 {
			orders["bases first"] = append(append([][3]int(nil), trees[2306:]...), trees[:2306]...)
		}
		byOrder := map[string][]float64{}
		byBucket := map[string][2]int{} // index range -> [cut, hero within 300]
		var previous []uint64
		lastRead := uint32(0)
		var right, shifted []float64
		minOn, maxOn := -1, -1
		heroes := func() [][2]float64 {
			var out [][2]float64
			for _, e := range p.FilterEntity(func(e *manta.Entity) bool { return strings.HasPrefix(e.GetClassName(), "CDOTA_Unit_Hero_") }) {
				cx, ok1 := e.GetUint64("CBodyComponent.m_cellX")
				cy, ok2 := e.GetUint64("CBodyComponent.m_cellY")
				vx, _ := e.GetFloat32("CBodyComponent.m_vecX")
				vy, _ := e.GetFloat32("CBodyComponent.m_vecY")
				if ok1 && ok2 {
					out = append(out, [2]float64{float64(cx)*128 + float64(vx) - 16384, float64(cy)*128 + float64(vy) - 16384})
				}
			}
			return out
		}
		nearest := func(tree [3]int, hs [][2]float64) float64 {
			best := math.Inf(1)
			for _, h := range hs {
				best = math.Min(best, math.Hypot(h[0]-float64(tree[0]), h[1]-float64(tree[1])))
			}
			return best
		}
		p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
			if e.GetClassName() != "CDOTA_DataRadiant" || op.Flag(manta.EntityOpDeleted) || p.NetTick < lastRead+15 {
				return nil
			}
			lastRead = p.NetTick
			words := make([]uint64, 256)
			on := 0
			for i := range words {
				words[i], _ = e.GetUint64(fmt.Sprintf("m_bWorldTreeState.%04d", i))
				on += bits.OnesCount64(words[i])
			}
			if on == 0 {
				return nil
			}
			if previous == nil {
				for i := 0; i < 256*64; i++ {
					if words[i/64]&(1<<(i%64)) != 0 {
						if minOn < 0 {
							minOn = i
						}
						maxOn = i
					}
				}
				fmt.Printf("FIRST on=%d minIndex=%d maxIndex=%d trees=%d\n", on, minOn, maxOn, len(trees))
			} else {
				hs := heroes()
				for i := 0; i < 256*64; i++ {
					if previous[i/64]&(1<<(i%64)) != 0 && words[i/64]&(1<<(i%64)) == 0 && i < len(trees) {
						d := nearest(trees[i], hs)
						bucket := fmt.Sprintf("%04d-%04d", i/500*500, i/500*500+499)
						if i >= 2306 {
							bucket = "bases 2306+"
						}
						counts := byBucket[bucket]
						counts[0]++
						if d <= 300 {
							counts[1]++
						}
						byBucket[bucket] = counts
						right = append(right, d)
						shifted = append(shifted, nearest(trees[(i+1)%len(trees)], hs))
						for name, order := range orders {
							byOrder[name] = append(byOrder[name], nearest(order[i], hs))
						}
					}
				}
			}
			previous = words
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		within := func(values []float64, limit float64) int {
			n := 0
			for _, v := range values {
				if v <= limit {
					n++
				}
			}
			return n
		}
		sort.Float64s(right)
		sort.Float64s(shifted)
		median := func(v []float64) float64 {
			if len(v) == 0 {
				return math.NaN()
			}
			return v[len(v)/2]
		}
		fmt.Printf("CUT trees=%d | bit i = tree i: hero within 300=%d within 800=%d median=%.0f | shifted by one: within 300=%d within 800=%d median=%.0f\n",
			len(right), within(right, 300), within(right, 800), median(right), within(shifted, 300), within(shifted, 800), median(shifted))
		for name, counts := range byBucket {
			fmt.Printf("BUCKET %-12s cut=%d heroWithin300=%d\n", name, counts[0], counts[1])
		}
		for name, values := range byOrder {
			sort.Float64s(values)
			fmt.Printf("ORDER %-12s within 200=%d within 300=%d median=%.0f\n", name, within(values, 200), within(values, 300), median(values))
		}
		os.Exit(0)
		return nil
	})
}

// The trees of a map file written by tools/MapExtractor, in its order.
func mapTrees(path string) ([][3]int, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	z, err := gzip.NewReader(f)
	if err != nil {
		return nil, err
	}
	var m struct {
		Trees [][3]int `json:"trees"`
	}
	if err := json.NewDecoder(z).Decode(&m); err != nil {
		return nil, err
	}
	return m.Trees, nil
}

// DATA-D17 spike: stolen abilities (m_bStolen) in the heroes' m_vecAbilities — who carries each, its entity
// index/serial, level, hidden and cooldown, and when it appears and goes away.
func init() {
	flag.Func("stolen-spike", "follow stolen abilities in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		type stolen struct {
			hero, name     string
			index, serial  int32
			level          int32
			hidden         bool
			cooldownStarts int
			lastCooldown   float32
			from           uint32
		}
		current := map[string]*stolen{}
		var finished []*stolen
		next := uint32(0)
		nameOf := func(e *manta.Entity) string {
			index, ok := e.GetInt32("m_pEntity.m_nameStringTableIndex")
			if !ok {
				return ""
			}
			name, _ := p.LookupStringByIndex("EntityNames", index)
			return name
		}
		p.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error {
			if p.NetTick < next {
				return nil
			}
			next = p.NetTick + 60
			seen := map[string]bool{}
			for _, hero := range p.FilterEntity(func(e *manta.Entity) bool { return strings.HasPrefix(e.GetClassName(), "CDOTA_Unit_Hero_") }) {
				for slot := 0; slot < 35; slot++ {
					handle, ok := hero.GetUint32(fmt.Sprintf("m_vecAbilities.%04d", slot))
					if !ok {
						continue
					}
					ability := p.FindEntityByHandle(uint64(handle))
					if ability == nil {
						continue
					}
					if isStolen, _ := ability.GetBool("m_bStolen"); !isStolen {
						continue
					}
					key := fmt.Sprintf("%d:%d", ability.GetIndex(), ability.GetSerial())
					seen[key] = true
					level, _ := ability.GetInt32("m_iLevel")
					hidden, _ := ability.GetBool("m_bHidden")
					cooldown, _ := ability.GetFloat32("m_fCooldown")
					s := current[key]
					if s == nil {
						s = &stolen{hero: nameOf(hero), name: nameOf(ability), index: ability.GetIndex(), serial: ability.GetSerial(), from: p.NetTick}
						current[key] = s
						fmt.Printf("ACQUIRED tick=%d hero=%s ability=%s entity=%s level=%d hidden=%v\n", p.NetTick, s.hero, s.name, key, level, hidden)
					}
					if cooldown > s.lastCooldown+0.5 {
						s.cooldownStarts++
					}
					s.lastCooldown, s.level, s.hidden = cooldown, level, hidden
				}
			}
			for key, s := range current {
				if !seen[key] {
					fmt.Printf("LOST     tick=%d hero=%s ability=%s entity=%s held=%ds cooldownsStarted=%d\n", p.NetTick, s.hero, s.name, key, (p.NetTick-s.from)/30, s.cooldownStarts)
					finished = append(finished, s)
					delete(current, key)
				}
			}
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		fmt.Printf("STOLEN total=%d still held at the end=%d\n", len(finished)+len(current), len(current))
		os.Exit(0)
		return nil
	})
}

// Event census (22/10, survey of what is not extracted yet, work/agents/events-survey.md): every combat log type and
// game chat event type in a replay, with counts and one example of each combat log type.
func init() {
	flag.Func("event-census", "count every combat log and chat event type in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		combat, chat := map[string]int{}, map[string]int{}
		examples := map[string]string{}
		name := func(index *uint32) string {
			if index == nil {
				return ""
			}
			s, _ := p.LookupStringByIndex("CombatLogNames", int32(*index))
			return s
		}
		p.Callbacks.OnCMsgDOTACombatLogEntry(func(m *dota.CMsgDOTACombatLogEntry) error {
			kind := strings.TrimPrefix(m.GetType().String(), "DOTA_COMBATLOG_")
			combat[kind]++
			if examples[kind] == "" {
				examples[kind] = fmt.Sprintf("attacker=%s target=%s inflictor=%s value=%d", name(m.AttackerName), name(m.TargetName), name(m.InflictorName), m.GetValue())
			}
			return nil
		})
		p.Callbacks.OnCDOTAUserMsg_ChatEvent(func(m *dota.CDOTAUserMsg_ChatEvent) error {
			chat[strings.TrimPrefix(m.GetType().String(), "CHAT_MESSAGE_")]++
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		for _, group := range []struct {
			title  string
			counts map[string]int
		}{{"COMBATLOG", combat}, {"CHATEVENT", chat}} {
			keys := make([]string, 0, len(group.counts))
			for k := range group.counts {
				keys = append(keys, k)
			}
			sort.Slice(keys, func(i, j int) bool { return group.counts[keys[i]] > group.counts[keys[j]] })
			for _, k := range keys {
				line := fmt.Sprintf("%s %-45s %6d", group.title, k, group.counts[k])
				if group.title == "COMBATLOG" {
					line += "  e.g. " + examples[k]
				}
				fmt.Println(line)
			}
		}
		os.Exit(0)
		return nil
	})
}

// MP4 spike: which units carry a vision range (and so can see for their team), by class, at a few moments.
func init() {
	flag.Func("vision-units-spike", "list units with a vision range in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		next := uint32(30 * 60 * 5) // a reading every 5 min of server time
		p.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error {
			if p.NetTick < next {
				return nil
			}
			next += 30 * 60 * 5
			type key struct {
				class      string
				team       int32
				day, night int32
			}
			counts := map[key]int{}
			alive := 0
			for _, e := range p.FilterEntity(func(e *manta.Entity) bool { return true }) {
				day, ok := e.GetInt32("m_iDayTimeVisionRange")
				if !ok {
					continue
				}
				night, _ := e.GetInt32("m_iNightTimeVisionRange")
				anyInt := func(name string) int32 {
					switch v := e.Get(name).(type) {
					case uint64:
						return int32(v)
					case int32:
						return v
					case uint32:
						return int32(v)
					}
					return -1
				}
				team, life := anyInt("m_iTeamNum"), anyInt("m_lifeState")
				if life != 0 || (team != 2 && team != 3) {
					continue
				}
				alive++
				counts[key{e.GetClassName(), team, day, night}]++
			}
			keys := make([]key, 0, len(counts))
			for k := range counts {
				keys = append(keys, k)
			}
			sort.Slice(keys, func(i, j int) bool { return counts[keys[i]] > counts[keys[j]] })
			fmt.Printf("TICK %d alive units with vision=%d\n", p.NetTick, alive)
			for _, k := range keys {
				fmt.Printf("  %-40s team=%d day=%d night=%d count=%d\n", k.class, k.team, k.day, k.night, counts[k])
			}
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		os.Exit(0)
		return nil
	})
}

// MP3 spike: what a replay says about the map's changing state (trees, temporary trees, Roshan/Tormentor, Watchers,
// gold with a location). Read-only; prints counts and examples, then exits.
func init() {
	flag.Func("map-spike", "inspect the map state (trees, bosses, lanterns, gold) in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}

		// (a) tree state words per Data class: first reading, then bit flips.
		treeWords := map[string]map[string]uint64{}
		flips := map[string][2]int{} // class -> [alive->dead, dead->alive]
		firstDead := map[string]int{}
		var flipExamples []string
		// (b) temporary trees.
		tempCreated, tempDeleted := 0, 0
		// (c) bosses.
		type bossReading struct {
			x, y  int
			alive bool
		}
		lastBoss := map[string]bossReading{}
		bossChanges := map[string]int{}
		// (d)(e) combat log.
		lampCount, goldCount, goldWithLocation := 0, 0, 0
		goldReasons := map[uint32]int{}
		var lampExamples, goldExamples []string
		lastSample := uint32(0)

		name := func(index *uint32) string {
			if index == nil {
				return ""
			}
			s, _ := p.LookupStringByIndex("CombatLogNames", int32(*index))
			return s
		}
		p.Callbacks.OnCMsgDOTACombatLogEntry(func(m *dota.CMsgDOTACombatLogEntry) error {
			inflictor := name(m.InflictorName)
			if strings.Contains(inflictor, "lamp") || strings.Contains(name(m.TargetName), "lantern") {
				lampCount++
				if len(lampExamples) < 5 {
					lampExamples = append(lampExamples, fmt.Sprintf("tick=%d type=%s attacker=%s target=%s inflictor=%s loc=(%v,%v) hasLoc=%v",
						p.NetTick, m.GetType(), name(m.AttackerName), name(m.TargetName), inflictor, m.GetLocationX(), m.GetLocationY(), m.LocationX != nil))
				}
			}
			if m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD {
				goldCount++
				goldReasons[m.GetGoldReason()]++
				if m.LocationX != nil && m.LocationY != nil {
					goldWithLocation++
				}
				if len(goldExamples) < 6 {
					goldExamples = append(goldExamples, fmt.Sprintf("tick=%d target=%s value=%d signed=%d reason=%d loc=(%v,%v) hasLoc=%v",
						p.NetTick, name(m.TargetName), m.GetValue(), int32(m.GetValue()), m.GetGoldReason(), m.GetLocationX(), m.GetLocationY(), m.LocationX != nil))
				}
			}
			return nil
		})

		pos := func(e *manta.Entity) (int, int, bool) {
			cx, ok1 := e.GetUint64("CBodyComponent.m_cellX")
			cy, ok2 := e.GetUint64("CBodyComponent.m_cellY")
			vx, ok3 := e.GetFloat32("CBodyComponent.m_vecX")
			vy, ok4 := e.GetFloat32("CBodyComponent.m_vecY")
			if !(ok1 && ok2 && ok3 && ok4) {
				return 0, 0, false
			}
			return int(math.Round(float64(cx)*128 + float64(vx) - 16384)), int(math.Round(float64(cy)*128 + float64(vy) - 16384)), true
		}

		p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
			class := e.GetClassName()
			switch {
			case class == "CDOTA_TempTree":
				x, y, _ := pos(e)
				if op.Flag(manta.EntityOpCreated) {
					tempCreated++
					if tempCreated <= 3 {
						fmt.Printf("TEMPTREE created tick=%d at=(%d,%d)\n", p.NetTick, x, y)
					}
				}
				if op.Flag(manta.EntityOpDeleted) {
					tempDeleted++
					if tempDeleted <= 3 {
						fmt.Printf("TEMPTREE deleted tick=%d at=(%d,%d)\n", p.NetTick, x, y)
					}
				}
			case class == "CDOTA_Unit_Roshan" || class == "CDOTA_Unit_Miniboss":
				if op.Flag(manta.EntityOpDeleted) || p.NetTick < lastSample {
					return nil
				}
				x, y, ok := pos(e)
				life, _ := e.GetInt32("m_lifeState")
				if !ok {
					return nil
				}
				key := fmt.Sprintf("%s#%d", class, e.GetIndex())
				reading := bossReading{x, y, life == 0}
				before, seen := lastBoss[key]
				if !seen || before.alive != reading.alive || math.Hypot(float64(before.x-x), float64(before.y-y)) > 200 {
					bossChanges[key]++
					fmt.Printf("BOSS tick=%d %s at=(%d,%d) alive=%v\n", p.NetTick, key, x, y, reading.alive)
					lastBoss[key] = reading
				}
			case class == "CDOTA_DataRadiant" || class == "CDOTA_DataDire":
				// Every 2 s of server ticks is enough to see trees fall.
				if p.NetTick < lastSample+60 && treeWords[class] != nil {
					return nil
				}
				words := map[string]uint64{}
				for k, v := range e.Map() {
					if strings.HasPrefix(k, "m_bWorldTreeState.") {
						if n, ok := v.(uint64); ok {
							words[k] = n
						}
					}
				}
				if len(words) == 0 {
					return nil
				}
				if treeWords[class] == nil {
					dead := 0
					for _, w := range words {
						dead += 64 - bits.OnesCount64(w)
					}
					firstDead[class] = dead
					ones := 0
					for _, w := range words {
						ones += bits.OnesCount64(w)
					}
					fmt.Printf("TREES first tick=%d %s words=%d bitsOn=%d bitsOff=%d\n", p.NetTick, class, len(words), ones, dead)
				} else {
					counts := flips[class]
					for k, w := range words {
						old := treeWords[class][k]
						died, born := old&^w, w&^old
						counts[0] += bits.OnesCount64(died)
						counts[1] += bits.OnesCount64(born)
						if (died != 0 || born != 0) && len(flipExamples) < 8 {
							flipExamples = append(flipExamples, fmt.Sprintf("tick=%d %s %s died=%d born=%d", p.NetTick, class, k, bits.OnesCount64(died), bits.OnesCount64(born)))
						}
					}
					flips[class] = counts
				}
				treeWords[class] = words
			}
			if p.NetTick >= lastSample+60 {
				lastSample = p.NetTick
			}
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}

		for class, words := range treeWords {
			fmt.Printf("TREES %s words=%d firstOff=%d flips alive->dead=%d dead->alive=%d\n", class, len(words), firstDead[class], flips[class][0], flips[class][1])
		}
		for _, e := range flipExamples {
			fmt.Println("FLIP", e)
		}
		fmt.Printf("TEMPTREES created=%d deleted=%d\n", tempCreated, tempDeleted)
		keys := make([]string, 0, len(bossChanges))
		for k := range bossChanges {
			keys = append(keys, k)
		}
		sort.Strings(keys)
		for _, k := range keys {
			fmt.Printf("BOSSCHANGES %s=%d\n", k, bossChanges[k])
		}
		fmt.Printf("LAMP entries=%d\n", lampCount)
		for _, e := range lampExamples {
			fmt.Println("LAMP", e)
		}
		fmt.Printf("GOLD entries=%d withLocation=%d reasons=%v\n", goldCount, goldWithLocation, goldReasons)
		for _, e := range goldExamples {
			fmt.Println("GOLD", e)
		}
		os.Exit(0)
		return nil
	})
}
