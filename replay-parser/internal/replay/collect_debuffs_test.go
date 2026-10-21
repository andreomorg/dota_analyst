package replay

import (
	"context"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestBuffKind(t *testing.T) {
	for _, tc := range []struct{ name, want string }{
		{"modifier_teleporting", "teleport"}, {"modifier_smoke_of_deceit", "smoke"},
		{"modifier_invisible", "invisible"}, {"modifier_rune_invis", "invisible"},
		{"modifier_nyx_assassin_vendetta", "invisible"},
		{"modifier_item_black_king_bar_immune", "magicImmune"}, {"modifier_magic_immune", "magicImmune"},
		{"modifier_item_glimmer_cape_fade", "save"},
		{"modifier_bottle_regeneration", "heal"}, {"modifier_clarity_potion", "heal"},
		{"modifier_flask_healing", "heal"}, {"modifier_tango_heal", "heal"},
		{"modifier_mana_draught_regeneration", "heal"}, {"modifier_rune_regen", "heal"},
		{"modifier_item_mekansm", "heal"}, {"modifier_item_mekansm_aura", "heal"},
		{"modifier_item_guardian_greaves", "heal"}, {"modifier_item_guardian_greaves_aura", "heal"},
		{"modifier_item_headdress_aura", "heal"},
		{"modifier_item_mekansm_noheal", "other"}, {"modifier_nyx_assassin_vendetta_fast", "other"},
		{"modifier_riki_smoke_screen", "other"}, {"modifier_twin_gate_warp_channel", "other"},
		{"modifier_item_ring_of_basilius_effect", "other"}, {"", "other"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := buffKind(tc.name); got != tc.want {
				t.Fatalf("got %s, want %s", got, tc.want)
			}
		})
	}
}

func TestBuffsCollectorAllPick(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&playersCollector{}, &debuffsCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	teleports, self, ally := 0, 0, 0
	for i, b := range m.Combat.Buffs {
		if i > 0 && b.Start < m.Combat.Buffs[i-1].Start {
			t.Fatal("buffs out of order")
		}
		if b.End != nil && *b.End < b.Start {
			t.Errorf("negative duration: %+v", b)
		}
		if b.Source == nil || *b.Source < 0 || *b.Source >= len(m.Players) || b.Target < 0 || b.Target >= len(m.Players) {
			t.Fatalf("invalid players: %+v", b)
		}
		if m.Players[*b.Source].Team != m.Players[b.Target].Team {
			t.Errorf("enemy source: %+v", b)
		}
		if *b.Source == b.Target {
			self++
		} else {
			ally++
		}
		if b.Kind == "teleport" {
			teleports++
		}
	}
	if teleports == 0 || self == 0 || ally == 0 {
		t.Fatalf("expected teleports and self/ally applications: %d/%d/%d", teleports, self, ally)
	}
}

func TestBuffsPairing(t *testing.T) {
	players := &Players{list: []PlayerInfo{{Index: 0, Team: matchdata.Radiant}, {Index: 1, Team: matchdata.Radiant}, {Index: 2, Team: matchdata.Dire}},
		byHeroName: map[string]int{"npc_dota_hero_target": 0, "npc_dota_hero_ally": 1, "npc_dota_hero_enemy": 2}}
	s := &Session{Players: players, Clock: &Clock{}}
	event := func(at float64, author string, add bool, duration float64) debuffEvent {
		return debuffEvent{at: ServerTime{Seconds: at}, attacker: author, target: "npc_dota_hero_target", name: "modifier_teleporting", attackerHero: true, add: add, duration: duration}
	}
	c := debuffsCollector{session: s, seen: true, events: []debuffEvent{
		event(1, "npc_dota_hero_target", true, -1), event(2, "npc_dota_hero_ally", true, -1),
		event(3, "npc_dota_hero_enemy", false, -1), // Different known author cannot close either buff.
		event(4, "npc_dota_hero_ally", false, -1), event(5, "", false, -1),
		event(6, "npc_dota_hero_target", true, 3), event(10, "npc_dota_hero_target", true, -1),
		event(11, "", true, 1), // Unknown ADD must not become a self-buff.
	}}
	m := matchdata.New()
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	if len(m.Combat.Buffs) != 4 || len(m.Combat.Debuffs) != 0 {
		t.Fatalf("unexpected modifier counts: %+v", m.Combat)
	}
	for i, want := range []matchdata.Seconds{5, 4, 9} {
		if b := m.Combat.Buffs[i]; b.End == nil || *b.End != want {
			t.Errorf("row %d: %+v, want end %v", i, b, want)
		}
	}
	if m.Combat.Buffs[3].End != nil {
		t.Fatal("unknown end must stay null")
	}
}

