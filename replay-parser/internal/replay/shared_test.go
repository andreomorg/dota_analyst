package replay

import (
	"context"
	"errors"
	"fmt"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
	"io"
	"os"
	"testing"
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

// cancelAfter cancels a parse after the requested byte count.
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
