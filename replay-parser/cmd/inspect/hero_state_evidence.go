package main

import (
	"encoding/json"
	"fmt"
	"log"
	"os"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// This probe inventories WIRE FIELDS, not effective HUD stats. Never print names, Steam IDs, chat or PlayerIds.
func runHeroStateEvidence(p *manta.Parser, out string) {
	type example struct {
		Tick                                      uint32  `json:"tick"`
		Parent                                    *uint32 `json:"parent"`
		Class                                     string  `json:"entityClass"`
		ModifierClass                             *int32  `json:"modifierClass"`
		Name                                      string  `json:"modifierName"`
		Index                                     *int32  `json:"index"`
		Serial                                    *int32  `json:"serial"`
		Active                                    *bool   `json:"active"`
		Armor, Damage, Speed, AttackSpeed, Stacks *int32
		Level                                     *int32
	}
	counts := map[string]int{}
	names := map[string]map[string]int{}
	rows := []example{}
	p.OnModifierTableEntry(func(m *dota.CDOTAModifierBuffTableEntry) error {
		counts["entries"]++
		class := ""
		if m.Parent != nil {
			if entity := p.FindEntityByHandle(uint64(*m.Parent)); entity != nil {
				class = entity.GetClassName()
			}
		}
		name := m.GetLuaName()
		if name == "" && m.ModifierClass != nil {
			for _, table := range []string{"ModifierNames", "ModifierTypes", "ModifierClassNames"} {
				if resolved, ok := p.LookupStringByIndex(table, *m.ModifierClass); ok {
					name = resolved
					break
				}
			}
		}
		if strings.Contains(class, "Hero") {
			counts["resolvedHeroEntries"]++
		}
		fields := map[string]*int32{"armor": m.Armor, "damage": m.Damage, "movementSpeed": m.MovementSpeed, "attackSpeed": m.AttackSpeed, "stackCount": m.StackCount}
		hasComponent := false
		for key, value := range fields {
			if value == nil {
				continue
			}
			counts[key+"Present"]++
			if *value != 0 {
				counts[key+"Nonzero"]++
				if names[key] == nil {
					names[key] = map[string]int{}
				}
				names[key][name]++
				if key != "stackCount" {
					hasComponent = true
				}
			}
		}
		if hasComponent {
			counts["eligibleExampleUpdates"]++
		}
		if hasComponent && len(rows) < 2000 {
			rows = append(rows, example{p.NetTick, m.Parent, class, m.ModifierClass, name, m.Index, m.SerialNum, m.Active,
				m.Armor, m.Damage, m.MovementSpeed, m.AttackSpeed, m.StackCount, m.AbilityLevel})
		}
		return nil
	})
	if err := p.Start(); err != nil {
		log.Fatal(err)
	}
	result := struct {
		Note     string                    `json:"note"`
		Scope    string                    `json:"scope"`
		Counts   map[string]int            `json:"counts"`
		Names    map[string]map[string]int `json:"namesByComponent"`
		Examples []example                 `json:"examples"`
	}{"Raw replicated modifier fields; not summed or classified as effective totals. Examples are the first 2000 nonzero component updates; not a representative sample. Tick is a network tick, not game seconds.",
		"Counts include all entities and repeated updates. Hero classes may include illusions. Active null/false is preserved; parent is a raw handle, not canonical player identity. Deleted modifier entries are not exposed by this callback.", counts, names, rows}
	data, err := json.MarshalIndent(result, "", "  ")
	if err != nil {
		log.Fatal(err)
	}
	if out == "" {
		fmt.Println(string(data))
		return
	}
	if err := os.WriteFile(out, data, 0600); err != nil {
		log.Fatal(err)
	}
	fmt.Printf("modifier evidence: %d entries, %d component examples; %s\n", counts["entries"], len(rows), out)
}