func TestDebuffKind(t *testing.T) {
	for _, tc := range []struct{ name, want string }{
		{"modifier_stunned", "stun"}, {"modifier_bashed", "stun"}, {"modifier_silence", "silence"},
		{"modifier_rooted", "root"}, {"modifier_ensnare", "root"}, {"modifier_hexed", "hex"},
		{"modifier_sheepstick_debuff", "hex"}, {"modifier_lion_voodoo", "hex"},
		{"modifier_lich_frostnova_slow", "slow"}, {"modifier_nyx_assassin_impale", "other"},
		{"modifier_riki_smoke_screen", "other"}, {"", "other"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := debuffKind(tc.name); got != tc.want {
				t.Fatalf("got %s, want %s", got, tc.want)
			}
		})
	}
}

func TestDebuffsCollectorAllPick(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&playersCollector{}, &debuffsCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	stuns := 0
	for i, d := range m.Combat.Debuffs {
		if i > 0 && d.Start < m.Combat.Debuffs[i-1].Start {
			t.Fatal("debuffs out of order")
		}
		if d.End != nil && *d.End < d.Start {
			t.Errorf("negative duration: %+v", d)
		}
		if d.Source == nil || *d.Source < 0 || *d.Source >= len(m.Players) || d.Target < 0 || d.Target >= len(m.Players) {
			t.Fatalf("invalid players: %+v", d)
		}
		if m.Players[*d.Source].Team == m.Players[d.Target].Team {
			t.Errorf("allied source: %+v", d)
		}
		if d.Kind == "stun" {
			stuns++
		}
	}
	if stuns == 0 {
		t.Fatal("expected some stuns")
	}
}

// Exercises the cases a real-replay aggregate cannot distinguish: author collisions, missing authors,
// unmatched ADDs, indefinite durations, and shared illusion/summon ownership classification.
func TestDebuffsPairing(t *testing.T) {
	players := &Players{list: []PlayerInfo{{Index: 0, Team: matchdata.Radiant}, {Index: 1, Team: matchdata.Dire}, {Index: 2, Team: matchdata.Dire}},
		byHeroName: map[string]int{"target": 0, "npc_dota_hero_one": 1, "npc_dota_hero_two": 2}}
	s := &Session{Players: players, Clock: &Clock{}}
	event := func(at float64, attacker, name string, add bool, duration float64) debuffEvent {
		return debuffEvent{at: ServerTime{Seconds: at}, attacker: attacker, target: "target", name: name, attackerHero: true, add: add, duration: duration}
	}
	illusion := event(30, "npc_dota_hero_one", "modifier_slow", true, 2)
	illusion.attackerIllusion = true
	summon := event(40, "summon", "modifier_rooted", true, 3)
	summon.attackerHero = false
	summon.source = "npc_dota_hero_two"
	c := debuffsCollector{session: s, seen: true, events: []debuffEvent{
		event(1, "npc_dota_hero_one", "modifier_stunned", true, -1),
		event(2, "npc_dota_hero_two", "modifier_stunned", true, -1),
		event(3, "npc_dota_hero_two", "modifier_stunned", false, -1),
		event(4, "", "modifier_stunned", false, -1),
		event(10, "npc_dota_hero_one", "modifier_silence", true, 5),
		event(20, "npc_dota_hero_one", "modifier_unknown", true, -1),
		illusion, summon, event(50, "", "modifier_unknown", true, 1),
	}}
	m := matchdata.New()
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	if len(m.Combat.Debuffs) != 6 {
		t.Fatalf("got %d rows", len(m.Combat.Debuffs))
	}
	for i, want := range []matchdata.Seconds{4, 3, 15} {
		d := m.Combat.Debuffs[i]
		if d.End == nil || *d.End != want {
			t.Errorf("row %d: %+v, want end %v", i, d, want)
		}
	}
	if m.Combat.Debuffs[3].End != nil {
		t.Fatal("indefinite modifier must keep unknown end")
	}
	if *m.Combat.Debuffs[4].Source != 1 || *m.Combat.Debuffs[5].Source != 2 {
		t.Fatal("illusion/summon attribution failed")
	}
}
