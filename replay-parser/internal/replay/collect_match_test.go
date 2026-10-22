package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"testing"
)

func TestMatchMetadataDistinguishesEngineBuildAndTruncatedClock(t *testing.T) {
	for _, ended := range []bool{false, true} {
		clock := &Clock{game: gameClock{gameStart: 100}}
		if ended {
			clock.gameEnd = &ServerTime{Seconds: 790.4}
		}
		c := &matchCollector{session: &Session{Parser: &manta.Parser{GameBuild: 6962}, Clock: clock}, lastSecond: 690}
		m := matchdata.New()
		if err := c.Fill(&m); err != nil {
			t.Fatal(err)
		}
		if m.Match.EngineBuild == nil || *m.Match.EngineBuild != 6962 {
			t.Fatal("missing engine build")
		}
		if m.Match.MatchEnded == nil || *m.Match.MatchEnded != ended {
			t.Fatal("incorrect end evidence")
		}
		if ended && float64(m.Match.DurationSeconds) != 690.4 {
			t.Fatalf("lost duration: %v", m.Match.DurationSeconds)
		}
	}
}
