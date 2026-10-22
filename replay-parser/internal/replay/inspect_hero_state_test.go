package replay

import (
	"context"
	"errors"
	"math"
	"strings"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestInspectHeroStateRejectsCancelledAndUnreadableInput(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := InspectHeroState(ctx, strings.NewReader("")); !errors.Is(err, context.Canceled) {
		t.Fatalf("cancelled input: %v", err)
	}
	if _, err := InspectHeroState(context.Background(), strings.NewReader("not a replay")); !errors.Is(err, laning.ErrUnreadableReplay) {
		t.Fatalf("bad input: %v", err)
	}
}

func TestInspectHeroStateMatchesProductionSamplesAndSelectedSerials(t *testing.T) {
	for _, fixture := range []struct {
		name      string
		replay    realReplay
		extraHero int
	}{
		{"all-pick", allPickReplay, 0},
		{"phantom-lancer", realReplay{env: "HERO_PROBE_PL_REPLAY", path: "../../../work/replays/9042407280.dem"}, 12},
		{"monkey-king", realReplay{env: "HERO_PROBE_MK_REPLAY", path: "../../../work/replays/9030042976.dem"}, 114},
		{"paused-match", realReplay{env: "HERO_PROBE_PAUSED_REPLAY", path: "../../../work/replays/9045593156.dem"}, 0},
	} {
		t.Run(fixture.name, func(t *testing.T) {
			report, err := InspectHeroState(context.Background(), fixture.replay.open(t))
			if err != nil {
				t.Fatal(err)
			}
			match, err := analyzeMatch(context.Background(), fixture.replay.open(t), []Collector{&matchCollector{}, &playersCollector{}})
			if err != nil {
				t.Fatal(err)
			}
			if len(report.Heroes) != len(match.Players) || report.Build == 0 {
				t.Fatal("missing roster or build")
			}
			observedExtraHero := fixture.extraHero == 0
			for _, track := range report.Heroes {
				var positions *matchdata.PlayerPositions
				for _, player := range match.Players {
					if player.HeroID == track.HeroID {
						for i := range match.Timelines.Positions {
							if match.Timelines.Positions[i].Player == player.Player {
								positions = &match.Timelines.Positions[i]
							}
						}
					}
				}
				if positions == nil || len(track.Samples) != len(positions.Samples) {
					t.Fatalf("hero %d: sample alignment", track.HeroID)
				}
				for i, sample := range track.Samples {
					if sample.Seconds != positions.Samples[i].T || int32(sample.SelectedHandle&0x3fff) != sample.Entity.Index || int32(sample.SelectedHandle>>14) != sample.Entity.Serial {
						t.Fatalf("hero %d: wrong clock/index/serial at %d", track.HeroID, sample.Seconds)
					}
					s, d := positions.Stats[i], positions.CombatDetails[i]
					legacy := map[string]*float64{"m_flStrengthTotal": s.Strength, "m_flAgilityTotal": s.Agility, "m_flIntellectTotal": s.Intellect,
						"m_flPhysicalArmorValue": s.Armor, "m_flMagicalResistanceValue": s.MagicResist, "m_iMoveSpeed": s.MoveSpeed,
						"m_iDamageMin": s.DamageMin, "m_iDamageMax": s.DamageMax}
					details := map[string]*float64{"m_flStrength": d.StrengthBase, "m_flAgility": d.AgilityBase, "m_flIntellect": d.IntellectBase,
						"m_iBaseDamageMin": d.DamageBaseMin, "m_iBaseDamageMax": d.DamageBaseMax, "m_iDamageBonus": d.DamageBonus,
						"m_flBaseAttackTime": d.BaseAttackTime, "m_iAttackRange": d.AttackRange, "m_iAttackCapabilities": d.AttackCapability,
						"m_flHealthThinkRegen": d.HealthThinkRegen, "m_flManaThinkRegen": d.ManaThinkRegen}
					for j, property := range report.Properties {
						stored, found := legacy[property]
						scale := 10.0
						if !found {
							stored = details[property]
							scale = 10000
						}
						raw := sample.Values[j]
						if (stored == nil) != (raw == nil) || stored != nil && math.Abs(*stored-math.Round(*raw*scale)/scale) > 1e-9 {
							t.Fatalf("hero %d second %d property %s differs from selected-entity wire value", track.HeroID, sample.Seconds, property)
						}
					}
				}
				if track.Peak != nil {
					for _, other := range track.Peak.Others {
						if other.Index == track.Peak.Selected.Index && other.Serial == track.Peak.Selected.Serial {
							t.Fatal("selected entity listed as nonselected")
						}
					}
					if track.HeroID == fixture.extraHero {
						observedExtraHero = true
					}
				}
			}
			if !observedExtraHero {
				t.Fatalf("fixture did not exercise multiple entities for hero %d", fixture.extraHero)
			}
		})
	}
}
