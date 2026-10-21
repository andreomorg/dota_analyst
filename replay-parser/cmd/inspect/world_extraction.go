package main

import (
	"fmt"
	"sort"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Keep the probe restricted to world events and internal names; never print arbitrary player messages.
func runWorldExtraction(p *manta.Parser) {
	counts := map[string]int{}
	p.Callbacks.OnCDOTAUserMsg_OutpostCaptured(func(m *dota.CDOTAUserMsg_OutpostCaptured) error {
		fmt.Printf("outpost tick=%d entity=%d team=%d\n", p.NetTick, m.GetOutpostEntindex(), m.GetTeamId())
		counts["outpostMessages"]++
		return nil
	})
	p.Callbacks.OnCMsgDOTACombatLogEntry(func(m *dota.CMsgDOTACombatLogEntry) error {
		name := func(id uint32) string { v, _ := p.LookupStringByIndex("CombatLogNames", int32(id)); return v }
		a, b, i := name(m.GetAttackerName()), name(m.GetTargetName()), name(m.GetInflictorName())
		if strings.Contains(a+b+i, "lotus") || strings.Contains(a+b+i, "famango") || strings.Contains(a+b+i, "miniboss") {
			key := fmt.Sprintf("%s attacker=%s target=%s inflictor=%s", m.GetType(), a, b, i)
			counts[key]++
			if counts[key] <= 2 {
				fmt.Printf("worldlog tick=%d time=%.2f value=%d %s\n", p.NetTick, m.GetTimestamp(), m.GetValue(), key)
			}
		}
		if m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE {
			counts[fmt.Sprintf("damageType=%d present=%v", m.GetDamageType(), m.DamageType != nil)]++
		}
		if m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_HEAL {
			counts["healEvents"]++
		}
		return nil
	})
	seen := map[string]bool{}
	lastMiniTick := uint32(0)
	eventNames := map[int32]string{}
	p.Callbacks.OnCMsgSource1LegacyGameEventList(func(m *dota.CMsgSource1LegacyGameEventList) error {
		for _, d := range m.GetDescriptors() {
			eventNames[d.GetEventid()] = d.GetName()
		}
		return nil
	})
	p.Callbacks.OnCMsgSource1LegacyGameEvent(func(m *dota.CMsgSource1LegacyGameEvent) error {
		n := eventNames[m.GetEventid()]
		counts["gameEvent "+n]++
		if strings.Contains(n, "lotus") || strings.Contains(n, "item_picked") {
			fmt.Printf("pickupEvent tick=%d name=%s keys=%v\n", p.NetTick, n, m.GetKeys())
		}
		return nil
	})
	p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
		class := e.GetClassName()
		unit := ""
		if class == "CDOTA_Unit_Miniboss" && (lastMiniTick == 0 || p.NetTick-lastMiniTick >= 300) {
			lastMiniTick = p.NetTick
			fmt.Printf("minipos tick=%d x=%v+%v y=%v+%v life=%v\n", p.NetTick, e.Get("CBodyComponent.m_cellX"), e.Get("CBodyComponent.m_vecX"), e.Get("CBodyComponent.m_cellY"), e.Get("CBodyComponent.m_vecY"), e.Get("m_lifeState"))
		}
		if v, ok := e.GetInt32("m_iUnitNameStringTableIndex"); ok {
			unit, _ = p.LookupStringByIndex("UnitNames", v)
		}
		if strings.Contains(class, "Miniboss") || strings.Contains(unit, "miniboss") || class == "CDOTA_BaseNPC_Watch_Tower" || class == "CDOTA_BaseNPC_LotusPool" {
			key := fmt.Sprintf("%s unit=%s entity=%d", class, unit, e.GetIndex())
			if !seen[key] {
				seen[key] = true
				fmt.Printf("worldentity tick=%d %s\n", p.NetTick, key)
				names := []string{}
				for k := range e.Map() {
					names = append(names, k)
				}
				sort.Strings(names)
				for _, k := range names {
					if strings.Contains(k, "Team") || strings.Contains(k, "cell") || strings.Contains(k, "vecX") || strings.Contains(k, "vecY") || strings.Contains(k, "lifeState") {
						fmt.Printf("  %s=%v\n", k, e.Get(k))
					}
				}
			}
			if op.Flag(manta.EntityOpCreated) {
				counts["created "+class+" unit="+unit]++
			}
		}
		return nil
	})
	if err := p.Start(); err != nil {
		fmt.Println("parse error", err)
	}
	keys := []string{}
	for k := range counts {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	for _, k := range keys {
		fmt.Printf("%s: %d\n", k, counts[k])
	}
}
