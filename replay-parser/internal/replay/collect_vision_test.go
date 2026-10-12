package replay

import (
	"context"
	"encoding/json"
	"fmt"
	"math"
	"strings"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// visionCounters reads, when the match ends, the game's own counters that the vision collector must agree with:
// per player (CDOTA_DataRadiant/Dire) and per team (CDOTATeam).
type visionCounters struct {
	observers, sentries, wardsDestroyed, smokes, roshans [playerCount]int
	towerKills, barracksKills                            map[matchdata.Team]int // buildings the team destroyed
}

func (v *visionCounters) Register(s *Session) {
	s.OnEnd(func(int) {
		for i := range playerCount {
			data, prefix := s.Players.TeamData(i)
			read := func(name string) int { n, _ := number(data, prefix+name); return int(n) }
			v.observers[i] = read("m_iObserverWardsPlaced")
			v.sentries[i] = read("m_iSentryWardsPlaced")
			v.wardsDestroyed[i] = read("m_iWardsDestroyed")
			v.smokes[i] = read("m_iSmokesUsed")
			v.roshans[i] = read("m_iRoshanKills")
		}
		v.towerKills, v.barracksKills = map[matchdata.Team]int{}, map[matchdata.Team]int{}
		for _, team := range s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == "CDOTATeam" }) {
			teamNumber, _ := number(team, "m_iTeamNum")
			towers, _ := number(team, "m_iTowerKills")
			barracks, _ := number(team, "m_iBarracksKills")
			if name := teamOf(int(teamNumber)); name != "" {
				v.towerKills[name], v.barracksKills[name] = int(towers), int(barracks)
			}
		}
	})
}

func (v *visionCounters) Fill(*matchdata.Match) error { return nil }

// analyzeVision reads a real replay with the match and vision collectors, plus the game's counters.
func analyzeVision(t *testing.T, replay realReplay) (matchdata.Match, *visionCounters) {
	t.Helper()
	counters := &visionCounters{}
	match, err := analyzeMatch(context.Background(), replay.open(t), []Collector{&matchCollector{}, &visionCollector{}, counters})
	if err != nil {
		t.Fatal(err)
	}
	if counters.towerKills == nil {
		t.Fatal("OnEnd did not run")
	}
	return match, counters
}

func TestVisionCollectorRealReplaysMatchTheGameCounters(t *testing.T) {
	for _, replay := range []realReplay{allPickReplay, turboReplay} {
		t.Run(replay.env, func(t *testing.T) {
			match, counters := analyzeVision(t, replay)
			vision, objectives := match.Vision, match.Objectives

			var observers, sentries, destroyed, smokes, roshans [playerCount]int
			for _, w := range vision.Wards {
				if w.Player == nil {
					t.Errorf("ward without owner: %+v", w)
					continue
				}
				if w.Type == matchdata.Observer {
					observers[*w.Player]++
				} else {
					sentries[*w.Player]++
				}
				if w.DestroyedBy != nil && *w.DestroyedByTeam != w.Team { // the game does not count denies
					destroyed[*w.DestroyedBy]++
				}
				if w.DestroyedBy != nil && !w.Destroyed || w.Destroyed && w.RemovedAt == nil {
					t.Errorf("inconsistent ward: %+v", w)
				}
				if w.RemovedAt != nil && *w.RemovedAt < w.PlacedAt {
					t.Errorf("ward removed before it was placed: %+v", w)
				}
			}
			for _, s := range vision.Smokes {
				smokes[s.Player]++
				if s.X == nil || s.Y == nil {
					t.Errorf("smoke without position: %+v", s)
				}
			}
			for _, r := range objectives.Roshan {
				if r.Killer != nil {
					roshans[*r.Killer]++
				}
			}
			check := func(what string, got, want [playerCount]int) {
				if got != want {
					t.Errorf("%s per player %v, game counters %v", what, got, want)
				}
			}
			check("observers placed", observers, counters.observers)
			check("sentries placed", sentries, counters.sentries)
			check("wards destroyed", destroyed, counters.wardsDestroyed)
			check("smokes", smokes, counters.smokes)
			check("Roshan last hits", roshans, counters.roshans)

			towers, barracks := map[matchdata.Team]int{}, map[matchdata.Team]int{}
			for _, b := range objectives.Buildings {
				enemy := matchdata.Radiant
				if b.Team == matchdata.Radiant {
					enemy = matchdata.Dire
				}
				switch {
				case b.Denied:
				case strings.Contains(b.Building, "_tower"):
					towers[enemy]++
				case strings.Contains(b.Building, "_rax_"):
					barracks[enemy]++
				}
			}
			for _, team := range []matchdata.Team{matchdata.Radiant, matchdata.Dire} {
				if towers[team] != counters.towerKills[team] || barracks[team] != counters.barracksKills[team] {
					t.Errorf("%s destroyed %d towers and %d barracks, CDOTATeam says %d and %d", team, towers[team],
						barracks[team], counters.towerKills[team], counters.barracksKills[team])
				}
			}
			// The last building is the Ancient, on the second the match ended.
			last := objectives.Buildings[len(objectives.Buildings)-1]
			if !strings.HasSuffix(last.Building, "_fort") || math.Abs(float64(last.T-match.Match.DurationSeconds)) > 1 {
				t.Errorf("last building %+v, match ended at %.1f", last, match.Match.DurationSeconds)
			}
			logSectionSizes(t, match)
		})
	}
}

