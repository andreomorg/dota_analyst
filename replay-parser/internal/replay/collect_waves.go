package replay

import (
	"math"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// wavesCollector fills timelines.waves: how far each team's lane creeps reach in each lane, every
// matchdata.PositionIntervalSeconds of the game clock (with the hero positions).
//
// Sources: the living lane creep entities (CDOTA_BaseNPC_Creep_Lane, and the siege creeps CDOTA_BaseNPC_Creep_Siege),
// kept while they exist; their team (m_iTeamNum), life state and position (CBodyComponent, like heroes). The lane is
// laning.ZoneOf; creeps already in a base go to the lane by the diagonal (|x - y| small = middle).
// Class names and health properties were checked on replay 9034320024; unknown classes leave lists empty.
type wavesCollector struct {
	lastPositions map[entityKey]creepPosition
	dead          map[entityKey]bool
	deaths        []trackedCreepDeath
	session       *Session
	creeps        map[entityKey]*manta.Entity
	fronts        map[string]matchdata.List[matchdata.WaveFront]
	sizes         map[string]matchdata.List[matchdata.WaveSize]
	strength      map[string]matchdata.List[matchdata.WaveStrength]
	composition   map[string]matchdata.List[matchdata.WaveComposition]
	offLane       matchdata.List[matchdata.OffLaneCreeps]
}

var waveLanes = []string{string(laning.ZoneTop), string(laning.ZoneMiddle), string(laning.ZoneBottom)}

// A base creep this close to the diagonal (|x - y|) is in the middle lane, like laning's middle band.
const baseMiddleGap = 1500

func (c *wavesCollector) Register(s *Session) {
	c.session = s
	c.lastPositions = map[entityKey]creepPosition{}
	c.dead = map[entityKey]bool{}
	c.creeps = map[entityKey]*manta.Entity{}
	c.fronts = map[string]matchdata.List[matchdata.WaveFront]{}
	c.sizes = map[string]matchdata.List[matchdata.WaveSize]{}
	c.strength = map[string]matchdata.List[matchdata.WaveStrength]{}
	c.composition = map[string]matchdata.List[matchdata.WaveComposition]{}
	track := func(e *manta.Entity, op manta.EntityOp) {
		key := keyOf(e)
		life, known := number(e, "m_lifeState")
		if known && life != 0 && !c.dead[key] {
			c.dead[key] = true
			team, _ := number(e, "m_iTeamNum")
			c.deaths = append(c.deaths, trackedCreepDeath{at: s.Clock.Now(), name: creepUnitName(s, e), team: teamOf(int(team)), position: c.lastPositions[key]})
		}

		if op.Flag(manta.EntityOpDeleted) {
			delete(c.creeps, keyOf(e))
			delete(c.lastPositions, key)
			delete(c.dead, key)
			return
		}
		c.creeps[keyOf(e)] = e
	}
	s.OnEntity("CDOTA_BaseNPC_Creep_Lane", track)
	s.OnEntity("CDOTA_BaseNPC_Creep_Siege", track)
	s.OnSecond(func(second int) {
		if second >= 0 && second%matchdata.PositionIntervalSeconds == 0 {
			c.sample(second)
		}
	})
}

// sample records each lane's fronts at this second; a lane without creeps of either team gets no entry.
func (c *wavesCollector) sample(second int) {
	type point struct {
		x, y          float64
		health, siege int
		kind          int
		low           bool
	}
	type front struct {
		radiant, dire *int
		points        [2][]point
		leading       [2]point
	}
	fronts := map[string]*front{}
	var off [2][]point
	for _, creep := range c.creeps {
		if lifeState, ok := number(creep, "m_lifeState"); !ok || lifeState != 0 {
			continue
		}
		team, ok := number(creep, "m_iTeamNum")
		if !ok || (team != teamRadiant && team != teamDire) {
			continue
		}
		x, y, ok := position(creep)
		if !ok {
			continue
		}
		c.lastPositions[keyOf(creep)] = creepPosition{x: int(math.Round(x)), y: int(math.Round(y)), known: true}
		side := 0
		if team == teamDire {
			side = 1
		}
		health, _ := number(creep, "m_iHealth")
		siege := 0
		if creep.GetClassName() == "CDOTA_BaseNPC_Creep_Siege" {
			siege = 1
		}
		maxHealth, maxKnown := number(creep, "m_iMaxHealth")
		_, healthKnown := number(creep, "m_iHealth")
		p := point{x: x, y: y, health: max(0, int(health)), siege: siege,
			kind: waveCreepKind(creepUnitName(c.session, creep)), low: healthKnown && maxKnown && maxHealth > 0 && health >= 0 && health <= maxHealth*.25}
		lane := waveLane(x, y)
		if lane == "" {
			off[side] = append(off[side], p)
			continue
		}
		if fronts[lane] == nil {
			fronts[lane] = &front{}
		}
		progress := int(math.Round(x + y))
		f := fronts[lane]

		f.points[side] = append(f.points[side], p)
		// Keep coordinates alongside progress so distance uses the world plane, not x+y.
		leader := f.leading[side]
		if len(f.points[side]) == 1 || team == teamRadiant && x+y > leader.x+leader.y ||
			team == teamDire && x+y < leader.x+leader.y ||
			x+y == leader.x+leader.y && x > leader.x {
			f.leading[side] = p
		}
		if team == teamRadiant && (f.radiant == nil || progress > *f.radiant) {
			f.radiant = &progress
		}
		if team == teamDire && (f.dire == nil || progress < *f.dire) {
			p := progress
			f.dire = &p
		}
	}
	for _, lane := range waveLanes {
		if f := fronts[lane]; f != nil {
			c.fronts[lane] = append(c.fronts[lane], matchdata.WaveFront{T: second, Radiant: f.radiant, Dire: f.dire})
			var atFront, health, frontHealth, siege [2]int
			var centers [2]*int
			var composition [2][7]int
			// The final leader is only known after sampling all entities. Reuse cached
			// positions rather than reading entity properties a second time.
			for side, points := range f.points {
				sum := 0.0
				for _, p := range points {
					if p.kind >= 0 {
						composition[side][p.kind]++
					}
					health[side] += p.health
					siege[side] = max(siege[side], p.siege)
					dx, dy := p.x-f.leading[side].x, p.y-f.leading[side].y
					if dx*dx+dy*dy <= 1200*1200 {
						atFront[side]++
						if p.low {
							composition[side][6]++
						}
						frontHealth[side] += p.health
						sum += p.x + p.y
					}
				}
				if atFront[side] > 0 {
					center := int(math.Round(sum / float64(atFront[side])))
					centers[side] = &center
				}
			}
			c.strength[lane] = append(c.strength[lane], matchdata.WaveStrength{T: second, RadiantHealth: health[0], DireHealth: health[1], RadiantHealthAtFront: frontHealth[0], DireHealthAtFront: frontHealth[1], RadiantSiege: siege[0], DireSiege: siege[1], RadiantCenter: centers[0], DireCenter: centers[1]})
			c.composition[lane] = append(c.composition[lane], matchdata.WaveComposition{T: second, Radiant: composition[0], Dire: composition[1]})
			c.sizes[lane] = append(c.sizes[lane], matchdata.WaveSize{T: second,
				RadiantInLane: len(f.points[0]), DireInLane: len(f.points[1]),
				RadiantAtFront: atFront[0], DireAtFront: atFront[1]})
		}
	}
	if len(off[0])+len(off[1]) > 0 {
		row := matchdata.OffLaneCreeps{T: second, RadiantCount: len(off[0]), DireCount: len(off[1])}
		means := [2][2]*int{}
		for side, points := range off {
			if len(points) == 0 {
				continue
			}
			x, y := 0.0, 0.0
			for _, p := range points {
				x += p.x
				y += p.y
			}
			mx, my := int(math.Round(x/float64(len(points)))), int(math.Round(y/float64(len(points))))
			means[side] = [2]*int{&mx, &my}
		}
		row.RadiantMeanX, row.RadiantMeanY, row.DireMeanX, row.DireMeanY = means[0][0], means[0][1], means[1][0], means[1][1]
		c.offLane = append(c.offLane, row)
	}
}

// waveLane is the lane of a creep at (x, y); "" in the jungle (a creep pulled or walking off its lane).
func waveLane(x, y float64) string {
	switch zone := laning.ZoneOf(x, y); zone {
	case laning.ZoneTop, laning.ZoneMiddle, laning.ZoneBottom:
		return string(zone)
	case laning.ZoneBase:
		switch {
		case math.Abs(x-y) < baseMiddleGap:
			return string(laning.ZoneMiddle)
		case y > x:
			return string(laning.ZoneTop)
		default:
			return string(laning.ZoneBottom)
		}
	default:
		return ""
	}
}

func (c *wavesCollector) Fill(m *matchdata.Match) error {
	waves := make(matchdata.List[matchdata.LaneWaves], 0, len(waveLanes))
	for _, lane := range waveLanes {
		waves = append(waves, matchdata.LaneWaves{Lane: lane, Fronts: c.fronts[lane], Sizes: c.sizes[lane], Strength: c.strength[lane], Composition: c.composition[lane]})
	}
	m.Timelines.Waves = waves
	m.Timelines.OffLaneCreeps = c.offLane
	return nil
}

// Upgrades take precedence so every recognized unit contributes to exactly one category.
// Unknown names remain unclassified rather than silently being called melee.
func waveCreepKind(name string) int {
	if name == "npc_dota_goodguys_siege" || name == "npc_dota_badguys_siege" {
		return 2
	}
	if !strings.HasPrefix(name, "npc_dota_creep_goodguys_") && !strings.HasPrefix(name, "npc_dota_creep_badguys_") {
		return -1
	}
	switch {
	case strings.Contains(name, "_mega"):
		return 5
	case strings.Contains(name, "_upgraded"):
		return 4
	case strings.Contains(name, "flagbearer"):
		return 3
	case strings.Contains(name, "_siege"):
		return 2
	case strings.Contains(name, "_ranged"):
		return 1
	case strings.Contains(name, "_melee"):
		return 0
	default:
		return -1
	}
}

// Entity serials prevent reused indices from inheriting another creep's last sampled position.
type creepPosition struct {
	x, y  int
	known bool
}
type trackedCreepDeath struct {
	at       ServerTime
	name     string
	team     matchdata.Team
	position creepPosition
}

// Unit names distinguish melee/ranged/siege; the entity name is only npc_dota_creep_lane.
func creepUnitName(s *Session, e *manta.Entity) string {
	if index, ok := e.GetInt32("m_iUnitNameStringTableIndex"); ok {
		name, _ := s.Parser.LookupStringByIndex("UnitNames", index)
		return name
	}
	return ""
}
