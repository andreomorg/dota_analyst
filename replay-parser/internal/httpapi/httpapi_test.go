package httpapi

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

const (
	testKey   = "test-key"
	replayURL = "http://replay202.valve.net/570/9034320024_96247495.dem.bz2"
)

// fakeSource and fakeAnalyzer stand in for valve.Downloader and replay.MatchAnalyzer.
type fakeSource struct {
	open func(ctx context.Context, replayURL string) (io.ReadCloser, error)
}

func (f fakeSource) Open(ctx context.Context, replayURL string) (io.ReadCloser, error) {
	return f.open(ctx, replayURL)
}

type fakeAnalyzer struct {
	analyze func(ctx context.Context, r io.Reader) (matchdata.Match, error)
}

func (f fakeAnalyzer) Analyze(ctx context.Context, r io.Reader) (matchdata.Match, error) {
	return f.analyze(ctx, r)
}

// trackedStream reports whether the handler closed it.
type trackedStream struct {
	io.Reader
	closed bool
}

func (s *trackedStream) Close() error { s.closed = true; return nil }

func replayBytes() fakeSource {
	return fakeSource{open: func(context.Context, string) (io.ReadCloser, error) {
		return &trackedStream{Reader: strings.NewReader("replay")}, nil
	}}
}

func failingSource(err error) fakeSource {
	return fakeSource{open: func(context.Context, string) (io.ReadCloser, error) { return nil, err }}
}

// A stream that fails while being read, like a download dropped halfway.
func streamFailingWith(err error) fakeSource {
	return fakeSource{open: func(context.Context, string) (io.ReadCloser, error) {
		return &trackedStream{Reader: io.MultiReader(strings.NewReader("rep"), errReader{err})}, nil
	}}
}

type errReader struct{ err error }

func (r errReader) Read([]byte) (int, error) { return 0, r.err }

var lichResult = sampleMatch()

func analyzerReturning(result matchdata.Match, err error) fakeAnalyzer {
	return fakeAnalyzer{analyze: func(_ context.Context, r io.Reader) (matchdata.Match, error) {
		if _, readErr := io.ReadAll(r); readErr != nil {
			// What a parser typically does with a broken stream.
			return matchdata.Match{}, fmt.Errorf("%w: %v", laning.ErrUnreadableReplay, readErr)
		}
		return result, err
	}}
}

func newTestHandler(source ReplaySource, analyzer MatchAnalyzer, config Config) http.Handler {
	return NewHandler(source, config, slog.New(slog.NewJSONHandler(io.Discard, nil)), WithMatchAnalyzer(analyzer))
}

func replayRequestFor(body, authorization string) *http.Request {
	request := httptest.NewRequest(http.MethodPost, "/v1/match", strings.NewReader(body))
	request.Header.Set("Content-Type", "application/json")
	if authorization != "" {
		request.Header.Set("Authorization", authorization)
	}
	return request
}