func TestVisionCollectorAllPick(t *testing.T) {
	match, _ := analyzeVision(t, allPickReplay)
	vision, objectives := match.Vision, match.Objectives
	// Counted from the chat events of the replay (cmd/inspect -catalog).
	if len(vision.Scans) != 6 || len(vision.Glyphs) != 6 || len(objectives.Outposts) != 1 || len(objectives.Roshan) != 0 {
		t.Errorf("%d scans, %d glyphs, %d outposts, %d Roshans; want 6, 6, 1, 0", len(vision.Scans), len(vision.Glyphs),
			len(objectives.Outposts), len(objectives.Roshan))
	}
	// 11:37 (699.7 s of server time - 262.47): Nyx Assassin (3) killed Spectre's (9) courier; at 26:27 a Radiant
	// creep killed Slardar's (8).
	want := []matchdata.CourierKill{{T: 437.2, Victim: 9, Killer: visionInt(3)}, {T: 1587.1, Victim: 8}}
	if len(objectives.Couriers) != len(want) {
		t.Fatalf("couriers %+v", objectives.Couriers)
	}
	for i, got := range objectives.Couriers {
		if math.Abs(float64(got.T-want[i].T)) > 0.2 || got.Victim != want[i].Victim || fmt.Sprint(visionDeref(got.Killer)) != fmt.Sprint(visionDeref(want[i].Killer)) {
			t.Errorf("courier %d: %+v, want %+v", i, got, want[i])
		}
	}
	// Storm Spirit's (2) sentry killed by himself at 7:56 (738.27 - 262.47): a deny of his team's (Lich's) ward.
	found := false
	for _, w := range vision.Wards {
		if w.RemovedAt != nil && math.Abs(float64(*w.RemovedAt)-475.8) < 0.2 {
			found = w.Type == matchdata.Sentry && visionDeref(w.Player) == 4 && visionDeref(w.DestroyedBy) == 2 &&
				w.DestroyedByTeam != nil && *w.DestroyedByTeam == matchdata.Radiant
		}
	}
	if !found {
		t.Error("the sentry denied by Storm Spirit at 7:56 is missing or wrong")
	}
}

// openDotaWard is a ward from OpenDota's obs_log/sen_log of match 9041133074 (player index, type, placed at, map
// position from its x/y in 128-unit cells, removed at from obs_left_log/sen_left_log; noRemoval = still there).
type openDotaWard struct {
	player    int
	kind      matchdata.WardType
	placedAt  float64
	x, y      int
	removedAt float64
}

const noRemoval = math.MaxFloat64

