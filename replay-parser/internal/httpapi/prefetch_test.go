package httpapi

import (
	"context"
	"errors"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/parsing"
	"io"
	"log/slog"
	"net/http/httptest"
	"strings"
	"testing"
)

type fakePrefetch struct{ err error }

func (f fakePrefetch) Prefetch(context.Context, string) (int64, error) { return 123, f.err }
func TestPrefetchContract(t *testing.T) {
	for _, tt := range []struct {
		name, body, key string
		err             error
		status          int
	}{
		{"auth", `{"replayUrl":"x"}`, "", nil, 401},
		{"success", `{"replayUrl":"x"}`, testKey, nil, 200},
		{"missing", `{}`, testKey, nil, 400},
		{"json", `!`, testKey, nil, 400},
		{"invalid", `{"replayUrl":"x"}`, testKey, parsing.ErrInvalidURL, 400},
		{"gone", `{"replayUrl":"x"}`, testKey, parsing.ErrReplayNotFound, 404},
		{"download", `{"replayUrl":"x"}`, testKey, parsing.ErrDownloadFailed, 502},
		{"busy", `{"replayUrl":"x"}`, testKey, parsing.ErrBusy, 503},
		{"large", `{"replayUrl":"x"}`, testKey, parsing.ErrTooLarge, 422},
	} {
		t.Run(tt.name, func(t *testing.T) {
			h := NewHandler(replayBytes(), analyzerReturning(lichResult, nil), Config{APIKey: testKey}, slog.New(slog.NewTextHandler(io.Discard, nil)), WithPrefetcher(fakePrefetch{tt.err}))
			r := httptest.NewRequest("POST", "/v1/prefetch", strings.NewReader(tt.body))
			if tt.key != "" {
				r.Header.Set("Authorization", "Bearer "+tt.key)
			}
			w := httptest.NewRecorder()
			h.ServeHTTP(w, r)
			if w.Code != tt.status {
				t.Fatalf("%d: %s", w.Code, w.Body.String())
			}
			if tt.status == 200 && strings.TrimSpace(w.Body.String()) != `{"cached":true,"bytes":123}` {
				t.Fatal(w.Body.String())
			}
			if errors.Is(tt.err, parsing.ErrBusy) && w.Header().Get("Retry-After") != "15" {
				t.Fatal("missing retry")
			}
			if tt.status != 200 && w.Header().Get("Content-Type") != "application/problem+json" {
				t.Fatal("problem content type")
			}
		})
	}
}
func TestPrefetchDisabledRoute(t *testing.T) {
	h := NewHandler(replayBytes(), analyzerReturning(lichResult, nil), Config{}, slog.New(slog.NewTextHandler(io.Discard, nil)))
	w := httptest.NewRecorder()
	h.ServeHTTP(w, httptest.NewRequest("POST", "/v1/prefetch", strings.NewReader(`{}`)))
	if w.Code != 404 {
		t.Fatal(w.Code)
	}
}
