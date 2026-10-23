package main

import (
	"fmt"
	"github.com/dotabuff/manta"
	"log"
	"math"
	"runtime"
	"time"
)

const ticksPerSecond = 30

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
func heroOf(parser *manta.Parser, resource *manta.Entity, slot int) *manta.Entity {
	handle, ok := resource.GetUint64(fmt.Sprintf("m_vecPlayerTeamData.%04d.m_hSelectedHero", slot))
	if !ok {
		return nil
	}
	return parser.FindEntityByHandle(handle)
}
func position(hero *manta.Entity) (float64, float64) {
	const cellWidth, worldOffset = 128, 16384
	x := number(hero, "CBodyComponent.m_cellX")*cellWidth + number(hero, "CBodyComponent.m_vecX") - worldOffset
	y := number(hero, "CBodyComponent.m_cellY")*cellWidth + number(hero, "CBodyComponent.m_vecY") - worldOffset
	return x, y
}
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

var classIndex = map[string]int32{}
