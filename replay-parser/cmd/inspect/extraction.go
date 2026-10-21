package main

import (
	"fmt"
	"sort"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// runExtraction prints only numeric communication fields, avoiding accidental chat disclosure.
func runExtraction(p *manta.Parser) {
	counts := map[string]int{}
	p.Callbacks.OnCDOTAUserMsg_SpectatorPlayerUnitOrders(func(m *dota.CDOTAUserMsg_SpectatorPlayerUnitOrders) error {
		if m.GetOrderType() == 5 || m.GetOrderType() == 6 {
			counts["castOrders"]++
			if counts["castOrders"] <= 8 {
				e := p.FindEntity(m.GetAbilityId())
				fmt.Printf("cast tick=%d controller=%d ability=%d entity=%v target=%d x=%v y=%v queue=%v\n", p.NetTick, m.GetEntindex(), m.GetAbilityId(), e != nil, m.GetTargetIndex(), m.GetPosition().GetX(), m.GetPosition().GetY(), m.GetQueue())
				if e != nil {
					fmt.Println("abilityClass", e.GetClassName())
				}
			}
		}
		return nil
	})
	p.Callbacks.OnCDOTAUserMsg_LocationPing(func(m *dota.CDOTAUserMsg_LocationPing) error {
		counts["locationPing"]++
		if counts["locationPing"] <= 3 {
			q := m.GetLocationPing()
			fmt.Printf("ping player=%d x=%d y=%d type=%d\n", m.GetPlayerId(), q.GetX(), q.GetY(), q.GetType())
		}
		return nil
	})
	p.Callbacks.OnCDOTAUserMsg_ChatWheel(func(m *dota.CDOTAUserMsg_ChatWheel) error {
		counts["chatWheel"]++
		if counts["chatWheel"] <= 3 {
			fmt.Printf("wheel player=%d phrase=%d\n", m.GetPlayerId(), m.GetChatMessageId())
		}
		return nil
	})
	p.Callbacks.OnCDOTAUserMsg_ChatMessage(func(*dota.CDOTAUserMsg_ChatMessage) error { counts["chatMessage"]++; return nil })
	p.Callbacks.OnCDOTAUserMsg_Ping(func(*dota.CDOTAUserMsg_Ping) error { counts["networkPing"]++; return nil })
	p.Callbacks.OnCMsgDOTACombatLogEntry(func(m *dota.CMsgDOTACombatLogEntry) error {
		if m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD || m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_XP {
			key := fmt.Sprintf("%s goldReason=%v:%d xpReason=%v:%d", m.GetType(), m.GoldReason != nil, m.GetGoldReason(), m.XpReason != nil, m.GetXpReason())
			counts[key]++
		}
		return nil
	})
	seen := map[string]bool{}
	p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
		name := e.GetClassName()
		if strings.Contains(name, "Rune") && !seen[name] {
			seen[name] = true
			fmt.Println("class", name)
			keys := []string{}
			for k := range e.Map() {
				keys = append(keys, k)
			}
			sort.Strings(keys)
			for _, k := range keys {
				fmt.Printf("  %s=%v\n", k, e.Map()[k])
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
