package replay

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"math"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
	"github.com/dotabuff/manta/dota"
)

// The real replays used by the /v1/match tests. They are not in Git (30 MB each, and Valve deletes them after a few
// weeks): each test is skipped when its file is missing.
var (
	// All Pick, 28:45, no pauses (the replay of the /v1/laning tests).
	allPickReplay = realReplay{env: "REPLAY_TEST_FILE", path: "../../../work/replays/9034320024.dem.bz2"}
	// Turbo, 23:40, parsed by OpenDota (radiant_win false, duration 1420).
	turboReplay = realReplay{env: "REPLAY_TEST_FILE_TURBO", path: "../../../work/replays/9041133074.dem"}
)

type realReplay struct{ env, path string }

// open returns the decompressed replay (.dem, .dem.bz2 in zstd or bzip2), or skips the test.
func (r realReplay) open(t *testing.T) io.Reader {
	t.Helper()
	path := os.Getenv(r.env)
	if path == "" {
		path = r.path
	}
	file, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		t.Skipf("replay %s not found (set %s to run this test)", path, r.env)
	}
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { file.Close() })
	replay, err := valve.Decompress(file)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { replay.Close() })
	return replay
}

func TestMatchAnalyzerRealReplays(t *testing.T) {
	radiant, dire := matchdata.Radiant, matchdata.Dire
	tests := []struct {
		name         string
		replay       realReplay
		matchID      string
		gameMode     int
		winner       *matchdata.Team
		duration     float64 // scoreboard duration (OpenDota)
		endedAt      int64
		bans         int
		wantMinBytes int
	}{
		{"All Pick 9034320024", allPickReplay, "9034320024", 22, &radiant, 1725.5, 1791425104, 17, 500},
		{"Turbo 9041133074", turboReplay, "9041133074", 23, &dire, 1420, 1791782581, 0, 500},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			r := test.replay.open(t)
			started := time.Now()
			match, err := MatchAnalyzer{}.Analyze(context.Background(), r)
			if err != nil {
				t.Fatal(err)
			}
			t.Logf("parsed in %s", time.Since(started).Round(time.Millisecond))

			info := match.Match
			if match.SchemaVersion != matchdata.SchemaVersion || info.MatchID != test.matchID || info.GameMode != test.gameMode {
				t.Errorf("schema %d, match %q, mode %d", match.SchemaVersion, info.MatchID, info.GameMode)
			}
			if (info.Winner == nil) != (test.winner == nil) || info.Winner != nil && *info.Winner != *test.winner {
				t.Errorf("winner %v, want %v", info.Winner, *test.winner)
			}
			if math.Abs(float64(info.DurationSeconds)-test.duration) > 1 {
				t.Errorf("duration %.1f, want %.1f (+-1)", info.DurationSeconds, test.duration)
			}
			if info.EndedAt == nil || *info.EndedAt != test.endedAt {
				t.Errorf("endedAt %v, want %d", info.EndedAt, test.endedAt)
			}
			if info.StartedAt == nil || math.Abs(float64(*info.EndedAt-*info.StartedAt)-test.duration) > 2 {
				t.Errorf("startedAt %v is not about duration before endedAt", info.StartedAt)
			}
			if info.PausedSeconds != 0 {
				t.Errorf("paused %.1f s, want 0 (no pauses in these replays)", info.PausedSeconds)
			}
			if len(match.Draft.AnnouncedBans) != test.bans {
				t.Errorf("%d bans, want %d: %v", len(match.Draft.AnnouncedBans), test.bans, match.Draft.AnnouncedBans)
			}

			raw, err := json.Marshal(match)
			if err != nil {
				t.Fatal(err)
			}
			if len(raw) < test.wantMinBytes || !json.Valid(raw) {
				t.Errorf("unexpected document: %s", raw)
			}
			t.Logf("JSON %d bytes", len(raw))
		})
	}
}

