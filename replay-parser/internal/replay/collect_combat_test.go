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
			checkHitsAndCreepKills(t, match)

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
			var fields map[string]json.RawMessage
			combatJSON, err := json.Marshal(match.Combat)
			if err != nil {
				t.Fatal(err)
			}
			if err := json.Unmarshal(combatJSON, &fields); err != nil {
				t.Fatal(err)
			}
			for _, name := range []string{"economy", "debuffs", "buffs", "damage", "healing", "perMinute", "buckets", "heroHits", "laneCreepDeaths", "creepKills"} {
				t.Logf("combat.%s JSON: %d bytes", name, len(fields[name]))
			}
			typedBytes, healingBytes := 0, 0
			for _, bucket := range match.Combat.Buckets {
				typed, _ := json.Marshal(bucket.ByType)
				healing, _ := json.Marshal(bucket.Healing)
				typedBytes += len(typed)
				healingBytes += len(healing)
			}
			t.Logf("nested buckets byType: %d bytes; healing: %d bytes (array values)", typedBytes, healingBytes)
			// Covers kills/buybacks, aggregate damage/healing, per-minute counters,
			// lane creep deaths (65455 B), hero hits (16423 B), creep kills (30224 B),
			// and 2 s buckets (67247 B, including typed damage 30228 B/healing 17450 B).
			// Economy and modifier collectors are not registered here: economy is absent,
			// debuffs/buffs are []. All Pick: 280500 B; 350 KB leaves ~25% headroom.
			if len(sections) > 350_000 {
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

// checkHitsAndCreepKills checks the hero-vs-hero hits (the same events as the per-minute damage to heroes, per
// attacker, between enemies, sorted) and the creep last hits (time order, known kinds, positions on the map).
func checkHitsAndCreepKills(t *testing.T, match matchdata.Match) {
	t.Helper()
	team := func(player int) bool { return player < 5 } // the test replays have Radiant in slots 0-4
	var dealt [playerCount]int
	for i, hit := range match.Combat.HeroHits {
		if hit.T%matchdata.BucketSeconds != 0 || hit.Amount <= 0 || team(hit.Attacker) == team(hit.Target) ||
			i > 0 && (hit.T < match.Combat.HeroHits[i-1].T) {
			t.Errorf("bad hero hit %+v", hit)
			break
		}
		dealt[hit.Attacker] += hit.Amount
	}
	for i, p := range match.Combat.PerMinute {
		total := 0
		for _, v := range p.DamageToHeroes {
			total += v
		}
		if dealt[i] != total {
			t.Errorf("player %d: hero hits %d, per-minute damage to heroes %d", i, dealt[i], total)
		}
	}
	kills := match.Combat.CreepKills
	if len(kills) < 500 {
		t.Errorf("only %d creep kills", len(kills))
	}
	kinds := map[matchdata.CreepKind]int{}
	for i, kill := range kills {
		kinds[kill.Kind]++
		if i > 0 && kill.T < kills[i-1].T || kill.Player < 0 || kill.Player >= playerCount ||
			kill.X != nil && (*kill.X < -9000 || *kill.X > 9000) {
			t.Errorf("bad creep kill %+v", kill)
			break
		}
	}
	if kinds[matchdata.CreepLane] == 0 || kinds[matchdata.CreepNeutral] == 0 || kinds[matchdata.CreepDeny] == 0 {
		t.Errorf("creep kinds %v: want lane, neutral and deny", kinds)
	}
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
		// The 2 s buckets are the same events: their sums match the minutes, in time order, on bucket boundaries.
		buckets := match.Combat.Buckets[i]
		bucketSum := func(l matchdata.List[matchdata.Bucket]) (total int) {
			for b, bucket := range l {
				if bucket.T%matchdata.BucketSeconds != 0 || b > 0 && bucket.T <= l[b-1].T || bucket.Amount == 0 {
					t.Errorf("player %d: bad bucket %v", i, bucket)
				}
				total += bucket.Amount
			}
			return total
		}
		if buckets.Player != i || bucketSum(buckets.ToHeroes) != sum(p.DamageToHeroes) ||
			bucketSum(buckets.FromHeroes) != sum(p.DamageFromHeroes) {
			t.Errorf("player %d: buckets do not add up to the minutes", i)
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

// Count by time AND player so a duplicate cannot conceal a missing last hit or deny.
func TestLaneCreepDeathsRealReplays(t *testing.T) {
	for _, replay := range []realReplay{allPickReplay, turboReplay} {
		t.Run(replay.path, func(t *testing.T) {
			waves := &wavesCollector{}
			m, err := analyzeMatch(context.Background(), replay.open(t), []Collector{&matchCollector{}, &combatCollector{waves: waves}, waves})
			if err != nil {
				t.Fatal(err)
			}
			type key struct {
				t      matchdata.Seconds
				player int
			}
			observed := map[key]int{}
			positioned := 0
			for i, d := range m.Combat.LaneCreepDeaths {
				if i > 0 && d.T < m.Combat.LaneCreepDeaths[i-1].T {
					t.Fatal("deaths not sorted")
				}
				if d.CreepTeam != matchdata.Radiant && d.CreepTeam != matchdata.Dire {
					t.Errorf("unknown creep team %+v", d)
				}
				if d.KillerKind == "hero" {
					if d.KillerPlayer == nil || *d.KillerPlayer < 0 || *d.KillerPlayer >= playerCount {
						t.Fatalf("bad hero owner %+v", d)
					}
					observed[key{d.T, *d.KillerPlayer}]++
				} else if d.KillerPlayer != nil {
					t.Errorf("nonhero owner %+v", d)
				}
				switch d.KillerKind {
				case "hero", "creep", "tower", "neutral", "other":
				default:
					t.Errorf("unknown killer kind %q", d.KillerKind)
				}
				if (d.X == nil) != (d.Y == nil) {
					t.Fatal("partial position")
				}
				if d.X != nil {
					positioned++
					lane := waveLane(float64(*d.X), float64(*d.Y))
					if lane == "" && d.Lane != nil || lane != "" && (d.Lane == nil || *d.Lane != lane) {
						t.Errorf("wrong lane %+v", d)
					}
				}
			}
			if positioned == 0 {
				t.Error("no linked sampled positions")
			}
			for _, kill := range m.Combat.CreepKills {
				if kill.Kind != matchdata.CreepLane && kill.Kind != matchdata.CreepDeny {
					continue
				}
				k := key{kill.T, kill.Player}
				if observed[k] == 0 {
					t.Errorf("missing hero lane death for %+v", kill)
				} else {
					observed[k]--
				}
			}
		})
	}
}

func TestLaneCreepKillerKind(t *testing.T) {
	for _, test := range []struct {
		unit combatUnit
		want string
	}{
		{combatUnit{0, matchdata.KindHero}, "hero"}, {combatUnit{1, matchdata.KindIllusion}, "hero"}, {combatUnit{2, matchdata.KindSummon}, "hero"},
		{combatUnit{-1, matchdata.KindLaneCreeps}, "creep"}, {combatUnit{-1, matchdata.KindBuildings}, "tower"}, {combatUnit{-1, matchdata.KindNeutrals}, "neutral"}, {combatUnit{-1, matchdata.KindOtherUnits}, "other"},
	} {
		if got := laneCreepKillerKind(test.unit, "npc_dota_goodguys_tower1_mid"); got != test.want {
			t.Errorf("%+v: %s want %s", test.unit, got, test.want)
		}
	}
}

func TestLaneCreepDeathLinkAmbiguity(t *testing.T) {
	for _, count := range []int{0, 1, 2} {
		waves := &wavesCollector{}
		for range count {
			waves.deaths = append(waves.deaths, trackedCreepDeath{at: ServerTime{Seconds: 10}, name: "creep", team: matchdata.Radiant, position: creepPosition{x: 100, y: -100, known: true}})
		}
		c := combatCollector{session: &Session{Clock: &Clock{}}, waves: waves, laneDeaths: []laneCreepDeathEvent{{at: ServerTime{Seconds: 10}, name: "creep", row: matchdata.LaneCreepDeath{CreepTeam: matchdata.Radiant}}}}
		rows := c.laneCreepDeathList()
		if (rows[0].X != nil) != (count == 1) {
			t.Errorf("%d candidates: %+v", count, rows[0])
		}
		// Two combat rows must not claim the same entity death, even if there is only one candidate.
		c.laneDeaths = append(c.laneDeaths, c.laneDeaths[0])
		rows = c.laneCreepDeathList()
		if rows[0].X != nil || rows[1].X != nil {
			t.Error("reused ambiguous entity death")
		}
	}
}
