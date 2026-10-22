package replay

import (
	"encoding/json"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestCooldownDropped_ResetReductionAndNoise(t *testing.T) {
	cases := []struct {
		name      string
		before    cooldownReading
		remaining float64
		now       float64
		want      bool
	}{
		{"runs normally", cooldownReading{remaining: 20, at: 100}, 15, 105, false},
		{"update noise", cooldownReading{remaining: 20, at: 100}, 14.7, 105, false},
		{"reset to zero (Refresher)", cooldownReading{remaining: 20, at: 100}, 0, 101, true},
		{"reduced by 5 s", cooldownReading{remaining: 20, at: 100}, 14, 101, true},
		{"finished by itself", cooldownReading{remaining: 0.4, at: 100}, 0, 100.5, false},
		{"was ready", cooldownReading{remaining: 0, at: 100}, 0, 110, false},
		{"paused: server time ran, cooldown did not", cooldownReading{remaining: 20, at: 100}, 20, 160, false},
	}
	for _, c := range cases {
		if got := cooldownDropped(c.before, c.remaining, c.now); got != c.want {
			t.Errorf("%s: cooldownDropped = %v, want %v", c.name, got, c.want)
		}
	}
}

func TestStolenObservation_DoesNotRewriteEarlierLevelOrCarrier(t *testing.T) {
	c := &itemsCollector{stolenNow: map[entityKey]*stolenPeriod{}}
	key := entityKey{index: 100, serial: 2}
	c.observeStolen(key, "rubick", "spell", 1, false, ServerTime{Seconds: 10})
	c.observeStolen(key, "rubick", "spell", 2, true, ServerTime{Seconds: 20})
	c.observeStolen(key, "other", "spell", 2, true, ServerTime{Seconds: 30})
	if len(c.stolenPeriods) != 3 {
		t.Fatalf("got %d observation periods, want 3", len(c.stolenPeriods))
	}
	first := c.stolenPeriods[0]
	if first.level != 1 || first.hidden || first.hero != "rubick" || first.to == nil || first.to.Seconds != 20 {
		t.Fatalf("earlier observation was overwritten: %+v", first)
	}
	if c.stolenPeriods[1].to == nil || c.stolenPeriods[1].to.Seconds != 30 || c.stolenPeriods[2].hero != "other" {
		t.Fatal("carrier transition must close the earlier period")
	}
}

func TestStolenAbility_Contract(t *testing.T) {
	to := matchdata.Seconds(1700.5)
	cases := []struct {
		value matchdata.StolenAbility
		want  string
	}{
		{matchdata.StolenAbility{From: 1525, To: &to, Name: "slardar_slithereen_crush", Entity: 290, Serial: 848, Level: 4},
			`[1525,1700.5,"slardar_slithereen_crush",290,848,4,false]`},
		{matchdata.StolenAbility{From: 2700, Name: "antimage_blink", Entity: 1871, Serial: 382, Level: 4, Hidden: true},
			`[2700,null,"antimage_blink",1871,382,4,true]`},
	}
	for _, c := range cases {
		raw, err := json.Marshal(c.value)
		if err != nil {
			t.Fatal(err)
		}
		if string(raw) != c.want {
			t.Errorf("StolenAbility = %s, want %s", raw, c.want)
		}
	}
}

func TestCooldownChange_Contract(t *testing.T) {
	raw, err := json.Marshal(matchdata.CooldownChange{T: 601.25, Name: "item_refresher", Entity: 1234, Serial: 7, ReadyAt: 601.25})
	if err != nil {
		t.Fatal(err)
	}
	if want := `[601.3,"item_refresher",1234,7,601.3]`; string(raw) != want {
		t.Errorf("CooldownChange = %s, want %s", raw, want)
	}
}
