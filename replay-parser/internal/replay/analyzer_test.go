package replay

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

// openTestReplay opens the decompressed real replay of match 9034320024, or skips the test when the file is not on
// this machine (replays are not in Git: they are 30 MB and Valve deletes them after a few weeks).
func openTestReplay(t *testing.T) io.Reader {
	t.Helper()
	path := os.Getenv("REPLAY_TEST_FILE")
	if path == "" {
		path = "../../../work/replays/9034320024.dem.bz2"
	}
	file, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		t.Skipf("replay %s not found (set REPLAY_TEST_FILE to run this test)", path)
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

func TestAnalyzeRealReplay(t *testing.T) {
	replay := openTestReplay(t)

	started := time.Now()
	result, err := Analyzer{}.Analyze(context.Background(), replay)
	if err != nil {
		t.Fatal(err)
	}
	t.Logf("parsed in %s", time.Since(started).Round(time.Millisecond))
	for _, player := range result.Players {
		t.Logf("hero %3d radiant=%-5t steam %d lane %-7s lh %3d dn %2d net %5d xp %5d deaths %d", player.HeroID,
			player.IsRadiant, player.SteamID, player.Lane, player.LastHits, player.Denies, player.NetWorth, player.XP,
			player.Deaths)
	}

	if result.EndsAtSeconds != laning.EndsAtSeconds {
		t.Errorf("EndsAtSeconds = %d, want %d", result.EndsAtSeconds, laning.EndsAtSeconds)
	}
	if len(result.Players) != 10 {
		t.Fatalf("got %d players, want 10", len(result.Players))
	}
	byHero := map[int]laning.PlayerLaning{}
	for _, player := range result.Players {
		byHero[player.HeroID] = player
	}

	// Lanes as played in the match (card 01): Radiant safe lane is bottom, Dire safe lane is top.
	lanes := []struct {
		heroID    int
		isRadiant bool
		lane      laning.Lane
	}{
		{32, true, laning.Middle},   // Riki
		{29, true, laning.Top},      // Tidehunter
		{88, true, laning.Top},      // Nyx Assassin
		{17, true, laning.Bottom},   // Storm Spirit
		{31, true, laning.Bottom},   // Lich
		{67, false, laning.Top},     // Spectre
		{68, false, laning.Top},     // Ancient Apparition
		{101, false, laning.Middle}, // Skywrath Mage
		{28, false, laning.Bottom},  // Slardar
		{85, false, laning.Bottom},  // Undying
	}
	for _, want := range lanes {
		got, ok := byHero[want.heroID]
		if !ok {
			t.Errorf("hero %d not found", want.heroID)
			continue
		}
		if got.Lane != want.lane || got.IsRadiant != want.isRadiant {
			t.Errorf("hero %d: lane %q radiant %t, want lane %q radiant %t", want.heroID, got.Lane, got.IsRadiant,
				want.lane, want.isRadiant)
		}
	}

	// Numbers at 10:00 measured in card 01 (the cut is on the same tick, so they match exactly).
	numbers := []laning.PlayerLaning{
		{HeroID: 31, SteamID: 76561198059783443, IsRadiant: true, Lane: laning.Bottom,
			LastHits: 12, Denies: 9, NetWorth: 2047, XP: 2609, Deaths: 2},
		{HeroID: 32, SteamID: byHero[32].SteamID, IsRadiant: true, Lane: laning.Middle,
			LastHits: 36, Denies: 20, NetWorth: 3999, XP: 5891, Deaths: 1},
	}
	for _, want := range numbers {
		if got := byHero[want.HeroID]; got != want {
			t.Errorf("hero %d:\n got %+v\nwant %+v", want.HeroID, got, want)
		}
	}
}

func TestAnalyzeRealReplayCancelled(t *testing.T) {
	replay := openTestReplay(t)

	// Cancelled after the first megabytes, in the middle of the parse.
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	reader := &cancelAfter{reader: replay, bytes: 4 << 20, cancel: cancel}

	started := time.Now()
	_, err := Analyzer{}.Analyze(ctx, reader)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("err = %v, want context.Canceled", err)
	}
	if elapsed := time.Since(started); elapsed > 2*time.Second {
		t.Errorf("took %s to stop after the cancellation", elapsed)
	}
}

// cancelAfter cancels a context once bytes have been read, like a client giving up in the middle of the parse.
type cancelAfter struct {
	reader io.Reader
	bytes  int
	cancel context.CancelFunc
}

func (c *cancelAfter) Read(p []byte) (int, error) {
	n, err := c.reader.Read(p)
	if c.bytes -= n; c.bytes <= 0 {
		c.cancel()
	}
	return n, err
}

func TestAnalyzeAlreadyCancelled(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	_, err := Analyzer{}.Analyze(ctx, strings.NewReader("PBDEMS2\x00"))
	if !errors.Is(err, context.Canceled) {
		t.Errorf("err = %v, want context.Canceled", err)
	}
}

