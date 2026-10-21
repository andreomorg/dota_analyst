package replay

import (
	"context"
	"fmt"
	"io"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// Collector fills one or more sections of the match document. Each collector lives in its own file
// (collect_<section>.go) and keeps all its state in its own value: the service may parse two replays at the same
// time, and a new set of collectors is made for every replay.
type Collector interface {
	// Register subscribes to the session's hooks. It runs before the replay is read; keep the session if Fill needs
	// it (Clock to convert times, Players, Name). Nothing in the replay has been read yet.
	Register(s *Session)
	// Fill writes the collector's sections after the whole replay was read; the clock and the players are known.
	// Collectors run in a fixed order; orders reads the sample seconds filled by players. An error makes the replay
	// unreadable (422): return one only for data that is wrong, not for data that is missing (leave that empty).
	Fill(m *matchdata.Match) error
}

// newCollectors makes a fresh set of collectors for one replay, in the order they fill the document. Each card adds
// its collector's work in its own file; this list only changes when a new collector file appears.
func newCollectors() []Collector {
	waves := &wavesCollector{}
	return []Collector{
		&matchCollector{},              // match, draft (collect_match.go)
		&playersCollector{},            // players, timelines (collect_players.go, card 03)
		&laningCollector{},             // laning (collect_laning.go, sprint 11)
		&combatCollector{waves: waves}, // kills, buybacks, combat (collect_combat.go, card 04)
		&itemsCollector{},              // items, abilities (collect_items.go, card 05)
		&visionCollector{},             // vision, objectives (collect_vision.go, card 06)
		waves,                          // timelines.waves (collect_waves.go, death-risk R12-B7)
		&ordersCollector{},             // timelines.positions[].orders (after players)
		&debuffsCollector{},
		&buildingsCollector{},   // objectives.buildingHealth (after vision)
		&neutralsCollector{},    // objectives.neutralCamps (fixed map spawners)
		&creepDamageCollector{}, // observed creep hits, not unreplicated attack targets
		&worldCollector{},       // timelines.dayNight (collect_world.go)
		&communicationCollector{},
		&economyCollector{},
		&runesCollector{},
		&abilityTargetsCollector{},
		&couriersCollector{},
		&mapObjectivesCollector{},
	}
}

// MatchAnalyzer reads a whole replay into the POST /v1/match document. The zero value is ready to use and safe for
// concurrent use: every call to Analyze makes its own session and collectors.
type MatchAnalyzer struct{}

// Analyze reads the whole replay from r (already decompressed: a plain .dem stream) and runs every collector. A
// replay manta cannot read, whose clock never starts, without 10 players, or that a collector refuses gives an error
// wrapping laning.ErrUnreadableReplay; a cancelled ctx stops the parse and gives ctx.Err().
func (MatchAnalyzer) Analyze(ctx context.Context, r io.Reader) (matchdata.Match, error) {
	return analyzeMatch(ctx, r, newCollectors())
}

func analyzeMatch(ctx context.Context, r io.Reader, collectors []Collector) (match matchdata.Match, err error) {
	if err := ctx.Err(); err != nil {
		return matchdata.Match{}, err
	}
	// manta turns panics inside Start into errors, but corrupt input can still panic elsewhere (e.g. while reading the
	// header), and so can a collector's Fill; a bad replay must never take the whole service down.
	defer func() {
		if recovered := recover(); recovered != nil {
			match, err = matchdata.Match{}, fmt.Errorf("%w: panic: %v", laning.ErrUnreadableReplay, recovered)
		}
	}()

	parser, err := manta.NewStreamParser(r)
	if err != nil {
		return matchdata.Match{}, unreadable(err)
	}
	session := newSession(ctx, parser)
	for _, collector := range collectors {
		collector.Register(session)
	}
	session.start()

	startErr := parser.Start()
	// Checked first: when ctx is cancelled the reader usually fails too, and that is not the replay's fault.
	if err := ctx.Err(); err != nil {
		return matchdata.Match{}, err
	}
	if startErr != nil {
		return matchdata.Match{}, unreadable(startErr)
	}
	if !session.Players.Known() {
		err := session.Players.learn() // why the last attempt failed
		return matchdata.Match{}, fmt.Errorf("%w: players not found: %v", laning.ErrUnreadableReplay, err)
	}
	if !session.Clock.Started() || !session.secondsStarted {
		return matchdata.Match{}, fmt.Errorf("%w: the game clock never started", laning.ErrUnreadableReplay)
	}
	session.end() // a replay that stops before the match ends still gets OnEnd

	match = matchdata.New()
	for _, collector := range collectors {
		if err := collector.Fill(&match); err != nil {
			return matchdata.Match{}, unreadable(fmt.Errorf("%T: %w", collector, err))
		}
	}
	return match, nil
}