var openDotaTurboWards = []openDotaWard{
	{1, matchdata.Observer, -30, -307, -973, 330}, {1, matchdata.Observer, 351, -4749, 4672, 711},
	{1, matchdata.Observer, 427, 13, -154, 697}, {1, matchdata.Observer, 459, -4326, -1011, 819},
	{1, matchdata.Observer, 995, -1306, -4314, 1111}, {1, matchdata.Observer, 1019, -4352, -1037, 1350},
	{1, matchdata.Observer, 1060, -1574, 3533, 1420}, {1, matchdata.Observer, 1290, 998, 3558, noRemoval},
	{1, matchdata.Sentry, -27, -13, -192, 393}, {1, matchdata.Sentry, 52, 4595, -4518, 472},
	{1, matchdata.Sentry, 334, -5926, 3430, 387}, {1, matchdata.Sentry, 347, -4954, 4544, 767},
	{1, matchdata.Sentry, 371, -5734, 4966, 791}, {1, matchdata.Sentry, 425, -243, -256, 471},
	{1, matchdata.Sentry, 458, -4339, -1037, 878}, {1, matchdata.Sentry, 480, -1293, -4352, 900},
	{1, matchdata.Sentry, 598, -922, -781, 699}, {1, matchdata.Sentry, 861, -3494, 461, 1281},
	{1, matchdata.Sentry, 883, -320, -307, 1068}, {1, matchdata.Sentry, 997, -1293, -4352, 1112},
	{1, matchdata.Sentry, 1018, -4339, -1037, 1348}, {1, matchdata.Sentry, 1061, -1216, 2957, 1481},
	{1, matchdata.Sentry, 1123, -4467, -5210, 1191}, {1, matchdata.Sentry, 1288, 1024, 3558, noRemoval},
	{2, matchdata.Sentry, 626, -2150, -1702, 1046}, {3, matchdata.Sentry, 1138, -5581, -6298, 1194},
	{4, matchdata.Observer, 332, -3354, 614, 692}, {4, matchdata.Observer, 635, 1062, -2701, 964},
	{4, matchdata.Sentry, 329, -4058, 397, 749}, {4, matchdata.Sentry, 628, 666, -3405, 645},
	{7, matchdata.Observer, -17, -5414, 3251, 338}, {7, matchdata.Observer, 342, -1293, -4339, 483},
	{7, matchdata.Observer, 744, 1216, -4506, 1104}, {7, matchdata.Observer, 843, -1613, -1498, 859},
	{7, matchdata.Observer, 852, -3059, -77, 863}, {7, matchdata.Observer, 1069, 230, -4045, 1429},
	{7, matchdata.Observer, 1135, -4672, -6912, noRemoval}, {7, matchdata.Observer, 1353, -4147, -4186, noRemoval},
	{7, matchdata.Sentry, -14, -4531, 4339, 350}, {7, matchdata.Sentry, 455, -1024, -627, 600},
	{7, matchdata.Sentry, 490, 358, -3789, 633}, {7, matchdata.Sentry, 642, 410, -3814, 1062},
	{7, matchdata.Sentry, 958, 397, -2304, 1378}, {7, matchdata.Sentry, 1091, 1766, -2714, 1511},
	{7, matchdata.Sentry, 1122, -2483, -6246, 1542},
	{9, matchdata.Observer, 500, 243, -4096, 631},
	{9, matchdata.Sentry, 43, 3674, -4941, 59}, {9, matchdata.Sentry, 385, -6016, 3610, 805},
	{9, matchdata.Sentry, 526, -1254, -4365, 947}, {9, matchdata.Sentry, 695, -538, -371, 885},
	{9, matchdata.Sentry, 773, 3968, -5312, 1193}, {9, matchdata.Sentry, 918, 666, 589, 1338},
	{9, matchdata.Sentry, 1063, -576, -282, 1483}, {9, matchdata.Sentry, 1106, -1254, -4352, 1526},
	{9, matchdata.Sentry, 1187, -4685, -6182, noRemoval}, {9, matchdata.Sentry, 1346, -4352, -1050, noRemoval},
	{9, matchdata.Sentry, 1405, -6477, -5952, noRemoval},
}

// openDotaTurboRunes is OpenDota's runes_log of match 9041133074: player index, time, rune type.
var openDotaTurboRunes = [][3]int{
	{0, 427, 8}, {0, 1280, 8}, {1, 35, 5}, {2, 871, 8}, {3, 136, 7}, {3, 749, 5}, {5, 3, 5}, {5, 139, 7}, {5, 877, 8},
	{5, 1332, 6}, {6, 33, 5}, {6, 609, 5}, {6, 845, 3}, {6, 1010, 5}, {6, 1020, 2}, {6, 1241, 5}, {6, 1308, 2},
	{7, 310, 7}, {7, 323, 5}, {7, 969, 5}, {8, 1, 5}, {8, 734, 5}, {8, 1245, 5}, {9, 339, 5}, {9, 488, 5},
	{9, 589, 8}, {9, 668, 4}, {9, 1272, 8},
}

