package main

import (
	"fmt"
	"log"
	"math"
	"runtime"
	"time"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Spike code (card 01): prints each hero's laning numbers at 10:00 of the game clock. The real
// implementation lives in internal/laning and internal/replay (card 02).

const (
	ticksPerSecond = 30
	laningEnd      = 600 // seconds of game clock
	positionsFrom  = 60
)

type heroTrack struct {
	samples    int
	sumX, sumY float64
	zones      map[string]int
}

func runLaning(parser *manta.Parser) {
	started := time.Now()
	tracks := map[int]*heroTrack{}
	lastSecond := math.MinInt
	clock := math.NaN()
	done := false

	// The clock is checked on every tick so the cut is exact; positions are sampled once per second of game clock.
	parser.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error {
		if done {
			return nil
		}
		rules := findByClass(parser, "CDOTAGamerulesProxy")
		resource := findByClass(parser, "CDOTA_PlayerResource")
		if rules == nil || resource == nil {
			return nil
		}
		now, running := gameClock(parser, rules)
		if !running {
			return nil
		}
		clock = now

		if second := int(clock); second != lastSecond && clock >= positionsFrom && clock <= laningEnd {
			lastSecond = second
			sampleHeroes(parser, resource, tracks)
		}

		if clock >= laningEnd {
			done = true
			printLaning(parser, resource, tracks, clock)
			parser.Stop()
		}
		return nil
	})

	err := parser.Start()
	if !done { // the replay ended before 10:00: the numbers at the end are the laning numbers
		if resource := findByClass(parser, "CDOTA_PlayerResource"); resource != nil {
			printLaning(parser, resource, tracks, clock)
		}
	}

	var memory runtime.MemStats
	runtime.ReadMemStats(&memory)
	fmt.Printf("\nnet tick %d, elapsed %s, total allocated %d MB, heap from OS %d MB\n",
		parser.NetTick, time.Since(started).Round(time.Millisecond), memory.TotalAlloc>>20, memory.HeapSys>>20)
	if err != nil {
		log.Fatal(err)
	}
}

// gameClock returns the seconds since the horn (0:00), without pauses. It is not running before the horn or while
// the game is paused (the paused ticks are only added to m_nTotalPausedTicks when the pause ends).
func gameClock(parser *manta.Parser, rules *manta.Entity) (float64, bool) {
	gameStart := number(rules, "m_pGameRules.m_flGameStartTime")
	if gameStart <= 0 {
		return 0, false
	}
	if paused, _ := rules.GetBool("m_pGameRules.m_bGamePaused"); paused {
		return 0, false
	}
	pausedTicks := number(rules, "m_pGameRules.m_nTotalPausedTicks")
	return float64(parser.NetTick)/ticksPerSecond - gameStart - pausedTicks/ticksPerSecond, true
}

func sampleHeroes(parser *manta.Parser, resource *manta.Entity, tracks map[int]*heroTrack) {
	for slot := 0; slot < 10; slot++ {
		hero := heroOf(parser, resource, slot)
		if hero == nil || number(hero, "m_lifeState") != 0 {
			continue // dead heroes say nothing about the lane
		}
		x, y := position(hero)
		track := tracks[slot]
		if track == nil {
			track = &heroTrack{zones: map[string]int{}}
			tracks[slot] = track
		}
		track.samples++
		track.sumX += x
		track.sumY += y
		track.zones[zoneOf(x, y)]++
	}
}

// runFull parses the whole replay doing nothing else, to measure the worst case.
func runFull(parser *manta.Parser) {
	started := time.Now()
	err := parser.Start()
	var memory runtime.MemStats
	runtime.ReadMemStats(&memory)
	fmt.Printf("net tick %d (%.1f min), elapsed %s, total allocated %d MB, heap from OS %d MB\n",
		parser.NetTick, float64(parser.NetTick)/ticksPerSecond/60, time.Since(started).Round(time.Millisecond),
		memory.TotalAlloc>>20, memory.HeapSys>>20)
	if err != nil {
		log.Fatal(err)
	}
}

