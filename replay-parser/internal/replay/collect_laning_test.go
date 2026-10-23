package replay

import (
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

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
