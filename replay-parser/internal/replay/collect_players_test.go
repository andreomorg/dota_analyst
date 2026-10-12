package replay

import (
	"context"
	"encoding/json"
	"fmt"
	"math"
	"sort"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// analyzePlayers reads a real replay with the match and players collectors only.
func analyzePlayers(t *testing.T, replay realReplay) matchdata.Match {
	t.Helper()
	match, err := analyzeMatch(context.Background(), replay.open(t), []Collector{&matchCollector{}, &playersCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	checkPlayersSections(t, match)
	return match
}

// checkPlayersSections checks what must hold in any replay: 10 players, columns of the same length ending at the
// final numbers, totals that never go down, gold by source adding up, positions every 2 s while alive.
func checkPlayersSections(t *testing.T, match matchdata.Match) {
	t.Helper()
	if len(match.Players) != 10 || len(match.Timelines.PerMinute) != 10 || len(match.Timelines.Positions) != 10 {
		t.Fatalf("%d players, %d per-minute, %d positions; want 10 each", len(match.Players),
			len(match.Timelines.PerMinute), len(match.Timelines.Positions))
	}
	if match.Timelines.PositionIntervalSeconds != matchdata.PositionIntervalSeconds {
		t.Errorf("positionIntervalSeconds %d", match.Timelines.PositionIntervalSeconds)
	}
	duration := float64(match.Match.DurationSeconds)
	wantPoints := int(duration)/60 + 2 // minutes 0..last whole minute, plus the end of the match
	for i, player := range match.Players {
		if player.Player != i || player.SteamID == "" || player.Name == "" || player.HeroID <= 0 ||
			player.HeroName == "" || player.Team == "" || player.TeamSlot < 0 || player.TeamSlot > 4 {
			t.Errorf("player %d identity: %+v", i, player)
		}
		if player.PickOrder == nil || *player.PickOrder < 1 || *player.PickOrder > 10 {
			t.Errorf("player %d pick order %v", i, player.PickOrder)
		}
		f := player.Final
		g := f.GoldBySource
		sources := g.Creeps + g.Heroes + g.Buildings + g.Neutrals + g.Roshan + g.BountyRunes + g.Passive + g.Wards +
			g.Couriers + g.Summons + g.Denies + g.Abilities + g.Comeback + g.Other
		if sources != f.GoldEarned {
			t.Errorf("player %d: gold by source (shared left out) adds up to %d, earned %d", i, sources, f.GoldEarned)
		}

		minutes := match.Timelines.PerMinute[i]
		columns := map[string]matchdata.List[int]{"gold": minutes.Gold, "xp": minutes.XP, "netWorth": minutes.NetWorth,
			"lastHits": minutes.LastHits, "denies": minutes.Denies, "level": minutes.Level, "kills": minutes.Kills,
			"deaths": minutes.Deaths, "assists": minutes.Assists, "heroDamage": minutes.HeroDamage}
		finals := map[string]int{"gold": f.GoldEarned, "xp": f.XP, "netWorth": f.NetWorth, "lastHits": f.LastHits,
			"denies": f.Denies, "level": f.Level, "kills": f.Kills, "deaths": f.Deaths, "assists": f.Assists,
			"heroDamage": f.HeroDamage}
		if minutes.Player != i {
			t.Errorf("per-minute entry %d is player %d", i, minutes.Player)
		}
		for name, column := range columns {
			if len(column) != wantPoints {
				t.Errorf("player %d %s: %d points, want %d", i, name, len(column), wantPoints)
				continue
			}
			if column[len(column)-1] != finals[name] {
				t.Errorf("player %d %s: last point %d, final %d", i, name, column[len(column)-1], finals[name])
			}
			if name == "netWorth" {
				continue // net worth goes down (deaths, consumables)
			}
			for m := 1; m < len(column); m++ {
				if column[m] < column[m-1] {
					t.Errorf("player %d %s goes down at minute %d: %v", i, name, m, column)
					break
				}
			}
		}
		if minutes.Gold[0] != 0 || minutes.XP[0] != 0 || minutes.Level[0] != 1 {
			t.Errorf("player %d at 0:00: gold %d xp %d level %d", i, minutes.Gold[0], minutes.XP[0], minutes.Level[0])
		}

		positions := match.Timelines.Positions[i]
		samples := positions.Samples
		// Dead time leaves gaps; nobody is dead for more than 40% of a match.
		if positions.Player != i || len(samples) > int(duration)/2+1 || len(samples) < int(0.6*duration/2) {
			t.Errorf("player %d: %d positions in %.0f s", i, len(samples), duration)
		}
		for s, sample := range samples {
			if sample.T%matchdata.PositionIntervalSeconds != 0 || s > 0 && sample.T <= samples[s-1].T ||
				sample.T < 0 || float64(sample.T) > duration || math.Abs(float64(sample.X)) > 9000 ||
				math.Abs(float64(sample.Y)) > 9000 {
				t.Errorf("player %d: bad sample %v", i, sample)
				break
			}
		}
	}
	players, _ := json.Marshal(match.Players)
	perMinute, _ := json.Marshal(match.Timelines.PerMinute)
	positions, _ := json.Marshal(match.Timelines.Positions)
	t.Logf("JSON: players %d B, timelines.perMinute %d B, timelines.positions %d B", len(players), len(perMinute),
		len(positions))
}

func TestPlayersCollectorAllPick(t *testing.T) {
	match := analyzePlayers(t, allPickReplay)

	// At 10:00, the numbers of the /v1/laning test (the cut is on the same tick: the first one of 10:00).
	tenMinutes := []struct {
		player, lastHits, denies, netWorth, xp, deaths int
	}{
		{0, 36, 20, 3999, 5891, 1}, // Riki
		{4, 12, 9, 2047, 2609, 2},  // Lich (the owner's account)
	}
	for _, want := range tenMinutes {
		m := match.Timelines.PerMinute[want.player]
		got := []int{m.LastHits[10], m.Denies[10], m.NetWorth[10], m.XP[10], m.Deaths[10]}
		if fmt.Sprint(got) != fmt.Sprint([]int{want.lastHits, want.denies, want.netWorth, want.xp, want.deaths}) {
			t.Errorf("player %d at 10:00: lh/dn/nw/xp/deaths %v, want %+v", want.player, got, want)
		}
	}

	riki, lich := match.Players[0], match.Players[4]
	if riki.HeroID != 32 || lich.HeroID != 31 || lich.SteamID != "76561198059783443" {
		t.Fatalf("players 0 and 4: %+v / %+v", riki, lich)
	}
	f := riki.Final
	if f.Kills != 32 || f.Deaths != 3 || f.Assists != 9 || f.Level != 26 || f.LastHits != 193 || f.Denies != 22 ||
		f.NetWorth != 22217 || f.GoldEarned != 23414 || f.XP != 39031 || f.HeroDamage != 39465 ||
		f.TowerDamage != 6274 || !f.FirstBlood {
		t.Errorf("Riki's final numbers: %+v", f)
	}
	// Ranked role queue: ranks and roles are in the replay (Riki queued safe lane, Lich hard support).
	if riki.RankTier == nil || *riki.RankTier != 74 || riki.Role == nil || *riki.Role != 1 || riki.PickOrder == nil ||
		*riki.PickOrder != 5 {
		t.Errorf("Riki: rank %v role %v pick %v", riki.RankTier, riki.Role, riki.PickOrder)
	}
	if lich.RankTier == nil || *lich.RankTier != 43 || lich.Role == nil || *lich.Role != 5 {
		t.Errorf("Lich: rank %v role %v", lich.RankTier, lich.Role)
	}
	roles := map[matchdata.Team][]int{}
	for _, player := range match.Players {
		if player.Role != nil {
			roles[player.Team] = append(roles[player.Team], *player.Role)
		}
	}
	for team, list := range roles {
		sort.Ints(list)
		if fmt.Sprint(list) != "[1 2 3 4 5]" {
			t.Errorf("%s roles %v, want one of each", team, list)
		}
	}
	if len(roles) != 2 {
		t.Errorf("roles %v", roles)
	}
}

// openDotaTurbo is OpenDota's parse of match 9041133074 (players in index order): final numbers and the per-minute
// arrays gold_t, xp_t, lh_t and dn_t (minutes 0..23).
var openDotaTurbo = []struct {
	heroID, kills, deaths, assists, level, lastHits, denies, netWorth, heroDamage, towerDamage, healing int
	goldT, xpT, lhT, dnT                                                                                []int
}{
	{heroID: 114, kills: 3, deaths: 9, assists: 4, level: 22, lastHits: 92, denies: 2, netWorth: 15695, heroDamage: 15027, towerDamage: 0, healing: 0,
		goldT: []int{0, 526, 706, 886, 1228, 1856, 2294, 3900, 4558, 5624, 6720, 7378, 7892, 8916, 9332, 10230, 12910, 13428, 13916, 14104, 14294, 14484, 15086, 15770},
		xpT:   []int{0, 268, 325, 382, 953, 1754, 2391, 3682, 4648, 5910, 7938, 8605, 9772, 10386, 10602, 13843, 22811, 23433, 24186, 24264, 24648, 24676, 27560, 28488},
		lhT:   []int{0, 3, 3, 3, 5, 12, 20, 25, 32, 37, 46, 51, 54, 63, 64, 76, 76, 83, 86, 86, 86, 86, 89, 92},
		dnT:   []int{0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2}},
	{heroID: 26, kills: 2, deaths: 9, assists: 12, level: 26, lastHits: 24, denies: 2, netWorth: 13804, heroDamage: 12895, towerDamage: 69, healing: 0,
		goldT: []int{0, 720, 900, 1262, 1442, 1622, 2140, 2534, 3052, 4104, 4646, 5226, 6272, 7022, 7620, 8602, 9146, 9656, 10118, 10664, 12892, 14802, 15802, 15992},
		xpT:   []int{0, 166, 622, 947, 1520, 1768, 2449, 2937, 4588, 6076, 7121, 7988, 9321, 11321, 11868, 13332, 14172, 17567, 19581, 20923, 28291, 34783, 39893, 39893},
		lhT:   []int{0, 1, 1, 3, 3, 3, 5, 5, 5, 6, 9, 11, 18, 18, 18, 19, 19, 22, 22, 24, 24, 24, 24, 24},
		dnT:   []int{0, 0, 0, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2}},
	{heroID: 53, kills: 5, deaths: 8, assists: 11, level: 23, lastHits: 166, denies: 3, netWorth: 20438, heroDamage: 17025, towerDamage: 171, healing: 0,
		goldT: []int{0, 454, 1116, 1814, 2006, 2260, 2860, 4540, 6622, 7564, 8224, 9250, 10798, 12150, 13038, 13840, 15394, 15896, 17652, 18488, 19174, 20960, 22946, 24182},
		xpT:   []int{0, 337, 1072, 1835, 2017, 2131, 2747, 6619, 7724, 8879, 8879, 9834, 10827, 12684, 13536, 15178, 15354, 15991, 20475, 22092, 22092, 25717, 28112, 28486},
		lhT:   []int{0, 0, 0, 5, 7, 8, 12, 17, 30, 35, 42, 44, 65, 72, 80, 89, 91, 93, 119, 128, 135, 139, 153, 166},
		dnT:   []int{0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3}},
	{heroID: 35, kills: 9, deaths: 6, assists: 11, level: 25, lastHits: 151, denies: 2, netWorth: 27862, heroDamage: 38114, towerDamage: 0, healing: 0,
		goldT: []int{0, 810, 1246, 2056, 2596, 3294, 3914, 7382, 7964, 9840, 10792, 11608, 12574, 13968, 15106, 16862, 18438, 19428, 20000, 20938, 22618, 25900, 27810, 28570},
		xpT:   []int{0, 688, 1534, 2797, 3561, 4298, 5133, 8637, 9525, 10886, 12182, 13707, 14930, 15297, 15610, 17576, 18592, 20028, 21393, 22687, 22883, 27333, 33657, 34640},
		lhT:   []int{0, 1, 4, 11, 15, 16, 20, 22, 28, 37, 43, 47, 57, 64, 69, 76, 79, 96, 102, 114, 116, 126, 144, 151},
		dnT:   []int{0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2}},
	{heroID: 31, kills: 3, deaths: 8, assists: 9, level: 24, lastHits: 13, denies: 7, netWorth: 16946, heroDamage: 13091, towerDamage: 0, healing: 0,
		goldT: []int{0, 990, 1354, 1708, 1888, 2588, 2768, 3422, 4200, 4988, 5258, 6034, 6430, 7814, 8508, 9176, 10694, 11212, 11582, 11950, 12320, 13536, 16306, 16700},
		xpT:   []int{0, 166, 627, 1396, 1396, 2010, 2145, 3627, 4834, 6130, 6130, 7463, 8346, 11486, 11573, 13147, 17525, 18987, 20418, 20700, 21547, 26091, 32116, 32685},
		lhT:   []int{0, 0, 0, 1, 1, 2, 2, 2, 3, 3, 3, 5, 6, 7, 9, 9, 9, 10, 10, 10, 10, 11, 11, 12},
		dnT:   []int{0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 7, 7}},
	{heroID: 101, kills: 13, deaths: 4, assists: 14, level: 27, lastHits: 117, denies: 3, netWorth: 28126, heroDamage: 28671, towerDamage: 619, healing: 0,
		goldT: []int{0, 1230, 1826, 2404, 3352, 4186, 5572, 6954, 7454, 7936, 8264, 8810, 9830, 12512, 13740, 14618, 16520, 17730, 19466, 21006, 23442, 24162, 24530, 26330},
		xpT:   []int{0, 569, 1736, 2450, 3647, 5461, 6652, 8475, 8788, 8988, 9252, 9716, 11338, 14930, 16301, 18529, 22915, 24107, 27023, 27760, 32478, 32723, 32837, 41914},
		lhT:   []int{0, 0, 5, 10, 13, 14, 17, 21, 25, 32, 34, 38, 48, 60, 69, 82, 82, 93, 102, 108, 110, 110, 112, 113},
		dnT:   []int{0, 0, 0, 1, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3}},
	{heroID: 96, kills: 6, deaths: 4, assists: 21, level: 28, lastHits: 153, denies: 8, netWorth: 30546, heroDamage: 22441, towerDamage: 3082, healing: 0,
		goldT: []int{0, 698, 2134, 2314, 3330, 4686, 5694, 6484, 7198, 7940, 8970, 10168, 11080, 12922, 13902, 15090, 16662, 18532, 19280, 20216, 22614, 24816, 26056, 29602},
		xpT:   []int{0, 271, 1136, 1250, 1927, 3409, 4430, 5454, 5738, 8026, 10801, 12365, 13487, 17844, 18830, 20592, 28179, 29484, 31946, 33053, 37527, 41541, 43549, 52832},
		lhT:   []int{0, 0, 3, 3, 12, 18, 27, 29, 31, 33, 37, 47, 58, 69, 73, 90, 93, 106, 112, 118, 119, 126, 145, 153},
		dnT:   []int{0, 0, 1, 2, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8}},
	{heroID: 9, kills: 3, deaths: 9, assists: 21, level: 26, lastHits: 60, denies: 0, netWorth: 21482, heroDamage: 13066, towerDamage: 736, healing: 3274,
		goldT: []int{0, 870, 1662, 1910, 2218, 2654, 3372, 3732, 4256, 4788, 5584, 6626, 7336, 7814, 8700, 9030, 9650, 11324, 12636, 13632, 15328, 16228, 17400, 20322},
		xpT:   []int{0, 244, 1029, 1263, 2059, 2225, 2782, 2910, 3900, 4669, 8003, 9237, 10006, 10104, 10784, 11945, 12168, 17669, 23238, 24247, 26106, 26345, 28580, 37863},
		lhT:   []int{0, 0, 2, 3, 3, 3, 5, 5, 7, 10, 16, 24, 28, 29, 34, 34, 35, 42, 47, 53, 53, 53, 60, 60},
		dnT:   []int{0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}},
	{heroID: 99, kills: 10, deaths: 1, assists: 11, level: 27, lastHits: 133, denies: 1, netWorth: 28449, heroDamage: 17031, towerDamage: 7809, healing: 0,
		goldT: []int{0, 1142, 1706, 2146, 3606, 4514, 5662, 6828, 7828, 9676, 11562, 12238, 12818, 14110, 14910, 15864, 19214, 20202, 21152, 21700, 23864, 26754, 27530, 28218},
		xpT:   []int{0, 397, 933, 1659, 2953, 3894, 4909, 6513, 7481, 10157, 13212, 14084, 14698, 18434, 19444, 20912, 30571, 31258, 32109, 33023, 36160, 41198, 42100, 43621},
		lhT:   []int{0, 3, 8, 11, 16, 23, 29, 36, 44, 49, 55, 60, 66, 74, 78, 87, 91, 96, 103, 103, 105, 115, 123, 123},
		dnT:   []int{0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1}},
	{heroID: 37, kills: 6, deaths: 6, assists: 24, level: 26, lastHits: 90, denies: 5, netWorth: 29824, heroDamage: 18636, towerDamage: 11075, healing: 3118,
		goldT: []int{0, 604, 976, 1156, 2184, 3516, 4698, 5244, 6024, 6570, 8252, 9376, 9978, 11178, 12722, 13538, 15830, 17454, 18496, 19678, 22974, 23874, 25074, 27538},
		xpT:   []int{0, 253, 717, 1071, 2365, 3790, 4034, 4323, 4647, 4857, 6460, 8064, 8763, 10532, 11273, 12539, 16871, 18909, 21635, 22744, 27576, 27576, 30518, 36930},
		lhT:   []int{0, 0, 2, 2, 2, 5, 11, 13, 15, 15, 20, 30, 31, 33, 41, 45, 47, 50, 58, 62, 76, 76, 81, 85},
		dnT:   []int{0, 0, 1, 3, 3, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5}},
}

func TestPlayersCollectorTurboAgainstOpenDota(t *testing.T) {
	match := analyzePlayers(t, turboReplay)

	// Final numbers: the same as OpenDota's scoreboard.
	for i, want := range openDotaTurbo {
		player := match.Players[i]
		f := player.Final
		got := []int{player.HeroID, f.Kills, f.Deaths, f.Assists, f.Level, f.LastHits, f.Denies, f.NetWorth,
			f.HeroDamage, f.TowerDamage, f.Healing}
		expected := []int{want.heroID, want.kills, want.deaths, want.assists, want.level, want.lastHits, want.denies,
			want.netWorth, want.heroDamage, want.towerDamage, want.healing}
		if fmt.Sprint(got) != fmt.Sprint(expected) {
			t.Errorf("player %d hero/K/D/A/level/LH/DN/NW/HD/TD/heal:\n got %v\nwant %v", i, got, expected)
		}
		// Turbo has no role queue and the replay hides the ranks (OpenDota's rank comes from the profiles).
		if player.Role != nil || player.RankTier != nil {
			t.Errorf("player %d: role %v rank %v in Turbo", i, player.Role, player.RankTier)
		}
	}

	// Per minute: OpenDota reads each minute a tick before us, so we are never below it. XP, last hits and denies are
	// equal except when something lands on the minute's first tick; gold is ahead by the passive gold and bounties
	// of that tick (+2..+8 in Turbo).
	type column struct {
		name       string
		ours       func(matchdata.PlayerMinutes) matchdata.List[int]
		openDota   func(int) []int
		minEqual   float64 // share of minutes with exactly OpenDota's value
		usualDelta int     // gold: the usual difference is in 0..usualDelta
	}
	columns := []column{
		{"gold", func(m matchdata.PlayerMinutes) matchdata.List[int] { return m.Gold },
			func(i int) []int { return openDotaTurbo[i].goldT }, 0, 8},
		{"xp", func(m matchdata.PlayerMinutes) matchdata.List[int] { return m.XP },
			func(i int) []int { return openDotaTurbo[i].xpT }, 0.9, 0},
		{"lastHits", func(m matchdata.PlayerMinutes) matchdata.List[int] { return m.LastHits },
			func(i int) []int { return openDotaTurbo[i].lhT }, 0.95, 0},
		{"denies", func(m matchdata.PlayerMinutes) matchdata.List[int] { return m.Denies },
			func(i int) []int { return openDotaTurbo[i].dnT }, 1, 0},
	}
	for _, c := range columns {
		deltas := map[int]int{}
		points, equal, usual := 0, 0, 0
		for i := range openDotaTurbo {
			ours, theirs := c.ours(match.Timelines.PerMinute[i]), c.openDota(i)
			if len(ours) != len(theirs)+1 {
				t.Fatalf("%s player %d: %d points, OpenDota %d (+ the end of the match)", c.name, i, len(ours), len(theirs))
			}
			for m, value := range theirs {
				delta := ours[m] - value
				if delta < 0 {
					t.Errorf("%s player %d minute %d: %d, below OpenDota's %d", c.name, i, m, ours[m], value)
				}
				deltas[delta]++
				points++
				if delta == 0 {
					equal++
				}
				if delta >= 0 && delta <= c.usualDelta {
					usual++
				}
			}
		}
		t.Logf("%s: ours - OpenDota over %d points: %v", c.name, points, deltas)
		if share := float64(equal) / float64(points); share < c.minEqual {
			t.Errorf("%s: %.0f%% equal to OpenDota, want at least %.0f%%", c.name, 100*share, 100*c.minEqual)
		}
		if share := float64(usual) / float64(points); share < 0.9 {
			t.Errorf("%s: only %.0f%% within 0..%d of OpenDota", c.name, 100*share, c.usualDelta)
		}
	}
	// Minute 0 is 0 for both; minute 10 of the spike (Monkey King: gold +2, XP equal).
	if mk := match.Timelines.PerMinute[3]; mk.Gold[10] != 10794 || mk.XP[10] != 12182 {
		t.Errorf("Monkey King at 10:00: gold %d xp %d, want 10794 and 12182", mk.Gold[10], mk.XP[10])
	}
}

func TestRoleFromLaneFlags(t *testing.T) {
	tests := []struct {
		flags int
		want  int // 0 = null
	}{
		{0, 0}, {1, 1}, {4, 2}, {2, 3}, {8, 4}, {16, 5}, {3, 0}, {32, 0}, {-1, 0},
	}
	for _, test := range tests {
		got := roleFromLaneFlags(test.flags)
		if (got == nil) != (test.want == 0) || got != nil && *got != test.want {
			t.Errorf("roleFromLaneFlags(%d) = %v, want %d", test.flags, got, test.want)
		}
	}
}