func printLaning(parser *manta.Parser, resource *manta.Entity, tracks map[int]*heroTrack, clock float64) {
	teamData := map[uint64]*manta.Entity{} // steam ID -> CDOTA_DataRadiant/Dire with the prefix of that player
	prefixes := map[uint64]string{}
	for _, className := range []string{"CDOTA_DataRadiant", "CDOTA_DataDire"} {
		data := findByClass(parser, className)
		for i := 0; data != nil && i < 5; i++ {
			prefix := fmt.Sprintf("m_vecDataTeam.%04d.", i)
			// Bots and players without a Steam ID all have 0: they cannot be told apart this way.
			if steamID, ok := data.GetUint64(prefix + "m_iPlayerSteamID"); ok && steamID != 0 {
				teamData[steamID], prefixes[steamID] = data, prefix
			}
		}
	}

	fmt.Printf("game clock %.1f s (net tick %d)\n", clock, parser.NetTick)
	fmt.Printf("%-4s %-4s %-17s %-5s %4s %4s %6s %6s %4s %4s  %-16s %s\n",
		"slot", "hero", "steamId", "team", "lh", "dn", "net", "xp", "dth", "lvl", "avg pos", "zones (s)")
	for slot := 0; slot < 10; slot++ {
		team := fmt.Sprintf("m_vecPlayerTeamData.%04d.", slot)
		player := fmt.Sprintf("m_vecPlayerData.%04d.", slot)
		steamID, _ := resource.GetUint64(player + "m_iPlayerSteamID")

		data, prefix := teamData[steamID], prefixes[steamID]
		stat := func(name string) float64 {
			if data == nil {
				return math.NaN()
			}
			return number(data, prefix+name)
		}

		avg, zones := "-", ""
		if track := tracks[slot]; track != nil {
			avg = fmt.Sprintf("(%5.0f,%5.0f)", track.sumX/float64(track.samples), track.sumY/float64(track.samples))
			zones = fmt.Sprint(track.zones)
		}
		fmt.Printf("%-4d %-4.0f %-17d %-5s %4.0f %4.0f %6.0f %6.0f %4.0f %4.0f  %-16s %s\n",
			slot, number(resource, team+"m_nSelectedHeroID"), steamID, teamName(number(resource, player+"m_iPlayerTeam")),
			stat("m_iLastHitCount"), stat("m_iDenyCount"), stat("m_iNetWorth"), stat("m_iTotalEarnedXP"),
			number(resource, team+"m_iDeaths"), number(resource, team+"m_iLevel"), avg, zones)
	}
}

func teamName(team float64) string {
	switch team {
	case 2:
		return "rad"
	case 3:
		return "dire"
	default:
		return "?"
	}
}

// heroOf follows the player's hero handle; matching by class name would also pick up illusions.
func heroOf(parser *manta.Parser, resource *manta.Entity, slot int) *manta.Entity {
	handle, ok := resource.GetUint64(fmt.Sprintf("m_vecPlayerTeamData.%04d.m_hSelectedHero", slot))
	if !ok {
		return nil
	}
	return parser.FindEntityByHandle(handle)
}

// position converts the cell + offset coordinates to world units (map roughly -8200..8200, Radiant bottom-left).
func position(hero *manta.Entity) (float64, float64) {
	const cellWidth, worldOffset = 128, 16384
	x := number(hero, "CBodyComponent.m_cellX")*cellWidth + number(hero, "CBodyComponent.m_vecX") - worldOffset
	y := number(hero, "CBodyComponent.m_cellY")*cellWidth + number(hero, "CBodyComponent.m_vecY") - worldOffset
	return x, y
}

// zoneOf is a rough first guess at the lanes, to be refined in card 02.
func zoneOf(x, y float64) string {
	switch {
	case (x < -5000 && y > -3500) || (y > 5000 && x < 3500):
		return "top"
	case (y < -5000 && x > -3500) || (x > 5000 && y < 3500):
		return "bot"
	case math.Abs(x-y) < 1800:
		return "mid"
	default:
		return "other"
	}
}

// classIndex caches where each singleton entity lives. A global is fine for this one-shot CLI; the service keeps
// it per parse (concurrent parses would race on it).
var classIndex = map[string]int32{}

func findByClass(parser *manta.Parser, className string) *manta.Entity {
	if index, ok := classIndex[className]; ok {
		if e := parser.FindEntity(index); e != nil && e.GetClassName() == className {
			return e
		}
	}
	found := parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == className })
	if len(found) == 0 {
		return nil
	}
	classIndex[className] = found[0].GetIndex()
	return found[0]
}

// number reads any numeric property as float64 (the wire types vary between properties and patches).
func number(e *manta.Entity, name string) float64 {
	switch v := e.Get(name).(type) {
	case int32:
		return float64(v)
	case uint32:
		return float64(v)
	case int64:
		return float64(v)
	case uint64:
		return float64(v)
	case float32:
		return float64(v)
	case bool:
		if v {
			return 1
		}
		return 0
	default:
		return math.NaN()
	}
}
