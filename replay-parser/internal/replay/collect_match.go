package replay

import (
	"math"
	"strconv"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

// matchCollector fills the match and draft sections. It is also the reference for the other collectors: state only
// in its own fields, hooks that copy what they need, conversion to the game clock only in Fill.
//
// Sources: CDemoFileInfo (match id, mode, league, winner, end time, Captains Mode picks/bans; at the very end of the
// file), the game rules (m_flGameEndTime via Clock.GameEnd, m_nGameWinner as the winner when the file info is
// missing) and the ban announcements (CHAT_MESSAGE_HERO_BANNED, value = heroId).
type matchCollector struct {
	session *Session

	matchID     uint64
	gameMode    int
	leagueID    int
	winner      int   // team number from the file info (2 Radiant, 3 Dire, 0 unknown)
	endedAt     int64 // Unix seconds; 0 unknown
	picksBans   []matchdata.DraftEvent
	rulesWinner int // m_nGameWinner when the match ended
	lastSecond  int
	bans        []int
}

func (c *matchCollector) Register(s *Session) {
	c.session = s
	s.OnFileInfo(func(info *dota.CDemoFileInfo) {
		game := info.GetGameInfo().GetDota()
		if game == nil {
			return
		}
		c.matchID = game.GetMatchId()
		c.gameMode = int(game.GetGameMode())
		c.leagueID = int(game.GetLeagueid())
		c.winner = int(game.GetGameWinner())
		c.endedAt = int64(game.GetEndTime())
		c.picksBans = nil
		for order, event := range game.GetPicksBans() {
			c.picksBans = append(c.picksBans, matchdata.DraftEvent{
				Order:  order,
				IsPick: event.GetIsPick(),
				Team:   teamOf(int(event.GetTeam())),
				HeroID: int(event.GetHeroId()),
			})
		}
	})
	s.OnChatEvent(func(event *dota.CDOTAUserMsg_ChatEvent, _ ServerTime) {
		if event.GetType() != dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_HERO_BANNED {
			return
		}
		hero := int(event.GetValue())
		for _, banned := range c.bans {
			if banned == hero {
				return // the same ban announced twice
			}
		}
		c.bans = append(c.bans, hero)
	})
	s.OnSecond(func(second int) { c.lastSecond = second })
	s.OnEnd(func(int) {
		if rules := s.Entity("CDOTAGamerulesProxy"); rules != nil {
			winner, _ := number(rules, "m_pGameRules.m_nGameWinner")
			c.rulesWinner = int(winner)
		}
	})
}

func (c *matchCollector) Fill(m *matchdata.Match) error {
	clock := c.session.Clock
	info := matchdata.Info{
		GameMode:        c.gameMode,
		LeagueID:        c.leagueID,
		DurationSeconds: matchdata.Seconds(c.lastSecond),
		PausedSeconds:   matchdata.Seconds(clock.PausedSeconds()),
	}
	if c.matchID != 0 {
		info.MatchID = strconv.FormatUint(c.matchID, 10)
	}
	winner := c.winner
	if winner != teamRadiant && winner != teamDire {
		winner = c.rulesWinner
	}
	if team := teamOf(winner); team != "" {
		info.Winner = &team
	}
	end, ended := clock.GameEnd()
	if ended {
		info.DurationSeconds = clock.Seconds(end)
	}
	if c.endedAt > 0 {
		endedAt := c.endedAt
		info.EndedAt = &endedAt
		if ended {
			// The server clock includes pauses, like the wall clock: the horn was this long before the end.
			startedAt := endedAt - int64(math.Round(end.Seconds-clock.game.gameStart))
			info.StartedAt = &startedAt
		}
	}
	m.Match = info
	m.Draft = matchdata.Draft{AnnouncedBans: c.bans, PicksBans: c.picksBans}
	return nil
}