func TestAnalyzeUnreadableReplay(t *testing.T) {
	// A valid header followed by bytes that are not demo messages.
	garbage := append([]byte("PBDEMS2\x00\x00\x00\x00\x00\x00\x00\x00\x00"), bytes.Repeat([]byte{0xFF, 0x13, 0x07}, 1000)...)

	tests := []struct {
		name   string
		replay []byte
	}{
		{"empty", nil},
		{"not a replay", []byte("<html>Not Found</html>")},
		{"Source 1 replay", []byte("PUFDEMS\x00 and the rest")},
		{"header only", []byte("PBDEMS2\x00\x00\x00\x00\x00\x00\x00\x00\x00")},
		{"garbage after the header", garbage},
		{"truncated header", []byte("PBDE")},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, err := Analyzer{}.Analyze(context.Background(), bytes.NewReader(test.replay))
			if !errors.Is(err, laning.ErrUnreadableReplay) {
				t.Errorf("err = %v, want laning.ErrUnreadableReplay", err)
			}
		})
	}
}

// fakeTeamData is CDOTA_DataRadiant/Dire with the steam IDs of m_vecDataTeam.0000-0004.
type fakeTeamData [5]uint64

func (f fakeTeamData) GetUint64(name string) (uint64, bool) {
	for i, steamID := range f {
		if name == fmt.Sprintf("m_vecDataTeam.%04d.m_iPlayerSteamID", i) {
			return steamID, true
		}
	}
	return 0, false
}

func TestTeamDataIndex(t *testing.T) {
	team := fakeTeamData{101, 0, 103, 0, 105}
	tests := []struct {
		name      string
		steamID   uint64
		teamSlot  int
		wantIndex int
		wantOK    bool
	}{
		{"by steam ID", 105, 0, 4, true},
		{"steam ID wins over the team slot", 103, 4, 2, true},
		{"bot (steam ID 0) by team slot", 0, 3, 3, true},
		{"bot whose team slot holds a human's entry (caught by teamDataEntries)", 0, 0, 0, true},
		{"steam ID not in the team data is not found (no guess by team slot)", 999, 1, 0, false},
		{"steam ID not in the team data, team slot out of range", 999, 7, 0, false},
		{"team slot out of range", 0, 5, 0, false},
		{"negative team slot", 0, -1, 0, false},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			index, ok := teamDataIndex(team, test.steamID, test.teamSlot)
			if index != test.wantIndex || ok != test.wantOK {
				t.Errorf("teamDataIndex = %d, %t; want %d, %t", index, ok, test.wantIndex, test.wantOK)
			}
		})
	}
}

func TestTeamDataEntries(t *testing.T) {
	type claim struct{ team, index, slot int }
	tests := []struct {
		name    string
		claims  []claim
		wantErr bool
	}{
		{"every player has their own entry", []claim{{teamRadiant, 0, 0}, {teamRadiant, 1, 1}, {teamDire, 0, 5}, {teamDire, 1, 6}}, false},
		{"same index in different teams", []claim{{teamRadiant, 2, 0}, {teamDire, 2, 5}}, false},
		{"two players on the same entry", []claim{{teamRadiant, 0, 0}, {teamRadiant, 3, 1}, {teamRadiant, 0, 4}}, true},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			entries := teamDataEntries{}
			var err error
			for _, c := range test.claims {
				if err = entries.claim(c.team, c.index, c.slot); err != nil {
					break
				}
			}
			if (err != nil) != test.wantErr {
				t.Errorf("err = %v, want error %t", err, test.wantErr)
			}
		})
	}
}

func TestPauseTracker(t *testing.T) {
	type tick struct {
		paused      bool
		pausedTicks float64
		wantRunning bool
	}
	// waitedTooLong is the flag cleared with the counter never changing: the clock stops for maxPauseSettleTicks.
	waitedTooLong := []tick{{false, 0, true}, {true, 0, false}}
	for range maxPauseSettleTicks {
		waitedTooLong = append(waitedTooLong, tick{false, 0, false})
	}
	waitedTooLong = append(waitedTooLong, tick{false, 0, true}, tick{false, 0, true})

	tests := []struct {
		name  string
		ticks []tick
	}{
		{"never paused", []tick{{false, 0, true}, {false, 0, true}}},
		{"counter updated during the pause", []tick{{false, 0, true}, {true, 1, false}, {true, 2, false}, {false, 3, true}}},
		{"counter updated on the tick the pause ends", []tick{{false, 0, true}, {true, 0, false}, {true, 0, false}, {false, 90, true}}},
		{"counter updated a tick after the flag clears", []tick{{false, 0, true}, {true, 0, false}, {true, 0, false},
			{false, 0, false}, {false, 90, true}, {false, 90, true}}},
		{"second pause waits for the counter again", []tick{{false, 0, true}, {true, 0, false}, {false, 90, true},
			{true, 90, false}, {false, 90, false}, {false, 150, true}}},
		{"counter never changes", waitedTooLong},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			var tracker pauseTracker
			for i, tick := range test.ticks {
				if got := tracker.running(tick.paused, tick.pausedTicks); got != tick.wantRunning {
					t.Fatalf("tick %d (paused %t, paused ticks %g): running = %t, want %t", i, tick.paused,
						tick.pausedTicks, got, tick.wantRunning)
				}
			}
		})
	}
}