// openDotaTurboBuildings is OpenDota's building_kill objectives of match 9041133074 (it leaves the fillers out):
// time, building, killer (player index, -1 = not a player).
var openDotaTurboBuildings = []struct {
	t        float64
	building string
	killer   int
}{
	{303, "npc_dota_goodguys_tower1_top", -1}, {413, "npc_dota_goodguys_tower1_bot", 8},
	{453, "npc_dota_goodguys_tower1_mid", 6}, {527, "npc_dota_badguys_tower1_top", -1},
	{817, "npc_dota_goodguys_tower2_bot", 6}, {825, "npc_dota_badguys_tower1_mid", -1},
	{974, "npc_dota_goodguys_tower2_mid", 6}, {1170, "npc_dota_goodguys_tower3_bot", -1},
	{1175, "npc_dota_goodguys_melee_rax_bot", 8}, {1178, "npc_dota_goodguys_range_rax_bot", 9},
	{1229, "npc_dota_goodguys_tower3_mid", 6}, {1375, "npc_dota_goodguys_range_rax_mid", 9},
	{1383, "npc_dota_goodguys_melee_rax_mid", 8}, {1389, "npc_dota_goodguys_tower4", 8},
	{1396, "npc_dota_goodguys_tower4", 9}, {1421, "npc_dota_goodguys_fort", 8},
}

func TestVisionCollectorTurboMatchesOpenDota(t *testing.T) {
	result, _ := analyzeVision(t, turboReplay)
	vision, objectives := result.Vision, result.Objectives

	// Wards: every OpenDota ward has one of ours (same player and type) placed within 1 s, within 64 units (half a
	// cell; OpenDota rounds to 1/10 cell), removed within 1.5 s. OpenDota also has removals after the Ancient fell
	// (the replay goes on); for us those wards were still there when the match ended (removedAt null).
	if len(vision.Wards) != len(openDotaTurboWards) {
		t.Errorf("%d wards, OpenDota has %d", len(vision.Wards), len(openDotaTurboWards))
	}
	used := map[int]bool{}
	for _, want := range openDotaTurboWards {
		match := -1
		for i, got := range vision.Wards {
			if !used[i] && got.Type == want.kind && visionDeref(got.Player) == want.player &&
				math.Abs(float64(got.PlacedAt)-want.placedAt) <= 1 {
				match = i
				break
			}
		}
		if match < 0 {
			t.Errorf("OpenDota ward %+v not found", want)
			continue
		}
		used[match] = true
		got := vision.Wards[match]
		if math.Abs(float64(got.X-want.x)) > 64 || math.Abs(float64(got.Y-want.y)) > 64 {
			t.Errorf("ward %+v at (%d, %d), OpenDota (%d, %d)", want, got.X, got.Y, want.x, want.y)
		}
		if want.removedAt > float64(result.Match.DurationSeconds) {
			want.removedAt = noRemoval
		}
		switch {
		case want.removedAt == noRemoval && got.RemovedAt != nil:
			t.Errorf("ward %+v removed at %.1f, OpenDota: still there", want, *got.RemovedAt)
		case want.removedAt != noRemoval && (got.RemovedAt == nil || math.Abs(float64(*got.RemovedAt)-want.removedAt) > 1.5):
			t.Errorf("ward %+v removed at %v, OpenDota %.0f", want, got.RemovedAt, want.removedAt)
		}
	}

	// Runes: the same pickups (player, type) within 1 s.
	if len(objectives.Runes) != len(openDotaTurboRunes) {
		t.Errorf("%d runes, OpenDota has %d", len(objectives.Runes), len(openDotaTurboRunes))
	}
	for _, want := range openDotaTurboRunes {
		found := false
		for _, got := range objectives.Runes {
			if got.Player == want[0] && got.RuneType == want[2] && math.Abs(float64(got.T)-float64(want[1])) <= 1 {
				found = true
			}
		}
		if !found {
			t.Errorf("OpenDota rune %v not found", want)
		}
	}

	// Buildings: OpenDota's list, in order, with the same killer (a Warlock golem counts for Warlock), plus 4
	// fillers.
	var buildings []matchdata.BuildingKill
	for _, b := range objectives.Buildings {
		if !strings.Contains(b.Building, "_fillers") {
			buildings = append(buildings, b)
		}
	}
	if len(buildings) != len(openDotaTurboBuildings) || len(objectives.Buildings) != len(openDotaTurboBuildings)+4 {
		t.Fatalf("%d buildings (%d without fillers), OpenDota has %d", len(objectives.Buildings), len(buildings),
			len(openDotaTurboBuildings))
	}
	for i, want := range openDotaTurboBuildings {
		got := buildings[i]
		killer := -1
		if got.Killer != nil {
			killer = *got.Killer
		}
		if got.Building != want.building || killer != want.killer || math.Abs(float64(got.T)-want.t) > 1 || got.Denied {
			t.Errorf("building %d: %+v (killer %d), OpenDota %+v", i, got, killer, want)
		}
	}

	// Roshan: one, killed by Dire's Skywrath Mage (5) at 18:13 (OpenDota: CHAT_MESSAGE_ROSHAN_KILL at 1093, team
	// 3), Aegis to Centaur (6) (CHAT_MESSAGE_AEGIS at 1094).
	if len(objectives.Roshan) != 1 {
		t.Fatalf("Roshan kills %+v", objectives.Roshan)
	}
	if r := objectives.Roshan[0]; math.Abs(float64(r.T)-1092.7) > 0.2 || r.Team != matchdata.Dire ||
		visionDeref(r.Killer) != 5 || visionDeref(r.AegisPlayer) != 6 {
		t.Errorf("Roshan %+v (killer %d, Aegis %d)", r, visionDeref(r.Killer), visionDeref(r.AegisPlayer))
	}
	if len(objectives.Tormentors) != 0 || len(objectives.Couriers) != 0 {
		t.Errorf("tormentors %+v, couriers %+v; want none", objectives.Tormentors, objectives.Couriers)
	}
	// Chat events of the replay: 5 glyphs, 4 scans, 7 smokes, 1 outpost (Dire, 22:15.9 of replay = 1335.9).
	if len(vision.Glyphs) != 5 || len(vision.Scans) != 4 || len(vision.Smokes) != 7 || len(objectives.Outposts) != 1 {
		t.Errorf("%d glyphs, %d scans, %d smokes, %d outposts; want 5, 4, 7, 1", len(vision.Glyphs), len(vision.Scans),
			len(vision.Smokes), len(objectives.Outposts))
	}
	if o := objectives.Outposts[0]; o.Team != matchdata.Dire || math.Abs(float64(o.T)-1335.9) > 0.2 || o.Player != nil {
		t.Errorf("outpost %+v", o)
	}
}