func TestReplay(t *testing.T) {
	validBody := `{"replayUrl":"` + replayURL + `"}`
	bearer := "Bearer " + testKey

	tests := []struct {
		name          string
		apiKey        string // empty = testKey
		authDisabled  bool
		body          string
		authorization string
		source        fakeSource
		analyzer      fakeAnalyzer
		wantStatus    int
	}{
		{
			name: "success", body: validBody, authorization: bearer,
			source: replayBytes(), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusOK,
		},
		{
			name: "unknown fields are ignored", body: `{"replayUrl":"` + replayURL + `","matchId":1}`, authorization: bearer,
			source: replayBytes(), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusOK,
		},
		{
			name: "authentication disabled", authDisabled: true, body: validBody,
			source: replayBytes(), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusOK,
		},
		{
			name: "lowercase bearer scheme", body: validBody, authorization: "bearer " + testKey,
			source: replayBytes(), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusOK,
		},

		{name: "no token", body: validBody, wantStatus: http.StatusUnauthorized},
		{name: "wrong token", body: validBody, authorization: "Bearer wrong", wantStatus: http.StatusUnauthorized},
		{name: "token with a suffix", body: validBody, authorization: bearer + "x", wantStatus: http.StatusUnauthorized},
		{name: "empty bearer", body: validBody, authorization: "Bearer ", wantStatus: http.StatusUnauthorized},
		{name: "basic scheme", body: validBody, authorization: "Basic " + testKey, wantStatus: http.StatusUnauthorized},
		{name: "token alone", body: validBody, authorization: testKey, wantStatus: http.StatusUnauthorized},
		{name: "401 comes before body validation", body: "not json", wantStatus: http.StatusUnauthorized},

		{name: "empty body", body: "", authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "invalid JSON", body: "{", authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "not an object", body: `"x"`, authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "missing replayUrl", body: `{}`, authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "blank replayUrl", body: `{"replayUrl":"  "}`, authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "replayUrl not a string", body: `{"replayUrl":1}`, authorization: bearer, wantStatus: http.StatusBadRequest},
		{name: "two objects", body: validBody + validBody, authorization: bearer, wantStatus: http.StatusBadRequest},
		{
			name: "body too large", body: `{"replayUrl":"` + strings.Repeat("a", 5000) + `"}`, authorization: bearer,
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "URL outside *.valve.net", body: validBody, authorization: bearer,
			source:     failingSource(fmt.Errorf("%w: host must be *.valve.net", valve.ErrInvalidURL)),
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "replay gone from Valve", body: validBody, authorization: bearer,
			source: failingSource(fmt.Errorf("%w: status 404", valve.ErrReplayNotFound)), wantStatus: http.StatusNotFound,
		},
		{
			name: "download failed", body: validBody, authorization: bearer,
			source: failingSource(fmt.Errorf("%w: status 503", valve.ErrDownloadFailed)), wantStatus: http.StatusBadGateway,
		},
		{
			name: "replay over the size limit", body: validBody, authorization: bearer,
			source: failingSource(valve.ErrTooLarge), wantStatus: http.StatusUnprocessableEntity,
		},
		{
			name: "unreadable replay", body: validBody, authorization: bearer,
			source: replayBytes(), analyzer: analyzerReturning(matchdata.Match{}, laning.ErrUnreadableReplay),
			wantStatus: http.StatusUnprocessableEntity,
		},
		{
			name: "download dropped while parsing is a 502, not an unreadable replay", body: validBody, authorization: bearer,
			source:   streamFailingWith(fmt.Errorf("%w: connection reset", valve.ErrDownloadFailed)),
			analyzer: analyzerReturning(lichResult, nil), wantStatus: http.StatusBadGateway,
		},
		{
			name: "decompressed size over the limit while parsing", body: validBody, authorization: bearer,
			source: streamFailingWith(valve.ErrTooLarge), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusUnprocessableEntity,
		},
		{
			name: "corrupt compressed data is an unreadable replay", body: validBody, authorization: bearer,
			source: streamFailingWith(errors.New("zstd: corrupt block")), analyzer: analyzerReturning(lichResult, nil),
			wantStatus: http.StatusUnprocessableEntity,
		},
		{
			name: "unexpected analyzer error", body: validBody, authorization: bearer,
			source: replayBytes(), analyzer: analyzerReturning(matchdata.Match{}, errors.New("boom")),
			wantStatus: http.StatusInternalServerError,
		},
		{
			name: "analyzer panic", body: validBody, authorization: bearer, source: replayBytes(),
			analyzer: fakeAnalyzer{analyze: func(context.Context, io.Reader) (matchdata.Match, error) {
				panic("index out of range")
			}},
			wantStatus: http.StatusInternalServerError,
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			config := Config{APIKey: testKey}
			if test.authDisabled {
				config.APIKey = ""
			}
			var opened *trackedStream
			source := test.source
			if source.open != nil {
				inner := source.open
				source.open = func(ctx context.Context, url string) (io.ReadCloser, error) {
					stream, err := inner(ctx, url)
					if s, ok := stream.(*trackedStream); ok {
						opened = s
					}
					return stream, err
				}
			}

			response := httptest.NewRecorder()
			newTestHandler(source, test.analyzer, config).ServeHTTP(response, replayRequestFor(test.body, test.authorization))

			if response.Code != test.wantStatus {
				t.Fatalf("status %d, want %d; body %s", response.Code, test.wantStatus, response.Body)
			}
			if opened != nil && !opened.closed {
				t.Error("the replay stream was not closed")
			}
			if test.wantStatus == http.StatusOK {
				if got := response.Header().Get("Content-Type"); got != "application/json" {
					t.Errorf("Content-Type %q", got)
				}
				return
			}
			assertProblem(t, response, test.wantStatus)
			if test.wantStatus == http.StatusUnauthorized && response.Header().Get("WWW-Authenticate") != "Bearer" {
				t.Error("401 without WWW-Authenticate: Bearer")
			}
			if test.wantStatus == http.StatusInternalServerError && strings.Contains(response.Body.String(), "boom") {
				t.Error("500 leaked the internal error")
			}
		})
	}
}

func assertProblem(t *testing.T, response *httptest.ResponseRecorder, status int) {
	t.Helper()
	if got := response.Header().Get("Content-Type"); got != "application/problem+json" {
		t.Errorf("Content-Type %q, want application/problem+json", got)
	}
	var problem problemDetails
	if err := json.Unmarshal(response.Body.Bytes(), &problem); err != nil {
		t.Fatalf("body is not ProblemDetails: %v; %s", err, response.Body)
	}
	if problem.Status != status || problem.Title == "" || problem.Type == "" || problem.Detail == "" {
		t.Errorf("incomplete ProblemDetails: %+v", problem)
	}
}

