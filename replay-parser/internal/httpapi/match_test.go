package httpapi

import (
	"bytes"
	"compress/gzip"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

type fakeMatchAnalyzer struct {
	analyze func(ctx context.Context, r io.Reader) (matchdata.Match, error)
}

func (f fakeMatchAnalyzer) Analyze(ctx context.Context, r io.Reader) (matchdata.Match, error) {
	return f.analyze(ctx, r)
}

func matchReturning(match matchdata.Match, err error) fakeMatchAnalyzer {
	return fakeMatchAnalyzer{analyze: func(_ context.Context, r io.Reader) (matchdata.Match, error) {
		if _, readErr := io.ReadAll(r); readErr != nil {
			return matchdata.Match{}, fmt.Errorf("%w: %v", laning.ErrUnreadableReplay, readErr)
		}
		return match, err
	}}
}

func sampleMatch() matchdata.Match {
	match := matchdata.New()
	match.Match.MatchID = "9034320024"
	match.Match.GameMode = 22
	match.Match.DurationSeconds = 1725.53
	return match
}

func matchRequest(authorization, acceptEncoding string) *http.Request {
	request := httptest.NewRequest(http.MethodPost, "/v1/match", strings.NewReader(`{"replayUrl":"`+replayURL+`"}`))
	request.Header.Set("Content-Type", "application/json")
	if authorization != "" {
		request.Header.Set("Authorization", authorization)
	}
	if acceptEncoding != "" {
		request.Header.Set("Accept-Encoding", acceptEncoding)
	}
	return request
}

func newMatchHandler(source ReplaySource, analyzer MatchAnalyzer, config Config, logs io.Writer) http.Handler {
	return NewHandler(source, config, slog.New(slog.NewJSONHandler(logs, nil)),
		WithMatchAnalyzer(analyzer))
}

func TestMatch(t *testing.T) {
	bearer := "Bearer " + testKey
	tests := []struct {
		name          string
		authorization string
		source        fakeSource
		analyzer      fakeMatchAnalyzer
		wantStatus    int
	}{
		{"success", bearer, replayBytes(), matchReturning(sampleMatch(), nil), http.StatusOK},
		{"no token", "", replayBytes(), matchReturning(sampleMatch(), nil), http.StatusUnauthorized},
		{"wrong token", "Bearer wrong", replayBytes(), matchReturning(sampleMatch(), nil), http.StatusUnauthorized},
		{"URL outside *.valve.net", bearer, failingSource(fmt.Errorf("%w: host", valve.ErrInvalidURL)),
			matchReturning(sampleMatch(), nil), http.StatusBadRequest},
		{"replay gone from Valve", bearer, failingSource(valve.ErrReplayNotFound), matchReturning(sampleMatch(), nil),
			http.StatusNotFound},
		{"unreadable replay", bearer, replayBytes(), matchReturning(matchdata.Match{}, laning.ErrUnreadableReplay),
			http.StatusUnprocessableEntity},
		{"download dropped while parsing", bearer, streamFailingWith(valve.ErrDownloadFailed),
			matchReturning(sampleMatch(), nil), http.StatusBadGateway},
		{"unexpected analyzer error", bearer, replayBytes(), matchReturning(matchdata.Match{}, errors.New("boom")),
			http.StatusInternalServerError},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			response := httptest.NewRecorder()
			newMatchHandler(test.source, test.analyzer, Config{APIKey: testKey}, io.Discard).
				ServeHTTP(response, matchRequest(test.authorization, ""))
			if response.Code != test.wantStatus {
				t.Fatalf("status %d, want %d; body %s", response.Code, test.wantStatus, response.Body)
			}
			if test.wantStatus != http.StatusOK {
				assertProblem(t, response, test.wantStatus)
				return
			}
			if response.Header().Get("Content-Type") != "application/json" ||
				response.Header().Get("Content-Encoding") != "" || response.Header().Get("Vary") != "Accept-Encoding" {
				t.Errorf("headers %v", response.Header())
			}
			body := response.Body.String()
			for _, want := range []string{`{"schemaVersion":1,"match":{"matchId":"9034320024","gameMode":22,`,
				`"durationSeconds":1725.5,`, `"players":[]`, `"kills":[]`} {
				if !strings.Contains(body, want) {
					t.Errorf("body without %s: %s", want, body)
				}
			}
		})
	}
}

