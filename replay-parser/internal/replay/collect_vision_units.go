package replay

import (
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// visionUnitsCollector writes timelines.visionUnits (MAPA MP4): every 2 s, each team's living units that carry a vision
// range and move — lane and siege creeps, summons, illusions, controlled neutrals — grouped by 128-unit cell and range.
// What other sections already have, or never moves, is left out (players' heroes, wards, couriers, towers, barracks,
// Ancients, fillers, effigies, the fountain, base blockers...): the C# side adds those itself. Spike: cmd/inspect
// -vision-units-spike (every unit with a range, by class).
type visionUnitsCollector struct {
	samples []matchdata.VisionUnits
}

// Units of a team that never move, or that another section already has.
var fixedVisionClasses = map[string]bool{
	"CDOTA_BaseNPC_Tower": true, "CDOTA_BaseNPC_Barracks": true, "CDOTA_BaseNPC_Fort": true, "CDOTA_BaseNPC_Filler": true,
	"CDOTA_BaseNPC_Effigy_Statue": true, "CDOTA_BaseNPC_HallofFame": true, "CDOTA_BaseNPC_NeutralItemStash": true,
	"CDOTA_NPC_BaseBlocker": true, "CDOTA_Unit_Fountain": true, "CDOTA_BaseNPC_Watch_Tower": true,
	"CDOTA_NPC_Observer_Ward": true, "CDOTA_NPC_Observer_Ward_TrueSight": true, "CDOTA_Unit_Courier": true,
}

// Units in the same cell of this size (map units) with the same ranges are one circle.
const visionCellSize = 128

func (c *visionUnitsCollector) Register(s *Session) {
	s.OnSecond(func(t int) {
		if t%matchdata.PositionIntervalSeconds != 0 {
			return
		}
		mainHeroes := map[int32]bool{}
		for _, player := range s.Players.All() {
			if hero := s.Players.Hero(player.Index); hero != nil {
				mainHeroes[hero.GetIndex()] = true
			}
		}
		type cell struct{ x, y, day, night int }
		teams := map[matchdata.Team]map[cell]bool{matchdata.Radiant: {}, matchdata.Dire: {}}
		for _, e := range s.Parser.FilterEntity(func(e *manta.Entity) bool { return !fixedVisionClasses[e.GetClassName()] }) {
			day, hasDay := number(e, "m_iDayTimeVisionRange")
			if !hasDay || mainHeroes[e.GetIndex()] {
				continue
			}
			night, _ := number(e, "m_iNightTimeVisionRange")
			if day <= 0 && night <= 0 {
				continue
			}
			team, known := number(e, "m_iTeamNum")
			life, alive := number(e, "m_lifeState")
			circles := teams[teamOf(int(team))]
			if !known || !alive || life != 0 || circles == nil {
				continue
			}
			x, y, positioned := position(e)
			if !positioned {
				continue
			}
			circles[cell{visionCell(x), visionCell(y), int(day), int(night)}] = true
		}
		sample := matchdata.VisionUnits{T: t}
		for team, circles := range teams {
			list := make(matchdata.List[matchdata.VisionCircle], 0, len(circles))
			for at := range circles {
				list = append(list, matchdata.VisionCircle{X: at.x, Y: at.y, Day: at.day, Night: at.night})
			}
			sortCircles(list)
			if team == matchdata.Radiant {
				sample.Radiant = list
			} else {
				sample.Dire = list
			}
		}
		c.samples = append(c.samples, sample)
	})
}

// visionCell is the centre of the 128-unit cell a coordinate falls in.
func visionCell(v float64) int {
	return int(math.Floor(v/visionCellSize))*visionCellSize + visionCellSize/2
}

func sortCircles(list matchdata.List[matchdata.VisionCircle]) {
	sort.Slice(list, func(i, j int) bool {
		a, b := list[i], list[j]
		if a.X != b.X {
			return a.X < b.X
		}
		if a.Y != b.Y {
			return a.Y < b.Y
		}
		if a.Day != b.Day {
			return a.Day < b.Day
		}
		return a.Night < b.Night
	})
}

func (c *visionUnitsCollector) Fill(m *matchdata.Match) error {
	m.Timelines.VisionUnits = c.samples
	return nil
}