func TestReplay_BusyAnswers503WithRetryAfter(t *testing.T) {
	started, release := make(chan struct{}), make(chan struct{})
	var once sync.Once
	analyzer := fakeAnalyzer{analyze: func(context.Context, io.Reader) (matchdata.Match, error) {
		once.Do(func() { close(started) })
		<-release
		return lichResult, nil
	}}
	handler := newTestHandler(replayBytes(), analyzer, Config{APIKey: testKey, MaxConcurrentParses: 1})
	body := `{"replayUrl":"` + replayURL + `"}`

	first := httptest.NewRecorder()
	done := make(chan struct{})
	go func() {
		handler.ServeHTTP(first, replayRequestFor(body, "Bearer "+testKey))
		close(done)
	}()
	<-started

	busy := httptest.NewRecorder()
	handler.ServeHTTP(busy, replayRequestFor(body, "Bearer "+testKey))
	if busy.Code != http.StatusServiceUnavailable {
		t.Fatalf("status %d, want 503", busy.Code)
	}
	assertProblem(t, busy, http.StatusServiceUnavailable)
	if busy.Header().Get("Retry-After") == "" {
		t.Error("503 without Retry-After")
	}

	close(release)
	<-done
	if first.Code != http.StatusOK {
		t.Fatalf("first request: status %d", first.Code)
	}
	// The slot is free again.
	again := httptest.NewRecorder()
	handler.ServeHTTP(again, replayRequestFor(body, "Bearer "+testKey))
	if again.Code != http.StatusOK {
		t.Fatalf("after the first finished: status %d", again.Code)
	}
}

func TestReplay_TimeoutAnswers502(t *testing.T) {
	var sawDeadline bool
	analyzer := fakeAnalyzer{analyze: func(ctx context.Context, _ io.Reader) (matchdata.Match, error) {
		_, sawDeadline = ctx.Deadline()
		<-ctx.Done()
		return matchdata.Match{}, ctx.Err()
	}}
	handler := newTestHandler(replayBytes(), analyzer, Config{APIKey: testKey, RequestTimeout: 20 * time.Millisecond})

	response := httptest.NewRecorder()
	handler.ServeHTTP(response, replayRequestFor(`{"replayUrl":"`+replayURL+`"}`, "Bearer "+testKey))
	if response.Code != http.StatusBadGateway {
		t.Fatalf("status %d, want 502; %s", response.Code, response.Body)
	}
	assertProblem(t, response, http.StatusBadGateway)
	if !sawDeadline {
		t.Error("the analyzer's context had no deadline")
	}
}

func TestReplay_ReceivesTheReplayURL(t *testing.T) {
	var got string
	source := fakeSource{open: func(_ context.Context, url string) (io.ReadCloser, error) {
		got = url
		return io.NopCloser(bytes.NewReader(nil)), nil
	}}
	handler := newTestHandler(source, analyzerReturning(lichResult, nil), Config{})
	handler.ServeHTTP(httptest.NewRecorder(), replayRequestFor(`{"replayUrl":"`+replayURL+`"}`, ""))
	if got != replayURL {
		t.Fatalf("source got %q", got)
	}
}

func TestRoutes(t *testing.T) {
	tests := []struct {
		method, path string
		wantStatus   int
	}{
		{http.MethodGet, "/health", http.StatusOK},
		{http.MethodGet, "/v1/match", http.StatusMethodNotAllowed},
		{http.MethodPost, "/v1/laning", http.StatusNotFound},
		{http.MethodPost, "/health", http.StatusMethodNotAllowed},
		{http.MethodGet, "/", http.StatusNotFound},
		{http.MethodPost, "/v2/laning", http.StatusNotFound},
	}
	handler := newTestHandler(replayBytes(), analyzerReturning(lichResult, nil), Config{APIKey: testKey})
	for _, test := range tests {
		t.Run(test.method+" "+test.path, func(t *testing.T) {
			response := httptest.NewRecorder()
			handler.ServeHTTP(response, httptest.NewRequest(test.method, test.path, nil))
			if response.Code != test.wantStatus {
				t.Fatalf("status %d, want %d", response.Code, test.wantStatus)
			}
			// Only answers about a replay are problem+json (see NewHandler).
			if test.wantStatus != http.StatusOK && response.Header().Get("Content-Type") == "application/problem+json" {
				t.Error("routing errors must not be problem+json")
			}
		})
	}
}

func TestLogsNeverContainTheToken(t *testing.T) {
	var logs bytes.Buffer
	handler := NewHandler(replayBytes(), Config{APIKey: testKey},
		slog.New(slog.NewJSONHandler(&logs, nil)), WithMatchAnalyzer(matchReturning(sampleMatch(), nil)))
	for _, authorization := range []string{"Bearer " + testKey, "Bearer wrong-" + testKey} {
		handler.ServeHTTP(httptest.NewRecorder(), replayRequestFor(`{"replayUrl":"`+replayURL+`"}`, authorization))
	}
	if strings.Contains(logs.String(), testKey) {
		t.Fatalf("token in the logs: %s", logs.String())
	}
	if !strings.Contains(logs.String(), `"status":401`) || !strings.Contains(logs.String(), `"status":200`) {
		t.Fatalf("missing access log lines: %s", logs.String())
	}
}