func TestMatch_Gzip(t *testing.T) {
	plain := httptest.NewRecorder()
	handler := newMatchHandler(replayBytes(), matchReturning(sampleMatch(), nil), Config{}, io.Discard)
	handler.ServeHTTP(plain, matchRequest("", ""))

	tests := []struct {
		acceptEncoding string
		wantGzip       bool
	}{
		{"gzip", true},
		{"gzip, deflate, br", true},
		{"br;q=1.0, GZIP;q=0.5", true},
		{"*", true},
		{"gzip;q=0", false},
		{"gzip;q=0, *", false},
		{"deflate, br", false},
		{"identity", false},
	}
	for _, test := range tests {
		t.Run(test.acceptEncoding, func(t *testing.T) {
			response := httptest.NewRecorder()
			handler.ServeHTTP(response, matchRequest("", test.acceptEncoding))
			if response.Code != http.StatusOK {
				t.Fatalf("status %d", response.Code)
			}
			gotGzip := response.Header().Get("Content-Encoding") == "gzip"
			if gotGzip != test.wantGzip {
				t.Fatalf("gzip %t, want %t", gotGzip, test.wantGzip)
			}
			body := response.Body.Bytes()
			if gotGzip {
				reader, err := gzip.NewReader(bytes.NewReader(body))
				if err != nil {
					t.Fatal(err)
				}
				if body, err = io.ReadAll(reader); err != nil {
					t.Fatal(err)
				}
			}
			if !bytes.Equal(body, plain.Body.Bytes()) {
				t.Errorf("body differs from the uncompressed one:\n%s\n%s", body, plain.Body)
			}
			if response.Header().Get("Content-Length") != fmt.Sprint(response.Body.Len()) {
				t.Errorf("Content-Length %s for %d bytes", response.Header().Get("Content-Length"), response.Body.Len())
			}
		})
	}
}

func TestMatch_ResponseOverTheLimitIs500(t *testing.T) {
	huge := sampleMatch()
	huge.Players = matchdata.List[matchdata.Player]{{Name: strings.Repeat("a", maxMatchResponseBytes)}}
	var logs bytes.Buffer
	response := httptest.NewRecorder()
	newMatchHandler(replayBytes(), matchReturning(huge, nil), Config{}, &logs).
		ServeHTTP(response, matchRequest("", "gzip"))

	if response.Code != http.StatusInternalServerError {
		t.Fatalf("status %d, want 500", response.Code)
	}
	assertProblem(t, response, http.StatusInternalServerError)
	if !strings.Contains(logs.String(), "over the size limit") || !strings.Contains(logs.String(), `"level":"ERROR"`) {
		t.Errorf("no error log about the size: %s", logs.String())
	}

	// Just under the limit is fine.
	small := sampleMatch()
	raw, _ := json.Marshal(small)
	small.Players = matchdata.List[matchdata.Player]{{}}
	withPlayer, _ := json.Marshal(small)
	small.Players[0].Name = strings.Repeat("a", maxMatchResponseBytes-len(withPlayer))
	ok := httptest.NewRecorder()
	newMatchHandler(replayBytes(), matchReturning(small, nil), Config{}, io.Discard).ServeHTTP(ok, matchRequest("", ""))
	if ok.Code != http.StatusOK || ok.Body.Len() != maxMatchResponseBytes {
		t.Errorf("status %d with %d bytes (empty match %d bytes), want 200 with exactly the limit", ok.Code,
			ok.Body.Len(), len(raw))
	}
}

func TestMatch_RouteNeedsTheOption(t *testing.T) {
	response := httptest.NewRecorder()
	NewHandler(replayBytes(), Config{}, slog.New(slog.NewJSONHandler(io.Discard, nil))).ServeHTTP(response, matchRequest("", ""))
	if response.Code != http.StatusNotFound {
		t.Fatalf("status %d, want 404 without WithMatchAnalyzer", response.Code)
	}

	get := httptest.NewRecorder()
	newMatchHandler(replayBytes(), matchReturning(sampleMatch(), nil), Config{}, io.Discard).
		ServeHTTP(get, httptest.NewRequest(http.MethodGet, "/v1/match", nil))
	if get.Code != http.StatusMethodNotAllowed {
		t.Fatalf("GET /v1/match: status %d, want 405", get.Code)
	}
}
