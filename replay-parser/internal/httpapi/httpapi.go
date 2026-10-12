// Package httpapi is the HTTP face of the service: POST /v1/laning and GET /health, the bearer token, the limit of
// parses running at once and the translation of errors into ProblemDetails (application/problem+json).
package httpapi

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

// Analyzer reads a decompressed replay and returns its laning phase (implemented by replay.Analyzer).
type Analyzer interface {
	Analyze(ctx context.Context, r io.Reader) (laning.Result, error)
}

// ReplaySource opens a replay by its URL and returns it decompressed (implemented by valve.Downloader).
type ReplaySource interface {
	Open(ctx context.Context, replayURL string) (io.ReadCloser, error)
}

// Config tunes the handler. Zero values mean the defaults.
type Config struct {
	// APIKey is the bearer token POST /v1/laning requires. Empty disables authentication (local `go run` only).
	APIKey string
	// MaxConcurrentParses is how many replays are downloaded and parsed at once; more requests get 503.
	MaxConcurrentParses int
	// RequestTimeout bounds download plus parse of one request.
	RequestTimeout time.Duration
}

const (
	DefaultMaxConcurrentParses = 1
	DefaultRequestTimeout      = 3 * time.Minute

	// The body only carries a URL; anything bigger is not a valid request.
	maxRequestBodyBytes = 4 << 10
	// A laning parse takes seconds (download ~30 MB + ~0.5 s of parsing), so the caller can come back soon.
	retryAfterSeconds = 15
)

type server struct {
	source      ReplaySource
	analyzer    Analyzer
	authEnabled bool
	// The key is kept as a SHA-256 hash so the comparison has a fixed length and does not leak the key's length.
	apiKeyHash [sha256.Size]byte
	slots      chan struct{}
	timeout    time.Duration
	logger     *slog.Logger
}

// NewHandler returns the service's HTTP handler.
func NewHandler(source ReplaySource, analyzer Analyzer, config Config, logger *slog.Logger) http.Handler {
	if config.MaxConcurrentParses <= 0 {
		config.MaxConcurrentParses = DefaultMaxConcurrentParses
	}
	if config.RequestTimeout <= 0 {
		config.RequestTimeout = DefaultRequestTimeout
	}
	s := &server{
		source:      source,
		analyzer:    analyzer,
		authEnabled: config.APIKey != "",
		apiKeyHash:  sha256.Sum256([]byte(config.APIKey)),
		slots:       make(chan struct{}, config.MaxConcurrentParses),
		timeout:     config.RequestTimeout,
		logger:      logger,
	}

	// Unknown routes keep the mux's plain-text 404/405 on purpose: the API client treats only problem+json as an
	// answer from this service, so a wrong base URL is never mistaken for "replay not found".
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", s.health)
	mux.Handle("POST /v1/laning", s.requireAPIKey(http.HandlerFunc(s.laning)))
	return s.logRequests(s.recoverPanics(mux))
}

func (s *server) health(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "healthy"})
}

type laningRequest struct {
	ReplayURL string `json:"replayUrl"`
}

func (s *server) laning(w http.ResponseWriter, r *http.Request) {
	var request laningRequest
	if err := decodeJSON(w, r, &request); err != nil {
		writeProblem(w, http.StatusBadRequest, "Invalid request body", err.Error())
		return
	}
	if strings.TrimSpace(request.ReplayURL) == "" {
		writeProblem(w, http.StatusBadRequest, "Invalid request body", "replayUrl is required")
		return
	}

	// Taken without waiting: a caller that waited here could hold its HTTP request open for minutes; 503 lets it
	// retry later instead.
	select {
	case s.slots <- struct{}{}:
		defer func() { <-s.slots }()
	default:
		w.Header().Set("Retry-After", strconv.Itoa(retryAfterSeconds))
		writeProblem(w, http.StatusServiceUnavailable, "Busy",
			"the maximum number of replays is already being parsed; try again later")
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), s.timeout)
	defer cancel()
	started := time.Now()
	result, err := s.analyze(ctx, request.ReplayURL)
	logger := s.logger.With("replayUrl", request.ReplayURL, "elapsedMs", time.Since(started).Milliseconds())
	if err != nil {
		s.writeAnalyzeError(ctx, w, r, logger, err)
		return
	}
	logger.Info("replay analyzed", "players", len(result.Players))
	writeJSON(w, http.StatusOK, newLaningResponse(result))
}

// analyze downloads and parses the replay. When reading the replay itself failed (network, size limit), that error
// wins over the analyzer's: the analyzer may only see "unreadable replay", but the cause is the download.
func (s *server) analyze(ctx context.Context, replayURL string) (laning.Result, error) {
	stream, err := s.source.Open(ctx, replayURL)
	if err != nil {
		return laning.Result{}, err
	}
	defer stream.Close()

	tracked := &readErrorTracker{r: stream}
	result, err := s.analyzer.Analyze(ctx, tracked)
	if err != nil && tracked.err != nil {
		if status, _ := classify(tracked.err); status != http.StatusInternalServerError {
			err = tracked.err
		}
	}
	return result, err
}

