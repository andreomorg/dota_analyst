package replay

import (
	"context"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// The laning section of /v1/match must give the same lanes as /v1/laning (the API now only calls /v1/match), and its
// numbers at 10:00 are the minute-10 point of the timelines.
func TestMatchLaningEqualsLaningEndpoint(t *testing.T) {
	for _, replay := range []realReplay{allPickReplay, turboReplay} {
		t.Run(replay.env, func(t *testing.T) {
			match, err := analyzeMatch(context.Background(), replay.open(t), []Collector{&playersCollector{}, &laningCollector{}})
			if err != nil {
				t.Fatal(err)
			}
			laningResult, err := Analyzer{}.Analyze(context.Background(), replay.open(t))
			if err != nil {
				t.Fatal(err)
			}

			if match.Laning.EndsAtSeconds != laningResult.EndsAtSeconds {
				t.Errorf("endsAtSeconds = %d, /v1/laning says %d", match.Laning.EndsAtSeconds, laningResult.EndsAtSeconds)
			}
			if len(match.Laning.Lanes) != 10 {
				t.Fatalf("%d lanes, want 10", len(match.Laning.Lanes))
			}
			for _, want := range laningResult.Players {
				i := playerOfHero(t, match, want.HeroID)
				if got := match.Laning.Lanes[i]; got.Player != i || got.Lane != string(want.Lane) {
					t.Errorf("hero %d: lane %+v, /v1/laning says %q", want.HeroID, got, want.Lane)
				}
				minutes := match.Timelines.PerMinute[i]
				const minute = laning.EndsAtSeconds / 60
				got := laning.PlayerLaning{LastHits: minutes.LastHits[minute], Denies: minutes.Denies[minute],
					NetWorth: minutes.NetWorth[minute], XP: minutes.XP[minute], Deaths: minutes.Deaths[minute]}
				if got.LastHits != want.LastHits || got.Denies != want.Denies || got.NetWorth != want.NetWorth ||
					got.XP != want.XP || got.Deaths != want.Deaths {
					t.Errorf("hero %d at 10:00: timelines %+v, /v1/laning %+v", want.HeroID, got, want)
				}
			}
		})
	}
}

func playerOfHero(t *testing.T, match matchdata.Match, heroID int) int {
	t.Helper()
	for _, player := range match.Players {
		if player.HeroID == heroID {
			return player.Player
		}
	}
	t.Fatalf("hero %d not in the match", heroID)
	return -1
}
