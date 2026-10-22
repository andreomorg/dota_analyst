package main

import (
	"bytes"
	"encoding/json"
	"flag"
	"fmt"
	"math"
	"os"
	"sort"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// A self-contained flag keeps this optional probe out of the regular inspector path.
func init() {
	flag.Func("neutral-sizes", "measure comma-separated local JSON documents and exit", func(paths string) error {
		for _, path := range strings.Split(paths, ",") {
			raw, err := os.ReadFile(path)
			if err != nil {
				return err
			}
			var compact bytes.Buffer
			if err = json.Compact(&compact, raw); err != nil {
				return err
			}
			var m struct {
				Items struct {
					NeutralCrafts []struct {
						Crafts []json.RawMessage `json:"crafts"`
					} `json:"neutralCrafts"`
				} `json:"items"`
				Timelines struct {
					Madstones []struct {
						Samples []json.RawMessage `json:"samples"`
					} `json:"madstones"`
				} `json:"timelines"`
			}
			if err = json.Unmarshal(raw, &m); err != nil {
				return err
			}
			crafts, samples := 0, 0
			for _, p := range m.Items.NeutralCrafts {
				crafts += len(p.Crafts)
			}
			for _, p := range m.Timelines.Madstones {
				samples += len(p.Samples)
			}
			fmt.Printf("%s JSON=%d gzip=%d crafts=%d madstoneSamples=%d\n", path, compact.Len(), gzipSize(compact.Bytes()), crafts, samples)
		}
		os.Exit(0)
		return nil
	})
	flag.Func("neutral-spike", "inspect neutral evidence in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		p, err := manta.NewStreamParser(f)
		if err != nil {
			return err
		}
		counts := map[string]int{}
		type found struct {
			tick uint32
			msg  *dota.CDOTAUserMsg_FoundNeutralItem
		}
		type itemReading struct {
			player, slot int
			purchase     float64
			name         string
			level        int32
		}
		var messages []found
		var readings []itemReading
		example := func(key string, v any) {
			counts[key]++
			if counts[key] <= 3 || key == "FoundNeutralItem" {
				fmt.Printf("%s tick=%d %v\n", key, p.NetTick, v)
			}
		}
		p.Callbacks.OnCDOTAUserMsg_FoundNeutralItem(func(m *dota.CDOTAUserMsg_FoundNeutralItem) error {
			example("FoundNeutralItem", m)
			messages = append(messages, found{p.NetTick, m})
			return nil
		})
		p.Callbacks.OnCDOTAUserMsg_MadstoneAlert(func(m *dota.CDOTAUserMsg_MadstoneAlert) error { example("MadstoneAlert", m); return nil })
		p.Callbacks.OnCDOTAUserMsg_NeutralCraftAvailable(func(m *dota.CDOTAUserMsg_NeutralCraftAvailable) error {
			example("NeutralCraftAvailable", m)
			return nil
		})
		p.Callbacks.OnCDOTAUserMsg_NeutralCampAlert(func(m *dota.CDOTAUserMsg_NeutralCampAlert) error { example("NeutralCampAlert", m); return nil })
		p.Callbacks.OnCMsgDOTACombatLogEntry(func(m *dota.CMsgDOTACombatLogEntry) error {
			if m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_NEUTRAL_ITEM_EARNED || m.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_NEUTRAL_CAMP_STACK {
				example(m.GetType().String(), m)
			}
			return nil
		})
		seen := map[string]string{}
		lastTick := uint32(0)
		p.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
			active, _ := e.GetBool("m_bIsNeutralActiveDrop")
			passive, _ := e.GetBool("m_bIsNeutralPassiveDrop")
			if p.NetTick >= lastTick+15 || (active || passive) && !op.Flag(manta.EntityOpDeleted) {
				lastTick = p.NetTick
				resources := p.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTA_PlayerResource" })
				if len(resources) > 0 {
					for player := 0; player < 10; player++ {
						handle, _ := resources[0].GetUint64(fmt.Sprintf("m_vecPlayerTeamData.%04d.m_hSelectedHero", player))
						hero := p.FindEntityByHandle(handle)
						if hero == nil {
							continue
						}
						for _, slot := range []int{16, 17} {
							h, _ := hero.GetUint32(fmt.Sprintf("m_hItems.%04d", slot))
							item := p.FindEntityByHandle(uint64(h))
							if item == nil {
								continue
							}
							purchase, _ := item.GetFloat32("m_flPurchaseTime")
							level, _ := item.GetInt32("m_iLevel")
							index, _ := item.GetInt32("m_pEntity.m_nameStringTableIndex")
							name, _ := p.LookupStringByIndex("EntityNames", index)
							key := fmt.Sprintf("slot:%d:%d:%f:%s", player, slot, purchase, name)
							if seen[key] == "" {
								seen[key] = "1"
								readings = append(readings, itemReading{player, slot, float64(purchase), name, level})
							}
						}
					}
				}
			}
			if op.Flag(manta.EntityOpDeleted) || !strings.HasPrefix(e.GetClassName(), "CDOTA_Data") {
				return nil
			}
			for k, v := range e.Map() {
				if strings.Contains(k, "NeutralItemsTierInfo") || strings.Contains(k, "Madstone") || strings.Contains(k, "NeutralTokensFound") || strings.Contains(k, "NeutralKillGold") {
					key := fmt.Sprintf("%s.%s", e.GetClassName(), k)
					value := fmt.Sprint(v)
					if seen[key] != value {
						seen[key] = value
						fmt.Printf("field tick=%d %s=%s\n", p.NetTick, key, value)
					}
				}
			}
			return nil
		})
		if err = p.Start(); err != nil {
			return err
		}
		paired := 0
		for _, m := range messages {
			matches := 0
			for _, r := range readings {
				if r.player == int(m.msg.GetPlayerId()) && math.Abs(r.purchase-float64(m.tick)/30) <= 1 {
					fmt.Printf("PAIR tick=%d player=%d tier=%d slot=%d name=%s level=%d purchase=%.3f delta=%.3f\n", m.tick, r.player, m.msg.GetItemTier()+1, r.slot, r.name, r.level, r.purchase, r.purchase-float64(m.tick)/30)
					matches++
				}
			}
			if matches == 2 {
				paired++
			}
		}
		fmt.Printf("PAIRED_BOTH %d/%d\n", paired, len(messages))
		keys := []string{"FoundNeutralItem", "MadstoneAlert", "NeutralCraftAvailable", "NeutralCampAlert", "DOTA_COMBATLOG_NEUTRAL_ITEM_EARNED", "DOTA_COMBATLOG_NEUTRAL_CAMP_STACK"}
		sort.Strings(keys)
		for _, k := range keys {
			fmt.Printf("COUNT %s=%d\n", k, counts[k])
		}
		os.Exit(0)
		return nil
	})
}