// sessionProbe is a collector that records what the session hands out, to test the session itself.
type sessionProbe struct {
	session *Session

	firstPurchase       ServerTime
	purchases           int
	knownAtPurchase     bool // players known on the first PURCHASE (pre-game)
	purchasePlayerFound bool // ...and the hero name resolved to a player
	startedAtPurchase   bool // the horn time was already known then (it must not be)
	knownSeen           bool
	firstKnownPurchase  ServerTime // first purchase with all 10 players known
	atSecondZero        ServerTime
	seconds             []int
	ends                []int
	endedAtEnd          bool
}

func (p *sessionProbe) Register(s *Session) {
	p.session = s
	s.OnCombatLog(func(entry CombatEntry) {
		if !p.knownSeen && s.Players.Known() {
			p.knownSeen, p.firstKnownPurchase = true, entry.At
		}
		if p.purchases++; p.purchases > 1 {
			return
		}
		p.firstPurchase = entry.At
		p.knownAtPurchase = s.Players.Known()
		_, p.purchasePlayerFound = s.Players.ByHeroName(entry.Target)
		p.startedAtPurchase = s.Clock.Started()
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE)
	s.OnSecond(func(second int) {
		if second == 0 {
			p.atSecondZero = s.Clock.Now()
		}
		p.seconds = append(p.seconds, second)
	})
	s.OnEnd(func(second int) {
		p.ends = append(p.ends, second)
		_, p.endedAtEnd = s.Clock.GameEnd()
	})
}

func (p *sessionProbe) Fill(*matchdata.Match) error { return nil }

func TestSessionOnRealReplay(t *testing.T) {
	r := allPickReplay.open(t)
	probe := &sessionProbe{}
	if _, err := analyzeMatch(context.Background(), r, []Collector{probe}); err != nil {
		t.Fatal(err)
	}
	clock := probe.session.Clock

	// The first purchases happen before the horn, when m_flGameStartTime is still 0: the time is kept as server time
	// and only converted at the end. OpenDota has the first purchases of this kind of match around -1:30..-1:00.
	if probe.purchases == 0 || probe.startedAtPurchase {
		t.Fatalf("purchases %d, horn known at the first one %t", probe.purchases, probe.startedAtPurchase)
	}
	if first := clock.Game(probe.firstPurchase); first < -95 || first > -30 {
		t.Errorf("first purchase at %.1f s of game clock, want before the horn (-95..-30)", first)
	}
	// In All Pick the first players buy while others are still picking: the identity of all 10 is not known yet,
	// but the buyer's hero name already resolves.
	t.Logf("players known at the first purchase: %t; first purchase with all known at %.1f s", probe.knownAtPurchase,
		clock.Game(probe.firstKnownPurchase))
	if !probe.purchasePlayerFound {
		t.Error("the hero of the first (pre-game) purchase was not resolved to a player")
	}
	// OnSecond(0) runs on the first tick of 0:00.
	if zero := clock.Game(probe.atSecondZero); zero < 0 || zero >= 0.1 {
		t.Errorf("second 0 sampled at %.3f s of game clock", zero)
	}
	// One call per second, from 0 to the end, in order.
	if len(probe.seconds) == 0 || probe.seconds[0] != 0 {
		t.Fatalf("seconds start at %v", probe.seconds[:min(3, len(probe.seconds))])
	}
	for i := 1; i < len(probe.seconds); i++ {
		if probe.seconds[i] != probe.seconds[i-1]+1 {
			t.Fatalf("second %d after %d", probe.seconds[i], probe.seconds[i-1])
		}
	}
	last := probe.seconds[len(probe.seconds)-1]
	if len(probe.ends) != 1 || probe.ends[0] != last || !probe.endedAtEnd {
		t.Errorf("OnEnd calls %v (want once, with the last second %d), game end known %t", probe.ends, last,
			probe.endedAtEnd)
	}
	if end, _ := clock.GameEnd(); math.Abs(clock.Game(end)-1725.5) > 0.5 || last != 1725 {
		t.Errorf("game end at %.2f s, last second %d; want 1725.5 and 1725", clock.Game(end), last)
	}
	players := probe.session.Players.All()
	if len(players) != 10 {
		t.Fatalf("%d players", len(players))
	}
	for i, player := range players {
		if player.Index != i || player.HeroID <= 0 || !strings.HasPrefix(player.HeroName, "npc_dota_hero_") ||
			player.Team == "" || player.Name == "" {
			t.Errorf("player %d: %+v", i, player)
		}
	}
	if lich, ok := probe.session.Players.BySteamID(76561198059783443); !ok || players[lich].HeroID != 31 {
		t.Errorf("the owner's account (Lich, hero 31) not found: %d %t", lich, ok)
	}
}

func TestClockConvertsServerTime(t *testing.T) {
	// m_flGameStartTime of match 9034320024: the horn at 262.47 s of server time.
	clock := &Clock{game: gameClock{gameStart: 262.47}}
	tests := []struct {
		name string
		at   ServerTime
		want float64
	}{
		{"captured before the horn (start time still 0 then)", ServerTime{Seconds: 172.47}, -90},
		{"the horn", ServerTime{Seconds: 262.47}, 0},
		{"after the horn", ServerTime{Seconds: 862.47}, 600},
		{"after a 60 s pause", ServerTime{Seconds: 922.47, PausedTicks: 60 * ticksPerSecond}, 600},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			if got := clock.Game(test.at); math.Abs(got-test.want) > 1e-9 {
				t.Errorf("Game = %v, want %v", got, test.want)
			}
		})
	}
	if !clock.Started() {
		t.Error("Started = false with a start time")
	}
	if (&Clock{}).Started() {
		t.Error("Started = true before the horn")
	}
	if _, ok := clock.GameEnd(); ok {
		t.Error("GameEnd known before the end")
	}
}

