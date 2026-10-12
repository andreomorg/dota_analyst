package replay

import (
	"bytes"
	"compress/gzip"
	"context"
	"encoding/json"
	"math"
	"slices"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// scoreboardProbe reads each player's scoreboard numbers when the match ends, to check the combat log against them.
type scoreboardProbe struct {
	kills, deaths, assists [playerCount]int
	heroDamage, healing    [playerCount]float64
	buybackGold            [playerCount]int
	teams                  [playerCount]matchdata.Team
}

func (p *scoreboardProbe) Register(s *Session) {
	s.OnEnd(func(int) {
		for i := range playerCount {
			resource, _, team := s.Players.Resource(i)
			data, prefix := s.Players.TeamData(i)
			f, d := fields{entity: resource}, fields{entity: data}
			p.kills[i] = f.int(team + "m_iKills")
			p.deaths[i] = f.int(team + "m_iDeaths")
			p.assists[i] = f.int(team + "m_iAssists")
			p.heroDamage[i] = d.float(prefix + "m_flHeroDamage")
			p.healing[i] = d.float(prefix + "m_fHealing")
			p.buybackGold[i] = d.int(prefix + "m_iGoldSpentOnBuybacks")
			player, _ := s.Players.Get(i)
			p.teams[i] = player.Team
		}
	})
}

func (p *scoreboardProbe) Fill(*matchdata.Match) error { return nil }

func TestCombatCollectorRealReplays(t *testing.T) {
	tests := []struct {
		name     string
		replay   realReplay
		deaths   int   // hero deaths in the combat log, Aegis included
		buybacks []int // players who bought back, in order
		// OpenDota's K/D for the Turbo match (players 0-9); nil when OpenDota did not parse the match.
		openDotaKills, openDotaDeaths []int
	}{
		{"All Pick 9034320024", allPickReplay, 82, nil, nil, nil},
		{"Turbo 9041133074", turboReplay, 65, []int{2},
			[]int{3, 2, 5, 9, 3, 13, 6, 3, 10, 6}, []int{9, 9, 8, 6, 8, 4, 4, 9, 1, 6}},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			r := test.replay.open(t)
			probe := &scoreboardProbe{}
			match, err := analyzeMatch(context.Background(), r, []Collector{&matchCollector{}, &combatCollector{}, probe})
			if err != nil {
				t.Fatal(err)
			}
			if len(match.Kills) != test.deaths {
				t.Errorf("%d hero deaths, want %d", len(match.Kills), test.deaths)
			}

			// K/D per player against the scoreboard (and OpenDota). The scoreboard leaves out Aegis deaths, credits a
			// kill only against an enemy, and credits a death to a creep or tower with a single assisting hero to that
			// hero (Turbo 15.3 s: tower kill, Skywrath's 13th kill; OpenDota's kills_log also misses it).
			var kills, deaths, assists [playerCount]int
			for _, kill := range match.Kills {
				if kill.Reincarnated {
					continue
				}
				deaths[kill.Victim]++
				switch {
				case kill.Killer != nil && probe.teams[*kill.Killer] != probe.teams[kill.Victim]:
					kills[*kill.Killer]++
				case kill.Killer == nil && len(kill.Assists) == 1:
					kills[kill.Assists[0]]++
					continue
				}
				for _, a := range kill.Assists {
					assists[a]++
				}
			}
			for i := range playerCount {
				t.Logf("player %d: K %d/%d D %d/%d A %d/%d (combat log / scoreboard)", i, kills[i], probe.kills[i],
					deaths[i], probe.deaths[i], assists[i], probe.assists[i])
				if kills[i] != probe.kills[i] || deaths[i] != probe.deaths[i] {
					t.Errorf("player %d: K/D %d/%d, scoreboard %d/%d", i, kills[i], deaths[i], probe.kills[i], probe.deaths[i])
				}
				if test.openDotaKills != nil && (kills[i] != test.openDotaKills[i] || deaths[i] != test.openDotaDeaths[i]) {
					t.Errorf("player %d: K/D %d/%d, OpenDota %d/%d", i, kills[i], deaths[i], test.openDotaKills[i],
						test.openDotaDeaths[i])
				}
				// assist_players is a subset: the scoreboard also credits heroes the combat log does not list.
				if assists[i] > probe.assists[i] {
					t.Errorf("player %d: %d assists, more than the scoreboard's %d", i, assists[i], probe.assists[i])
				}
			}
			checkKills(t, match)

			var buybacks []int
			for _, b := range match.Buybacks {
				buybacks = append(buybacks, b.Player)
				if b.Cost == nil || *b.Cost != probe.buybackGold[b.Player] {
					t.Errorf("buyback %+v: cost %v, scoreboard gold spent on buybacks %d", b, b.Cost, probe.buybackGold[b.Player])
				}
			}
			if !slices.Equal(buybacks, test.buybacks) {
				t.Errorf("buybacks by %v, want %v", buybacks, test.buybacks)
			}

			// Hero damage against m_flHeroDamage: equal for heroes, illusions included; Tombstone zombies (Undying,
			// All Pick) are ~3% off. Healing to other heroes against m_fHealing: mostly equal (some ally heals, e.g.
			// Tidehunter's in All Pick, are not in the combat log).
			heroDamage, healing := combatTotals(match.Combat, probe.teams)
			for i := range playerCount {
				t.Logf("player %d: hero damage hero %d +illusions %d +summons %d / scoreboard %.0f; healing %d (others %d) / %.0f",
					i, heroDamage[i][0], heroDamage[i][1], heroDamage[i][2], probe.heroDamage[i], healing[i][0], healing[i][1],
					probe.healing[i])
				if diff := math.Abs(float64(heroDamage[i][2]) - probe.heroDamage[i]); diff > 0.05*probe.heroDamage[i]+10 {
					t.Errorf("player %d: hero damage %d, scoreboard %.0f", i, heroDamage[i][2], probe.heroDamage[i])
				}
				if float64(healing[i][1]) > probe.healing[i]+1 {
					t.Errorf("player %d: healing to others %d, scoreboard %.0f", i, healing[i][1], probe.healing[i])
				}
			}
			checkPerMinute(t, match, heroDamage, healing)

			sections, err := json.Marshal(struct {
				Kills    any `json:"kills"`
				Buybacks any `json:"buybacks"`
				Combat   any `json:"combat"`
			}{match.Kills, match.Buybacks, match.Combat})
			if err != nil {
				t.Fatal(err)
			}
			var zipped bytes.Buffer
			zip := gzip.NewWriter(&zipped)
			_, _ = zip.Write(sections)
			_ = zip.Close()
			t.Logf("kills %d, buybacks %d, damage rows %d, healing rows %d; kills+buybacks+combat JSON %d bytes (%d gzip)",
				len(match.Kills), len(match.Buybacks), len(match.Combat.Damage), len(match.Combat.Healing), len(sections),
				zipped.Len())
			if len(sections) > 150_000 {
				t.Errorf("combat sections take %d bytes", len(sections))
			}
		})
	}
}

