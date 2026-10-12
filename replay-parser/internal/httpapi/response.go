package httpapi

import (
	"strconv"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
)

// laningResponse is the 200 body of POST /v1/laning (the contract in replay-parser/CLAUDE.md). It is separate from
// laning.Result so the JSON names and types are fixed here, not by the rules package.
type laningResponse struct {
	LaningEndsAtSeconds int              `json:"laningEndsAtSeconds"`
	Players             []playerResponse `json:"players"`
}

type playerResponse struct {
	HeroID int `json:"heroId"`
	// Text, not a number: a SteamID64 is bigger than 2^53 and JavaScript-style parsers would round it. "0" for bots.
	SteamID   string      `json:"steamId"`
	IsRadiant bool        `json:"isRadiant"`
	Lane      laning.Lane `json:"lane"`
	LastHits  int         `json:"lastHits"`
	Denies    int         `json:"denies"`
	NetWorth  int         `json:"netWorth"`
	XP        int         `json:"xp"`
	Deaths    int         `json:"deaths"`
}

func newLaningResponse(result laning.Result) laningResponse {
	// Never nil, so an empty list is [] in the JSON, not null.
	players := make([]playerResponse, 0, len(result.Players))
	for _, player := range result.Players {
		players = append(players, playerResponse{
			HeroID:    player.HeroID,
			SteamID:   strconv.FormatUint(player.SteamID, 10),
			IsRadiant: player.IsRadiant,
			Lane:      player.Lane,
			LastHits:  player.LastHits,
			Denies:    player.Denies,
			NetWorth:  player.NetWorth,
			XP:        player.XP,
			Deaths:    player.Deaths,
		})
	}
	return laningResponse{LaningEndsAtSeconds: result.EndsAtSeconds, Players: players}
}
