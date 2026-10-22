package replay

import (
	"context"
	"io"
	"math"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// HeroStateInspection is a local development diagnostic, never part of the match/HTTP contract.
// Values are unrounded wire properties. Total in a property's name is not independent HUD verification.
type HeroStateInspection struct {
	Build      uint32                `json:"replayBuild"`
	Note       string                `json:"note"`
	Properties []string              `json:"properties"`
	Heroes     []HeroStateTrackProbe `json:"heroes"`
}

type HeroEntityProbe struct {
	Index  int32  `json:"index"`
	Serial int32  `json:"serial"`
	Class  string `json:"class"`
}

type HeroStateProbeSample struct {
	Seconds              int             `json:"seconds"`
	SelectedHandle       uint64          `json:"selectedHandle"`
	Entity               HeroEntityProbe `json:"entity"`
	Values               []*float64      `json:"values"`
	NonSelectedSameClass int             `json:"nonSelectedSameClass"`
}

type HeroEntityPeakProbe struct {
	Seconds  int               `json:"seconds"`
	Selected HeroEntityProbe   `json:"selected"`
	Others   []HeroEntityProbe `json:"others"`
}

type HeroStateTrackProbe struct {
	HeroID  int                    `json:"heroId"`
	Samples []HeroStateProbeSample `json:"samples"`
	Peak    *HeroEntityPeakProbe   `json:"peakNonSelectedSameClass"`
}

// InspectHeroState follows the production session's selected-hero handles and pause-aware clock.
// It exports no player names, player IDs, Steam IDs, chat or credentials. Other entities sharing
// a class are recorded as nonselected, not automatically classified as illusions or clones.
func InspectHeroState(ctx context.Context, r io.Reader) (HeroStateInspection, error) {
	c := &heroStateProbeCollector{report: HeroStateInspection{
		Note: "Raw replicated properties at production position-sample times. Selected handle resolves index and serial. Other same-class entities are not classified. No effective defense reconstruction or HUD oracle.",
		Properties: []string{
			"m_flStrengthTotal", "m_flAgilityTotal", "m_flIntellectTotal",
			"m_flStrength", "m_flAgility", "m_flIntellect",
			"m_flPhysicalArmorValue", "m_flMagicalResistanceValue", "m_iMoveSpeed",
			"m_iDamageMin", "m_iDamageMax", "m_iBaseDamageMin", "m_iBaseDamageMax", "m_iDamageBonus",
			"m_flBaseAttackTime", "m_iAttackRange", "m_iAttackCapabilities",
			"m_flHealthThinkRegen", "m_flManaThinkRegen",
		},
	}}
	_, err := analyzeMatch(ctx, r, []Collector{c})
	if err != nil {
		return HeroStateInspection{}, err
	}
	return c.report, nil
}

type heroStateProbeCollector struct {
	report HeroStateInspection
}

func (c *heroStateProbeCollector) Register(s *Session) {
	s.OnSecond(func(second int) {
		if second%matchdata.PositionIntervalSeconds != 0 {
			return
		}
		players := s.Players.All()
		if c.report.Heroes == nil {
			for _, player := range players {
				c.report.Heroes = append(c.report.Heroes, HeroStateTrackProbe{HeroID: player.HeroID, Samples: []HeroStateProbeSample{}})
			}
		}
		byClass := map[string][]*manta.Entity{}
		for _, entity := range s.Parser.FilterEntity(func(e *manta.Entity) bool {
			state, ok := number(e, "m_lifeState")
			return ok && state == 0
		}) {
			byClass[entity.GetClassName()] = append(byClass[entity.GetClassName()], entity)
		}
		for i, player := range players {
			hero := s.Players.Hero(player.Index)
			if hero == nil {
				continue
			}
			if state, ok := number(hero, "m_lifeState"); !ok || state != 0 {
				continue
			}
			if _, _, ok := position(hero); !ok {
				continue
			}
			resource, _, prefix := s.Players.Resource(player.Index)
			handle, ok := resource.GetUint64(prefix + "m_hSelectedHero")
			if !ok || s.Parser.FindEntityByHandle(handle) != hero {
				continue
			}
			entity := heroEntityProbe(hero)
			others := []HeroEntityProbe{}
			for _, other := range byClass[hero.GetClassName()] {
				if other.GetIndex() != hero.GetIndex() || other.GetSerial() != hero.GetSerial() {
					others = append(others, heroEntityProbe(other))
				}
			}
			values := make([]*float64, len(c.report.Properties))
			for j, property := range c.report.Properties {
				if value, found := number(hero, property); found && !math.IsNaN(value) && !math.IsInf(value, 0) {
					values[j] = &value
				}
			}
			track := &c.report.Heroes[i]
			track.Samples = append(track.Samples, HeroStateProbeSample{second, handle, entity, values, len(others)})
			if len(others) > 0 && (track.Peak == nil || len(others) > len(track.Peak.Others)) {
				track.Peak = &HeroEntityPeakProbe{second, entity, others}
			}
		}
	})
	s.OnEnd(func(int) { c.report.Build = s.Parser.GameBuild })
}

func (c *heroStateProbeCollector) Fill(*matchdata.Match) error { return nil }

func heroEntityProbe(e *manta.Entity) HeroEntityProbe {
	return HeroEntityProbe{e.GetIndex(), e.GetSerial(), e.GetClassName()}
}