// checkKills checks what every kill must have: time order, a position on the map, assists that are neither the
// killer nor the victim, and exactly one first blood.
func checkKills(t *testing.T, match matchdata.Match) {
	t.Helper()
	firstBloods := 0
	for i, kill := range match.Kills {
		if i > 0 && kill.T < match.Kills[i-1].T {
			t.Errorf("kill %d at %.1f before the previous one", i, kill.T)
		}
		if kill.X == 0 && kill.Y == 0 || math.Abs(float64(kill.X)) > 9000 || math.Abs(float64(kill.Y)) > 9000 {
			t.Errorf("kill %d at (%d, %d)", i, kill.X, kill.Y)
		}
		if kill.Killer != nil && slices.Contains(kill.Assists, *kill.Killer) || slices.Contains(kill.Assists, kill.Victim) {
			t.Errorf("kill %d: killer or victim among the assists %v", i, kill.Assists)
		}
		if kill.KillerUnit == "" || kill.T < 0 || float64(kill.T) > float64(match.Match.DurationSeconds)+1 {
			t.Errorf("kill %d: %+v", i, kill)
		}
		if kill.FirstBlood {
			firstBloods++
			if i != 0 {
				t.Errorf("first blood on kill %d", i)
			}
		}
	}
	if firstBloods != 1 {
		t.Errorf("%d first bloods", firstBloods)
	}
}

