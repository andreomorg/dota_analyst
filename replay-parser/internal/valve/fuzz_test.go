package valve

import "testing"

// URL parsing has no network or replay allocation; it is safe to fuzz in the CPU CI job.
func FuzzReplayURL(f *testing.F) {
	for _, raw := range []string{"", "https://replay1.valve.net/570/1_2.dem.bz2", "http://127.0.0.1/570/1.dem.bz2", "https://valve.net.evil.com/570/1.dem.bz2"} {
		f.Add(raw)
	}
	f.Fuzz(func(t *testing.T, raw string) {
		if len(raw) > 4096 {
			return
		}
		d := NewDownloader(Limits{})
		u, err := d.parseURL(raw)
		if err != nil {
			return
		}
		if u.User != nil || u.Port() != "" || u.RawQuery != "" || u.Fragment != "" || !isValveHost(u) || !replayPath.MatchString(u.EscapedPath()) {
			t.Fatalf("unsafe URL accepted: %q", raw)
		}
	})
}
