package replay

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"testing"
)

// A real replay exercises shared Manta callbacks and collector state under concurrent requests.
func TestConcurrentMatchIsolation(t *testing.T) {
	readers := []io.Reader{openTestReplay(t), openTestReplay(t)}
	type result struct {
		body []byte
		err  error
	}
	done := make(chan result, 2)
	for _, reader := range readers {
		go func(r io.Reader) {
			match, err := (MatchAnalyzer{}).Analyze(context.Background(), r)
			if err != nil {
				done <- result{err: err}
				return
			}
			body, err := json.Marshal(match)
			done <- result{body, err}
		}(reader)
	}
	first, second := <-done, <-done
	if first.err != nil || second.err != nil {
		t.Fatalf("parse errors: %v / %v", first.err, second.err)
	}
	if !bytes.Equal(first.body, second.body) {
		t.Fatal("concurrent independent parses produced different documents")
	}
	if peak, err := os.ReadFile("/sys/fs/cgroup/memory.peak"); err == nil {
		t.Logf("isolated concurrent-test cgroup peak bytes: %s", peak)
	}
}