// combatTotals adds up each player's damage to enemy heroes ([0] hero only, [1] with illusions, [2] with illusions and
// summons) and healing to heroes ([0] itself included, [1] other heroes only).
func combatTotals(combat matchdata.Combat, teams [playerCount]matchdata.Team) (damage, healing [playerCount][3]int) {
	for _, row := range combat.Damage {
		if row.SourcePlayer == nil || row.TargetPlayer == nil || row.TargetKind != matchdata.KindHero ||
			teams[*row.SourcePlayer] == teams[*row.TargetPlayer] {
			continue
		}
		switch row.SourceKind {
		case matchdata.KindHero:
			damage[*row.SourcePlayer][0] += row.Amount
			fallthrough
		case matchdata.KindIllusion:
			damage[*row.SourcePlayer][1] += row.Amount
			fallthrough
		default:
			damage[*row.SourcePlayer][2] += row.Amount
		}
	}
	for _, row := range combat.Healing {
		if row.SourcePlayer == nil || row.TargetPlayer == nil || row.TargetKind != matchdata.KindHero {
			continue
		}
		healing[*row.SourcePlayer][0] += row.Amount
		if *row.TargetPlayer != *row.SourcePlayer {
			healing[*row.SourcePlayer][1] += row.Amount
		}
	}
	return damage, healing
}

// checkPerMinute checks that the per-minute lists add up to the rows and cover the whole match.
func checkPerMinute(t *testing.T, match matchdata.Match, heroDamage, healing [playerCount][3]int) {
	t.Helper()
	minutes := int(float64(match.Match.DurationSeconds)/60) + 1
	if len(match.Combat.PerMinute) != playerCount {
		t.Fatalf("%d per-minute entries", len(match.Combat.PerMinute))
	}
	for i, p := range match.Combat.PerMinute {
		if p.Player != i || len(p.DamageToHeroes) != minutes || len(p.DamageFromHeroes) != minutes ||
			len(p.DamageTaken) != minutes || len(p.Healing) != minutes {
			t.Errorf("player %d: lengths %d/%d/%d/%d, want %d", i, len(p.DamageToHeroes), len(p.DamageFromHeroes),
				len(p.DamageTaken), len(p.Healing), minutes)
		}
		sum := func(l matchdata.List[int]) (total int) {
			for _, v := range l {
				total += v
			}
			return total
		}
		if got := sum(p.DamageToHeroes); got != heroDamage[i][2] {
			t.Errorf("player %d: per-minute damage to heroes %d, rows %d", i, got, heroDamage[i][2])
		}
		if got := sum(p.Healing); got != healing[i][1] {
			t.Errorf("player %d: per-minute healing %d, rows %d", i, got, healing[i][1])
		}
		if sum(p.DamageTaken) < sum(p.DamageFromHeroes) {
			t.Errorf("player %d: took less damage than from heroes", i)
		}
	}
}

func TestCombatUnitKind(t *testing.T) {
	tests := []struct {
		name     string
		building bool
		want     matchdata.UnitKind
	}{
		{"npc_dota_roshan", false, matchdata.KindRoshan},
		{"npc_dota_miniboss", false, matchdata.KindTormentor},
		{"npc_dota_neutral_centaur_khan", false, matchdata.KindNeutrals},
		{"npc_dota_dark_troll_warlord_skeleton_warrior", false, matchdata.KindNeutrals},
		{"npc_dota_creep_goodguys_melee_upgraded", false, matchdata.KindLaneCreeps},
		{"npc_dota_creep_badguys_flagbearer", false, matchdata.KindLaneCreeps},
		{"npc_dota_badguys_siege", false, matchdata.KindLaneCreeps},
		{"npc_dota_goodguys_tower1_mid", false, matchdata.KindBuildings},
		{"npc_dota_badguys_fort", true, matchdata.KindBuildings},
		{"dota_fountain", false, matchdata.KindBuildings},
		{"npc_dota_observer_wards", false, matchdata.KindOtherUnits},
		{"npc_dota_courier", false, matchdata.KindOtherUnits},
	}
	for _, test := range tests {
		if got := combatUnitKind(test.name, test.building); got != test.want {
			t.Errorf("%s: %s, want %s", test.name, got, test.want)
		}
	}
}

func TestCombatDamageType(t *testing.T) {
	for value, want := range map[uint32]matchdata.DamageType{0: "", 1: matchdata.Physical, 2: matchdata.Magical,
		4: matchdata.Pure, 8: ""} {
		if got := combatDamageType(value); got != want {
			t.Errorf("%d: %q, want %q", value, got, want)
		}
	}
}
