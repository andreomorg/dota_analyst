package replay

import "testing"

// A synthetic match: the horn at server second 100, a 60 s pause from server second 400 (game clock 300).
const (
	pauseHorn  = 100.0
	pauseBegin = 400.0
	pauseLen   = 60.0
)

// pauseClock replays the ticks of the pause into a Clock. countedAtEnd: the counter only grows when the pause ends (as
// the replays do); otherwise it grows on every paused tick.
func pauseClock(countedAtEnd bool) *Clock {
	c := &Clock{game: gameClock{gameStart: pauseHorn}}
	for tick := 0; tick <= int((pauseBegin+pauseLen+5)*ticksPerSecond); tick++ {
		seconds := float64(tick) / ticksPerSecond
		paused := seconds >= pauseBegin && seconds < pauseBegin+pauseLen
		switch {
		case paused && !countedAtEnd:
			c.game.pausedTicks = (seconds - pauseBegin) * ticksPerSecond
		case !paused && seconds >= pauseBegin+pauseLen+1.0/ticksPerSecond:
			c.game.pausedTicks = pauseLen * ticksPerSecond
		}
		c.game.trackPause(seconds, paused)
	}
	return c
}

func TestClock_EventsDuringAPause_StayAtItsStart(t *testing.T) {
	for _, countedAtEnd := range []bool{true, false} {
		c := pauseClock(countedAtEnd)
		cases := []struct {
			name   string
			server float64
			want   float64
		}{
			{"before the pause", 350, 250},
			{"when it begins", pauseBegin, 300},
			{"in the middle", pauseBegin + 30, 300},
			{"just before it ends", pauseBegin + pauseLen - 0.1, 300},
			{"after it", pauseBegin + pauseLen + 10, 310},
		}
		for _, tc := range cases {
			// The counter stamped on the moment is ignored once pauses are known: it lagged during the pause.
			got := c.Game(ServerTime{Seconds: tc.server, PausedTicks: 0})
			if diff := got - tc.want; diff > 0.05 || diff < -0.05 {
				t.Errorf("countedAtEnd=%v %s: game clock %.2f, want %.2f", countedAtEnd, tc.name, got, tc.want)
			}
		}
	}
}

func TestClock_NeverGoesBack_AcrossAPause(t *testing.T) {
	c := pauseClock(true)
	previous := -1e9
	for server := 300.0; server <= pauseBegin+pauseLen+5; server += 0.1 {
		got := c.Game(ServerTime{Seconds: server})
		if got < previous-1e-9 {
			t.Fatalf("game clock went back at server %.1f: %.2f after %.2f", server, got, previous)
		}
		previous = got
	}
}

func TestClock_WithoutPauses_UsesTheStampedCounter(t *testing.T) {
	c := &Clock{game: gameClock{gameStart: pauseHorn}}
	if got := c.Game(ServerTime{Seconds: 500, PausedTicks: 60 * ticksPerSecond}); got != 340 {
		t.Fatalf("game clock %.2f, want 340", got)
	}
}

func TestClock_APauseTheCounterNeverCounts_ClosesAfterTheSettleWait(t *testing.T) {
	c := &Clock{game: gameClock{gameStart: pauseHorn}}
	for tick := 0; tick <= int(470*ticksPerSecond); tick++ {
		seconds := float64(tick) / ticksPerSecond
		c.game.trackPause(seconds, seconds >= pauseBegin && seconds < pauseBegin+pauseLen)
	}
	if span := c.game.pauses[0]; !span.closed || span.after != span.before {
		t.Fatalf("span %+v: want closed with no counted ticks", span)
	}
	// Nothing counted: moments after the pause keep running on the server clock (the old behavior).
	if got := c.Game(ServerTime{Seconds: 470}); got != 370 {
		t.Fatalf("game clock %.2f, want 370", got)
	}
}