func TestMatchAnalyzerRealReplayCancelled(t *testing.T) {
	r := allPickReplay.open(t)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	started := time.Now()
	_, err := MatchAnalyzer{}.Analyze(ctx, &cancelAfter{reader: r, bytes: 4 << 20, cancel: cancel})
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("err = %v, want context.Canceled", err)
	}
	if elapsed := time.Since(started); elapsed > 2*time.Second {
		t.Errorf("took %s to stop after the cancellation", elapsed)
	}
}

func TestMatchAnalyzerAlreadyCancelled(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := (MatchAnalyzer{}).Analyze(ctx, strings.NewReader("PBDEMS2\x00")); !errors.Is(err, context.Canceled) {
		t.Errorf("err = %v, want context.Canceled", err)
	}
}

func TestMatchAnalyzerUnreadableReplay(t *testing.T) {
	garbage := append([]byte("PBDEMS2\x00\x00\x00\x00\x00\x00\x00\x00\x00"), bytes.Repeat([]byte{0xFF, 0x13, 0x07}, 1000)...)
	tests := []struct {
		name   string
		replay []byte
	}{
		{"empty", nil},
		{"not a replay", []byte("<html>Not Found</html>")},
		{"header only", []byte("PBDEMS2\x00\x00\x00\x00\x00\x00\x00\x00\x00")},
		{"garbage after the header", garbage},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, err := MatchAnalyzer{}.Analyze(context.Background(), bytes.NewReader(test.replay))
			if !errors.Is(err, laning.ErrUnreadableReplay) {
				t.Errorf("err = %v, want laning.ErrUnreadableReplay", err)
			}
		})
	}
}

// failingCollector fails (or panics) in Fill.
type failingCollector struct{ panics bool }

func (failingCollector) Register(*Session) {}

func (c failingCollector) Fill(*matchdata.Match) error {
	if c.panics {
		panic("index out of range")
	}
	return errors.New("two players on the same hero")
}

func TestMatchAnalyzerCollectorErrorIsUnreadable(t *testing.T) {
	for _, collector := range []failingCollector{{panics: false}, {panics: true}} {
		r := allPickReplay.open(t)
		_, err := analyzeMatch(context.Background(), r, []Collector{&matchCollector{}, collector})
		if !errors.Is(err, laning.ErrUnreadableReplay) {
			t.Errorf("panics %t: err = %v, want laning.ErrUnreadableReplay", collector.panics, err)
		}
	}
}