func (s *server) writeAnalyzeError(ctx context.Context, w http.ResponseWriter, r *http.Request, logger *slog.Logger,
	err error) {
	if r.Context().Err() != nil {
		// The caller gave up (or the server is shutting down): nobody is left to read an answer.
		logger.Info("caller went away before the replay was analyzed", "error", err)
		return
	}
	if errors.Is(ctx.Err(), context.DeadlineExceeded) {
		err = fmt.Errorf("%w: gave up after %s: %w", errTimedOut, s.timeout, err)
	}
	status, title := classify(err)
	detail := err.Error()
	if status == http.StatusInternalServerError {
		logger.Error("unexpected error analyzing replay", "error", err)
		detail = "unexpected error analyzing the replay"
	} else {
		logger.Warn("replay not analyzed", "status", status, "error", err)
	}
	writeProblem(w, status, title, detail)
}

var errTimedOut = errors.New("timed out")

// classify maps an error to its HTTP status and ProblemDetails title.
func classify(err error) (int, string) {
	switch {
	case errors.Is(err, valve.ErrInvalidURL):
		return http.StatusBadRequest, "Invalid replay URL"
	case errors.Is(err, valve.ErrReplayNotFound):
		return http.StatusNotFound, "Replay not found"
	// Size limits are a property of the replay: retrying cannot help, so it is "unreadable" (422), not 502.
	case errors.Is(err, valve.ErrTooLarge):
		return http.StatusUnprocessableEntity, "Replay too large"
	case errors.Is(err, laning.ErrUnreadableReplay):
		return http.StatusUnprocessableEntity, "Unreadable replay"
	case errors.Is(err, errTimedOut):
		return http.StatusBadGateway, "Replay download timed out"
	case errors.Is(err, valve.ErrDownloadFailed):
		return http.StatusBadGateway, "Replay download failed"
	default:
		return http.StatusInternalServerError, "Unexpected error"
	}
}

// readErrorTracker remembers the first error (other than EOF) the replay stream returned.
type readErrorTracker struct {
	r   io.Reader
	err error
}

func (t *readErrorTracker) Read(p []byte) (int, error) {
	n, err := t.r.Read(p)
	if err != nil && err != io.EOF && t.err == nil {
		t.err = err
	}
	return n, err
}

// decodeJSON reads one JSON object from a size-limited body. Unknown fields are ignored (like ASP.NET's default), so
// the API can add optional fields without breaking older versions of this service.
func decodeJSON(w http.ResponseWriter, r *http.Request, target any) error {
	decoder := json.NewDecoder(http.MaxBytesReader(w, r.Body, maxRequestBodyBytes))
	if err := decoder.Decode(target); err != nil {
		var tooLarge *http.MaxBytesError
		switch {
		case errors.As(err, &tooLarge):
			return fmt.Errorf("body larger than %d bytes", maxRequestBodyBytes)
		case errors.Is(err, io.EOF):
			return errors.New("body is empty")
		default:
			return fmt.Errorf("body is not valid JSON: %v", err)
		}
	}
	if _, err := decoder.Token(); !errors.Is(err, io.EOF) {
		return errors.New("body must contain a single JSON object")
	}
	return nil
}

func (s *server) requireAPIKey(next http.Handler) http.Handler {
	if !s.authEnabled {
		return next
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		token, ok := bearerToken(r.Header.Get("Authorization"))
		tokenHash := sha256.Sum256([]byte(token))
		// Constant time, so the response time does not reveal how much of the token matched.
		if subtle.ConstantTimeCompare(tokenHash[:], s.apiKeyHash[:]) != 1 || !ok {
			w.Header().Set("WWW-Authenticate", "Bearer")
			writeProblem(w, http.StatusUnauthorized, "Unauthorized", "missing or invalid bearer token")
			return
		}
		next.ServeHTTP(w, r)
	})
}

func bearerToken(header string) (string, bool) {
	scheme, token, found := strings.Cut(header, " ")
	if !found || !strings.EqualFold(scheme, "Bearer") || token == "" {
		return "", false
	}
	return token, true
}

// Same "type" links ASP.NET uses in its ProblemDetails, so both services answer errors alike.
var problemTypes = map[int]string{
	http.StatusBadRequest:          "https://tools.ietf.org/html/rfc9110#section-15.5.1",
	http.StatusUnauthorized:        "https://tools.ietf.org/html/rfc9110#section-15.5.2",
	http.StatusNotFound:            "https://tools.ietf.org/html/rfc9110#section-15.5.5",
	http.StatusUnprocessableEntity: "https://tools.ietf.org/html/rfc9110#section-15.5.21",
	http.StatusInternalServerError: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
	http.StatusBadGateway:          "https://tools.ietf.org/html/rfc9110#section-15.6.3",
	http.StatusServiceUnavailable:  "https://tools.ietf.org/html/rfc9110#section-15.6.4",
}

type problemDetails struct {
	Type   string `json:"type"`
	Title  string `json:"title"`
	Status int    `json:"status"`
	Detail string `json:"detail"`
}

func writeProblem(w http.ResponseWriter, status int, title, detail string) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(problemDetails{Type: problemTypes[status], Title: title, Status: status, Detail: detail})
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(body)
}
