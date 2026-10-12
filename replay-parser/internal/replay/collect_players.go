package replay

import (
	"errors"
	"fmt"
	"math"
	"strconv"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// playersCollector fills the players and timelines sections.
//
// Sources: the identity comes from Session.Players; the numbers from CDOTA_PlayerResource (K/D/A, level, rank, draft,
// runes, teamfight participation) and CDOTA_DataRadiant/Dire (gold, XP, net worth, last hits, damage, gold by source
// and spent, counters), read together as a snapshot: at every minute of the game clock (the per-minute timeline) and
// once more on the tick the match ends (the last point of the timeline and the players' final numbers). Positions
// come from each player's hero entity every matchdata.PositionIntervalSeconds, while it is alive.
//
// Total gold is m_iTotalEarnedGold (gold earned so far, not gold in hand). Each minute is read on its first tick, one
// tick after OpenDota's gold_t/xp_t/lh_t/dn_t: gold is ahead by the passive gold of that tick (+2..+8 at ~95% of
// the minutes of the Turbo match), XP, last hits and denies are equal, except when a kill or last hit lands exactly
// on that tick (then we already count it). See collect_players_test.go.
type playersCollector struct {
	session *Session

	players   []PlayerInfo
	last      []playerSnapshot // the latest snapshot read (the end of the match, after OnEnd)
	minutes   []matchdata.PlayerMinutes
	positions []matchdata.PlayerPositions
	err       error // first property missing: the replay's format is not the one this code knows
}

// playerSnapshot is one player's numbers at one moment.
type playerSnapshot struct {
	final     matchdata.PlayerFinal
	rankTier  int // 0 = hidden/unknown
	pickOrder int // 1-10; 0 = unknown
	randomed  bool
	laneFlags int // m_eLaneSelectionFlags: one bit per role-queue position
	facet     int // 0 = none
}

func (c *playersCollector) Register(s *Session) {
	c.session = s
	s.OnSecond(func(second int) {
		c.start()
		// Minute m is read on the first second at or after m:00; a minute the replay skipped gets the next reading.
		for len(c.minutes[0].Gold) <= second/60 {
			if !c.sampleMinute() {
				break
			}
		}
		if second%matchdata.PositionIntervalSeconds == 0 {
			c.samplePositions(second)
		}
	})
	s.OnEnd(func(int) {
		c.start()
		c.sampleMinute() // the last point: the end of the match (also the players' final numbers)
	})
}

// start prepares one entry per player, the first time a hook runs (the players are known by then).
func (c *playersCollector) start() {
	if c.players != nil {
		return
	}
	c.players = c.session.Players.All()
	for _, player := range c.players {
		c.minutes = append(c.minutes, matchdata.PlayerMinutes{Player: player.Index})
		c.positions = append(c.positions, matchdata.PlayerPositions{Player: player.Index})
	}
}

// sampleMinute reads every player's numbers and appends them to the per-minute columns. It reports false (and appends
// nothing) when the entities are gone (a replay that stops before the match ends) or a property is missing.
func (c *playersCollector) sampleMinute() bool {
	if c.err != nil {
		return false
	}
	snapshots := make([]playerSnapshot, len(c.players))
	for i, player := range c.players {
		snapshot, ok, err := c.read(player.Index)
		if err != nil {
			c.err = fmt.Errorf("player %d: %w", player.Index, err)
			return false
		}
		if !ok {
			return false
		}
		snapshots[i] = snapshot
	}
	for i, snapshot := range snapshots {
		f, m := snapshot.final, &c.minutes[i]
		m.Gold = append(m.Gold, f.GoldEarned)
		m.XP = append(m.XP, f.XP)
		m.NetWorth = append(m.NetWorth, f.NetWorth)
		m.LastHits = append(m.LastHits, f.LastHits)
		m.Denies = append(m.Denies, f.Denies)
		m.Level = append(m.Level, f.Level)
		m.Kills = append(m.Kills, f.Kills)
		m.Deaths = append(m.Deaths, f.Deaths)
		m.Assists = append(m.Assists, f.Assists)
		m.HeroDamage = append(m.HeroDamage, f.HeroDamage)
	}
	c.last = snapshots
	return true
}

// samplePositions records where each living hero is (a dead hero has no sample).
func (c *playersCollector) samplePositions(second int) {
	for i, player := range c.players {
		hero := c.session.Players.Hero(player.Index)
		if hero == nil {
			continue
		}
		if lifeState, ok := number(hero, "m_lifeState"); !ok || lifeState != 0 {
			continue // dying (1) or dead (2)
		}
		x, y, ok := position(hero)
		if !ok {
			continue
		}
		c.positions[i].Samples = append(c.positions[i].Samples,
			matchdata.Sample{T: second, X: int(math.Round(x)), Y: int(math.Round(y))})
	}
}

// read takes one player's snapshot. ok is false while (or once) CDOTA_PlayerResource or the team data entity does not
// exist; err says that a property the scoreboard needs is missing. The counters that only some patches have (Tormentor
// kills, Madstones...) are 0 when missing.
func (c *playersCollector) read(index int) (snapshot playerSnapshot, ok bool, err error) {
	resource, playerPrefix, teamPrefix := c.session.Players.Resource(index)
	data, dataPrefix := c.session.Players.TeamData(index)
	if resource == nil || data == nil {
		return playerSnapshot{}, false, nil
	}
	r, d := fields{entity: resource}, fields{entity: data}
	optional := func(e *manta.Entity, name string) float64 {
		value, _ := number(e, name)
		return value
	}
	pr := func(name string) int { return int(optional(resource, teamPrefix+name)) }
	dt := func(name string) int { return int(optional(data, dataPrefix+name)) }

	f := matchdata.PlayerFinal{
		Level:      r.int(teamPrefix + "m_iLevel"),
		Kills:      r.int(teamPrefix + "m_iKills"),
		Deaths:     r.int(teamPrefix + "m_iDeaths"),
		Assists:    r.int(teamPrefix + "m_iAssists"),
		LastHits:   d.int(dataPrefix + "m_iLastHitCount"),
		Denies:     d.int(dataPrefix + "m_iDenyCount"), // not m_iClaimedDenyCount: this is the scoreboard's
		NetWorth:   d.int(dataPrefix + "m_iNetWorth"),
		GoldEarned: d.int(dataPrefix + "m_iTotalEarnedGold"),
		XP:         d.int(dataPrefix + "m_iTotalEarnedXP"),
		HeroDamage: int(math.Round(d.float(dataPrefix + "m_flHeroDamage"))),

		MissedLastHits:         dt("m_iMissCount"),
		TowerDamage:            int(math.Round(optional(data, dataPrefix+"m_flTowerDamage"))),
		Healing:                int(math.Round(optional(data, dataPrefix+"m_fHealing"))),
		StunSeconds:            roundDecimals(optional(data, dataPrefix+"m_fStuns"), 100),
		TeamFightParticipation: roundDecimals(optional(resource, teamPrefix+"m_flTeamFightParticipation"), 1000),
		CampsStacked:           dt("m_iCampsStacked"),
		RunePickups:            dt("m_iRunePickups"),
		ObserversPlaced:        dt("m_iObserverWardsPlaced"),
		SentriesPlaced:         dt("m_iSentryWardsPlaced"),
		WardsDestroyed:         dt("m_iWardsDestroyed"),
		SmokesUsed:             dt("m_iSmokesUsed"),
		TowerKills:             dt("m_iTowerKills"),
		RoshanKills:            dt("m_iRoshanKills"),
		CourierKills:           dt("m_iCourierKills"),
		GoldBySource: matchdata.GoldBySource{
			Creeps:      dt("m_iCreepKillGold"),
			Heroes:      dt("m_iHeroKillGold"),
			Buildings:   dt("m_iBuildingGold"),
			Neutrals:    dt("m_iNeutralKillGold"),
			Roshan:      dt("m_iRoshanGold"),
			BountyRunes: dt("m_iBountyGold"),
			Passive:     dt("m_iIncomeGold"),
			Shared:      dt("m_iSharedGold"),
			Wards:       dt("m_iWardKillGold"),
			Couriers:    dt("m_iCourierGold"),
			Summons:     dt("m_iSummonKillGold"),
			Denies:      dt("m_iCreepDenyGold"),
			Abilities:   dt("m_iAbilityGold"),
			Comeback:    dt("m_iComebackGold"),
			Other:       dt("m_iOtherGold") + dt("m_iExperimentalGold") + dt("m_iExperimental2Gold"),
		},
		GoldSpent: matchdata.GoldSpent{
			Items:       dt("m_iGoldSpentOnItems"),
			Consumables: dt("m_iGoldSpentOnConsumables"),
			Support:     dt("m_iGoldSpentOnSupport"),
			Buybacks:    dt("m_iGoldSpentOnBuybacks"),
		},
		GoldLostToDeath:    dt("m_iGoldLostToDeath"),
		Gold:               dt("m_iReliableGold") + dt("m_iUnreliableGold"),
		FirstBlood:         pr("m_iFirstBloodClaimed") > 0,
		CreepsStacked:      dt("m_iCreepsStacked"),
		BountyRunes:        pr("m_iBountyRunes"),
		PowerRunes:         pr("m_iPowerRunes"),
		WaterRunes:         pr("m_iWaterRunes"),
		WardsPurchased:     dt("m_iWardsPurchased"),
		TormentorKills:     dt("m_iTormentorKills"),
		OutpostsCaptured:   pr("m_iOutpostsCaptured"),
		WatchersTaken:      dt("m_iWatchersTaken"),
		LotusesTaken:       dt("m_iLotusesTaken"),
		WisdomShrinesTaken: dt("m_iWisdomShrinesTaken"),
		NeutralTokensFound: dt("m_iNeutralTokensFound"),
		CommandsIssued:     dt("m_iCommandsIssued"),
	}
	if err := errors.Join(r.err, d.err); err != nil {
		return playerSnapshot{}, false, err
	}
	randomed, _ := resource.GetBool(teamPrefix + "m_bHasRandomed")
	return playerSnapshot{
		final:     f,
		rankTier:  int(optional(resource, playerPrefix+"m_iRankTier")),
		pickOrder: pr("m_unPickOrder"),
		randomed:  randomed,
		laneFlags: pr("m_eLaneSelectionFlags"),
		facet:     pr("m_nSelectedHeroFacet"),
	}, true, nil
}

func (c *playersCollector) Fill(m *matchdata.Match) error {
	m.Timelines.PositionIntervalSeconds = matchdata.PositionIntervalSeconds
	if c.err != nil {
		return c.err
	}
	if c.last == nil {
		return errors.New("the players' numbers were never read")
	}
	players := make(matchdata.List[matchdata.Player], 0, len(c.players))
	for i, player := range c.players {
		snapshot := c.last[i]
		players = append(players, matchdata.Player{
			Player:    player.Index,
			SteamID:   strconv.FormatUint(player.SteamID, 10),
			Name:      player.Name,
			Team:      player.Team,
			TeamSlot:  player.TeamSlot,
			HeroID:    player.HeroID,
			HeroName:  player.HeroName,
			RankTier:  knownInt(snapshot.rankTier),
			PickOrder: knownInt(snapshot.pickOrder),
			Randomed:  snapshot.randomed,
			Role:      roleFromLaneFlags(snapshot.laneFlags),
			Facet:     knownInt(snapshot.facet),
			Final:     snapshot.final,
		})
	}
	m.Players = players
	m.Timelines.PerMinute = c.minutes
	m.Timelines.Positions = c.positions
	return nil
}

// roleFromLaneFlags turns m_eLaneSelectionFlags (Valve's ELaneSelectionFlags: 1 safe lane, 2 off lane, 4 mid lane,
// 8 support, 16 hard support; one bit = the role the player was queued for) into the position 1-5. No bit (Turbo,
// unranked) or more than one is unknown. In match 9034320024 the Dire roles match the lanes played.
func roleFromLaneFlags(flags int) *int {
	positions := map[int]int{1: 1, 2: 3, 4: 2, 8: 4, 16: 5}
	role, ok := positions[flags]
	if !ok {
		return nil
	}
	return &role
}

// knownInt is &value, or nil for 0 and below (the replay's "unknown").
func knownInt(value int) *int {
	if value <= 0 {
		return nil
	}
	return &value
}

// roundDecimals rounds to 1/scale (scale 100 = two decimal places).
func roundDecimals(value, scale float64) float64 {
	return math.Round(value*scale) / scale
}