func TestAssignAegis(t *testing.T) {
	clock := &Clock{game: gameClock{gameStart: 100}}
	kills := []matchdata.RoshanKill{{T: 600}, {T: 1500}}
	c := &visionCollector{aegis: []runeEvent{
		{at: ServerTime{Seconds: 701}, player: 3},  // first kill's Aegis
		{at: ServerTime{Seconds: 1601}, player: 7}, // second kill's Aegis
		{at: ServerTime{Seconds: 1650}, player: 8}, // a later message about the same Aegis: ignored
		{at: ServerTime{Seconds: 50}, player: 1},   // before any kill: ignored
	}}
	c.assignAegis(kills, clock)
	if visionDeref(kills[0].AegisPlayer) != 3 || visionDeref(kills[1].AegisPlayer) != 7 {
		t.Errorf("Aegis %v %v, want 3 and 7", kills[0].AegisPlayer, kills[1].AegisPlayer)
	}
}

// logSectionSizes logs the JSON size of the vision and objectives sections.
func logSectionSizes(t *testing.T, match matchdata.Match) {
	t.Helper()
	vision, _ := json.Marshal(match.Vision)
	objectives, _ := json.Marshal(match.Objectives)
	t.Logf("vision: %d wards, %d smokes, %d scans, %d glyphs; %d bytes", len(match.Vision.Wards), len(match.Vision.Smokes),
		len(match.Vision.Scans), len(match.Vision.Glyphs), len(vision))
	t.Logf("objectives: %d buildings, %d Roshan, %d tormentors, %d couriers, %d runes, %d outposts; %d bytes",
		len(match.Objectives.Buildings), len(match.Objectives.Roshan), len(match.Objectives.Tormentors),
		len(match.Objectives.Couriers), len(match.Objectives.Runes), len(match.Objectives.Outposts), len(objectives))
}

// visionDeref is the value of an optional player, or -1.
func visionDeref(p *int) int {
	if p == nil {
		return -1
	}
	return *p
}
