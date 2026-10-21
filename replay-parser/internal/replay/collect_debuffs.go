package replay

import (
	"cmp"
	"math"
	"slices"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

// debuffsCollector records modifier lifetimes on real heroes. Classification reuses combatCollector.
type debuffsCollector struct {
	session *Session
	events  []debuffEvent
	seen    bool
}

type debuffEvent struct {
	at                                  ServerTime
	attacker, source, target, name      string
	attackerHero, attackerIllusion, add bool
	duration                            float64 // negative means absent/indefinite
}

func (c *debuffsCollector) Register(s *Session) {
	c.session = s
	s.OnCombatLog(func(e CombatEntry) {
		c.seen = true
		if !e.Raw.GetIsTargetHero() || e.Raw.GetIsTargetIllusion() || e.Inflictor == "" {
			return
		}
		duration := -1.0
		if e.Raw.ModifierDuration != nil {
			duration = float64(e.Raw.GetModifierDuration())
		}
		c.events = append(c.events, debuffEvent{at: e.At, attacker: e.Attacker, source: e.Source, target: e.Target, name: e.Inflictor,
			attackerHero: e.Raw.GetIsAttackerHero(), attackerIllusion: e.Raw.GetIsAttackerIllusion(),
			add: e.Type == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MODIFIER_ADD, duration: duration})
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MODIFIER_ADD, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MODIFIER_REMOVE)
}

func (c *debuffsCollector) Fill(m *matchdata.Match) error {
	if !c.seen {
		return nil
	}
	classifier := combatCollector{session: c.session}
	// Stable timestamp order also preserves ADD/REMOVE order within the same tick.
	slices.SortStableFunc(c.events, func(a, b debuffEvent) int { return cmp.Compare(a.at.Seconds, b.at.Seconds) })
	type key struct {
		target int
		name   string
	}
	type pending struct {
		row      matchdata.Debuff
		duration float64
		enemy    bool
		source   int
	}
	var pendingRows []pending
	active := map[key][]int{}
	for _, e := range c.events {
		if classifier.afterEnd(e.at) {
			continue
		}
		target, ok := c.session.Players.ByHeroName(e.target)
		if !ok {
			continue
		}
		attacker := -1
		if e.attackerHero {
			if p, ok := c.session.Players.ByHeroName(e.attacker); ok {
				attacker = p
			}
		}
		// Only the flags consumed by the shared attacker classifier are reconstructed.
		raw := &dota.CMsgDOTACombatLogEntry{IsAttackerHero: &e.attackerHero, IsAttackerIllusion: &e.attackerIllusion}
		source := classifier.attacker(CombatEntry{Raw: raw, Attacker: e.attacker, Source: e.source, AttackerPlayer: attacker}).player
		k := key{target, e.name}
		if e.add {
			row := matchdata.Debuff{Start: c.session.Clock.Seconds(e.at), Source: combatPlayer(source), Target: target, Modifier: e.name, Kind: debuffKind(e.name)}
			active[k] = append(active[k], len(pendingRows))
			pendingRows = append(pendingRows, pending{row: row, duration: e.duration, enemy: classifier.enemies(source, target), source: source})
			continue
		}
		queue := active[k]
		if len(queue) == 0 {
			continue
		}
		// Prefer the same known author; unknown REMOVE authors use FIFO. Known different authors never pair.
		index := -1
		for i, p := range queue {
			if source < 0 || pendingRows[p].source == source {
				index = i
				break
			}
		}
		if index < 0 {
			continue
		}
		end := c.session.Clock.Seconds(e.at)
		pendingRows[queue[index]].row.End = &end
		active[k] = append(queue[:index], queue[index+1:]...)
	}
	m.Combat.Debuffs = make(matchdata.List[matchdata.Debuff], 0)
	m.Combat.Buffs = make(matchdata.List[matchdata.Debuff], 0)
	for _, p := range pendingRows {
		// Unknown ADD owners are omitted from both sections: self/ally application cannot be proved.
		// In particular, a missing author is never assumed to mean self. Unknown REMOVE uses FIFO above.
		if p.source < 0 {
			continue
		}
		if p.row.End == nil && p.duration >= 0 && !math.IsNaN(p.duration) && !math.IsInf(p.duration, 0) {
			end := p.row.Start + matchdata.Seconds(p.duration)
			p.row.End = &end
		}
		if p.enemy {
			m.Combat.Debuffs = append(m.Combat.Debuffs, p.row)
		} else {
			p.row.Kind = buffKind(p.row.Modifier)
			m.Combat.Buffs = append(m.Combat.Buffs, p.row)
		}
	}
	return nil
}

// buffKind is lexical, not a per-patch ability database. Exact names below were observed in
// 9034320024; invis and black_king_bar/magic_immune are conservative lexical families (the latter
// absent in that replay). Glimmer fade is classified as save before the invis family. Other includes
// passive auras/counters and does not establish that a modifier is beneficial; noheal is not healing.
func buffKind(name string) string {
	switch {
	case name == "modifier_teleporting":
		return "teleport"
	case name == "modifier_smoke_of_deceit":
		return "smoke"
	case name == "modifier_item_glimmer_cape_fade":
		return "save"
	case strings.Contains(name, "invis"), name == "modifier_nyx_assassin_vendetta":
		return "invisible"
	case strings.Contains(name, "black_king_bar"), strings.Contains(name, "magic_immune"):
		return "magicImmune"
	case name == "modifier_bottle_regeneration", name == "modifier_clarity_potion",
		name == "modifier_flask_healing", name == "modifier_tango_heal",
		name == "modifier_mana_draught_regeneration", name == "modifier_rune_regen",
		name == "modifier_item_mekansm", name == "modifier_item_mekansm_aura",
		name == "modifier_item_guardian_greaves", name == "modifier_item_guardian_greaves_aura",
		name == "modifier_item_headdress_aura":
		return "heal"
	default:
		return "other"
	}
}

// debuffKind is deliberately lexical, not a patch-specific ability database. Precedence is stun, silence,
// root, hex, slow; only the two established item/ability hex names supplement these substrings.
// Unknown ability names (even when they disable in-game) remain other; modifiers can also be cosmetic.
func debuffKind(name string) string {
	switch {
	case strings.Contains(name, "stun"), strings.Contains(name, "bash"):
		return "stun"
	case strings.Contains(name, "silence"):
		return "silence"
	case strings.Contains(name, "root"), strings.Contains(name, "ensnare"):
		return "root"
	case strings.Contains(name, "hex"), name == "modifier_sheepstick_debuff", name == "modifier_lion_voodoo":
		return "hex"
	case strings.Contains(name, "slow"):
		return "slow"
	default:
		return "other"
	}
}
