package replay

import (
	"context"
	"math"
	"reflect"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

func TestTormentorSiteAmbiguity(t *testing.T) {
	sites := map[string]*matchdata.TormentorState{"a": {X: 0, Y: 0}, "b": {X: 1000, Y: 0}}
	if uniqueTormentorSite(0, 0, sites) != "a" || uniqueTormentorSite(500, 0, sites) != "" || uniqueTormentorSite(3000, 0, sites) != "" {
		t.Fatal("site matching must reject overlaps and remote units")
	}
	one, zero := 1, 0
	if sameOptionalInt(nil, &zero) || sameOptionalInt(&zero, &one) || !sameOptionalInt(nil, nil) || !sameOptionalInt(&one, &one) {
		t.Fatal("unknown must not be treated as dead")
	}
}

func TestTypedCombatExcludesIllusionTargets(t *testing.T) {
	yes, no := true, false
	amount, kind := uint32(10), uint32(1)
	c := &combatCollector{session: &Session{Players: &Players{list: []PlayerInfo{{Index: 0, Team: matchdata.Radiant}, {Index: 1, Team: matchdata.Dire}}}}, damage: map[combatKey]*combatTotal{}, healing: map[combatKey]*combatTotal{}}
	raw := &dota.CMsgDOTACombatLogEntry{IsAttackerHero: &yes, IsTargetHero: &yes, IsTargetIllusion: &yes, IsAttackerIllusion: &no, Value: &amount, DamageType: &kind}
	e := CombatEntry{Raw: raw, AttackerPlayer: 0, TargetPlayer: 1}
	c.onDamage(e)
	c.onHeal(e)
	if len(c.typed) != 0 {
		t.Fatal("illusion targets entered typed damage or healing")
	}
	raw.IsTargetIllusion = &no
	raw.IsAttackerIllusion = &yes
	c.onDamage(e)
	c.onHeal(e)
	if len(c.typed) != 2 {
		t.Fatal("illusion source must retain the owner's existing attribution")
	}
}

func TestCourierChangesThresholdAndState(t *testing.T) {
	flying, ground := true, false
	base := matchdata.CourierSample{Alive: 1, Flying: &ground}
	for _, tt := range []struct {
		name string
		next matchdata.CourierSample
		want bool
	}{
		{"unchanged", base, false},
		{"threshold", matchdata.CourierSample{Alive: 1, X: 200, Flying: &ground}, false},
		{"beyond", matchdata.CourierSample{Alive: 1, X: 201, Flying: &ground}, true},
		{"diagonal", matchdata.CourierSample{Alive: 1, X: 150, Y: 150, Flying: &ground}, true},
		{"death", matchdata.CourierSample{Alive: 0, Flying: &ground}, true},
		{"flight", matchdata.CourierSample{Alive: 1, Flying: &flying}, true},
		{"unknown flight", matchdata.CourierSample{Alive: 1}, true},
	} {
		t.Run(tt.name, func(t *testing.T) {
			if got := courierChanged(base, tt.next); got != tt.want {
				t.Fatalf("changed=%v want=%v", got, tt.want)
			}
		})
	}
}

func TestNormalizedYaw(t *testing.T) {
	for _, tt := range []struct {
		input float64
		want  int
	}{{0, 0}, {359.4, 359}, {359.6, 0}, {360, 0}, {-90, 270}, {720.6, 1}, {226.40625, 226}} {
		if got := normalizedYaw(tt.input); got == nil || *got != tt.want {
			t.Errorf("yaw %v = %v want %d", tt.input, got, tt.want)
		}
	}
	if normalizedYaw(math.NaN()) != nil || normalizedYaw(math.Inf(1)) != nil {
		t.Fatal("nonfinite yaw must be unknown")
	}
}

func TestTypedBucketsBoundariesAndHealing(t *testing.T) {
	c := &combatCollector{session: &Session{Clock: &Clock{}}, typed: []typedCombatEvent{
		{at: ServerTime{Seconds: 1.99}, source: 0, target: 1, amount: 10, damageType: 1},
		{at: ServerTime{Seconds: 2}, source: 0, target: 1, amount: 20, damageType: 2},
		{at: ServerTime{Seconds: 2.1}, source: 0, target: 1, amount: 30, damageType: 4},
		{at: ServerTime{Seconds: 2.2}, source: 0, target: 1, amount: 99, damageType: 0},
		{at: ServerTime{Seconds: 2.3}, source: 0, target: 0, amount: 50, heal: true},
		{at: ServerTime{Seconds: 2.4}, source: -1, target: 1, amount: 15, heal: true},
		{at: ServerTime{Seconds: -0.1}, source: 0, target: 1, amount: 5, damageType: 1},
	}}
	rows := make(matchdata.List[matchdata.PlayerCombatBuckets], 2)
	c.fillTypedBuckets(rows)
	wantTo := matchdata.List[matchdata.DamageTypeBucket]{{-2, 5, 0, 0, 0, 0, 0}, {0, 10, 0, 0, 0, 0, 0}, {2, 0, 20, 30, 0, 0, 0}}
	if !reflect.DeepEqual(rows[0].ByType, wantTo) {
		t.Fatalf("to=%v want=%v", rows[0].ByType, wantTo)
	}
	if got := rows[1].ByType[2]; got != (matchdata.DamageTypeBucket{2, 0, 0, 0, 0, 20, 30}) {
		t.Fatalf("from=%v", got)
	}
	if !reflect.DeepEqual(rows[0].Healing, matchdata.List[matchdata.HealingBucket]{{2, 50, 50}}) || !reflect.DeepEqual(rows[1].Healing, matchdata.List[matchdata.HealingBucket]{{2, 15, 0}}) {
		t.Fatalf("healing=%v %v", rows[0].Healing, rows[1].Healing)
	}
}

// The replay assertions compare independent existing aggregates and preserve sample alignment.
func TestWorldExtractionAllPick(t *testing.T) {
	m, err := (MatchAnalyzer{}).Analyze(context.Background(), allPickReplay.open(t))
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Timelines.Couriers) != 10 {
		t.Fatalf("couriers=%d", len(m.Timelines.Couriers))
	}
	teams := map[matchdata.Team]int{}
	ids := map[string]bool{}
	for _, c := range m.Timelines.Couriers {
		if ids[c.ID] {
			t.Errorf("duplicate courier %s", c.ID)
		}
		ids[c.ID] = true
		teams[c.Team]++
		for i, v := range c.Samples {
			if v.T%2 != 0 || i > 0 && (v.T <= c.Samples[i-1].T || !courierChanged(c.Samples[i-1], v)) {
				t.Errorf("bad courier sample %+v", v)
			}
		}
	}
	if teams[matchdata.Radiant] != 5 || teams[matchdata.Dire] != 5 {
		t.Fatalf("teams=%v", teams)
	}
	stash := 0
	for _, p := range m.Items.Slots {
		for _, v := range p.Changes {
			if v.Slot >= 9 && v.Slot <= 14 {
				stash++
			}
		}
	}
	if stash == 0 {
		t.Fatal("missing replicated stash changes")
	}
	for _, p := range m.Timelines.Positions {
		if len(p.Facing) != len(p.Samples) {
			t.Fatalf("player %d facing misaligned", p.Player)
		}
		for _, yaw := range p.Facing {
			if yaw == nil || *yaw < 0 || *yaw > 359 {
				t.Errorf("player %d invalid yaw %v", p.Player, yaw)
			}
		}
	}
	var healTo, healFrom [playerCount]int
	for _, v := range m.Combat.Healing {
		if v.TargetKind != matchdata.KindHero {
			continue
		}
		if v.TargetPlayer != nil {
			healTo[*v.TargetPlayer] += v.Amount
		}
		if v.SourcePlayer != nil {
			healFrom[*v.SourcePlayer] += v.Amount
		}
	}
	for _, row := range m.Combat.Buckets {
		received, done := 0, 0
		for i, v := range row.Healing {
			received += v[1]
			done += v[2]
			if i > 0 && v[0] <= row.Healing[i-1][0] {
				t.Fatal("healing unsorted")
			}
		}
		if received != healTo[row.Player] || done != healFrom[row.Player] {
			t.Errorf("healing player %d got %d/%d want %d/%d", row.Player, received, done, healTo[row.Player], healFrom[row.Player])
		}
		to, from := 0, 0
		for _, v := range row.ByType {
			to += v[1] + v[2] + v[3]
			from += v[4] + v[5] + v[6]
		}
		oldTo, oldFrom := 0, 0
		for _, v := range row.ToHeroes {
			oldTo += v.Amount
		}
		for _, v := range row.FromHeroes {
			oldFrom += v.Amount
		}
		if to > oldTo || from > oldFrom {
			t.Errorf("typed totals exceed all types for player %d", row.Player)
		}
	}
	if len(m.Objectives.OutpostCaptures) != len(m.Objectives.Outposts) || len(m.Objectives.OutpostCaptures) != 1 {
		t.Fatal("outpost message lost")
	}
	if m.Objectives.OutpostCaptures[0].ID != 729 || m.Objectives.OutpostCaptures[0].Team != matchdata.Radiant {
		t.Fatalf("capture=%+v", m.Objectives.OutpostCaptures)
	}
	if len(m.Objectives.TormentorStates) != 2 {
		t.Fatalf("tormentor sites=%d", len(m.Objectives.TormentorStates))
	}
	for _, site := range m.Objectives.TormentorStates {
		for i, v := range site.Samples {
			if v.Alive != nil && *v.Alive != 0 && *v.Alive != 1 {
				t.Fatal("invalid alive state")
			}
			if i > 0 && (v.T <= site.Samples[i-1].T || sameOptionalInt(v.Alive, site.Samples[i-1].Alive)) {
				t.Fatal("tormentor not sparse")
			}
		}
	}
}
