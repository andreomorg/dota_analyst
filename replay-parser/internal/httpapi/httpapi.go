// Package httpapi is the HTTP face of the service: POST /v1/laning, POST /v1/match and GET /health, the bearer token,
// the limit of parses running at once and the translation of errors into ProblemDetails (application/problem+json).
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
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/parsing"
)

// Analyzer reads a decompressed replay and returns its laning phase (implemented by replay.Analyzer).
type Analyzer interface {
	Analyze(ctx context.Context, r io.Reader) (laning.Result, error)
}

// ReplaySource is kept as a compatibility name for handler construction.
type ReplaySource = parsing.ReplaySource

// Config tunes the handler. Zero values mean the defaults.
type Config struct {
	// APIKey is the bearer token POST /v1/laning and /v1/match require. Empty disables authentication (local `go run` only).
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

// MatchAnalyzer reads a decompressed replay and returns the whole match (implemented by replay.MatchAnalyzer).
type MatchAnalyzer interface {
	Analyze(ctx context.Context, r io.Reader) (matchdata.Match, error)
}

// Option adds an optional part to the handler.
type Option func(*server)

// WithMatchAnalyzer turns on POST /v1/match. Without it the route does not exist (404).
func WithMatchAnalyzer(analyzer MatchAnalyzer) Option {
	return func(s *server) { s.matchAnalyzer = analyzer }
}

// Prefetcher stages a replay without acquiring a parse slot.
type Prefetcher interface {
	Prefetch(context.Context, string) (int64, error)
}

func WithPrefetcher(p Prefetcher) Option { return func(s *server) { s.prefetcher = p } }

type server struct {
	prefetcher      Prefetcher
	prefetchTimeout time.Duration
	parser          *parsing.Service
	analyzer        Analyzer
	matchAnalyzer   MatchAnalyzer
	// maxMatchBytes is the largest /v1/match answer (uncompressed); bigger is a bug, answered with 500.
	maxMatchBytes int
	authEnabled   bool
	// The key is kept as a SHA-256 hash so the comparison has a fixed length and does not leak the key's length.
	apiKeyHash [sha256.Size]byte
	logger     *slog.Logger
}

// NewHandler returns the service's HTTP handler.
func NewHandler(source ReplaySource, analyzer Analyzer, config Config, logger *slog.Logger,
	options ...Option) http.Handler {
	if config.MaxConcurrentParses <= 0 {
		config.MaxConcurrentParses = DefaultMaxConcurrentParses
	}
	if config.RequestTimeout <= 0 {
		config.RequestTimeout = DefaultRequestTimeout
	}
	s := &server{
		parser: parsing.New(source, config.MaxConcurrentParses, config.RequestTimeout, parsing.WithObserver(func(m parsing.Measurement) {
			logger.Info("parse resources", "openMs", m.Open.Milliseconds(), "analyzeMs", m.Analyze.Milliseconds(),
				"elapsedMs", m.Elapsed.Milliseconds(), "readBytes", m.ReadBytes, "activeAtStart", m.ActiveAtStart)
		})),
		prefetchTimeout: config.RequestTimeout,
		analyzer:        analyzer,
		authEnabled:     config.APIKey != "",
		apiKeyHash:      sha256.Sum256([]byte(config.APIKey)),
		logger:          logger,

		maxMatchBytes: maxMatchResponseBytes,
	}
	for _, option := range options {
		option(s)
	}

	// Unknown routes keep the mux's plain-text 404/405 on purpose: the API client treats only problem+json as an
	// answer from this service, so a wrong base URL is never mistaken for "replay not found".
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", s.health)
	if s.prefetcher != nil {
		mux.Handle("POST /v1/prefetch", s.requireAPIKey(http.HandlerFunc(s.prefetch)))
	}
	mux.Handle("POST /v1/laning", s.requireAPIKey(http.HandlerFunc(s.laning)))
	if s.matchAnalyzer != nil {
		mux.Handle("POST /v1/match", s.requireAPIKey(http.HandlerFunc(s.match)))
	}
	return s.logRequests(s.recoverPanics(mux))
}

func (s *server) health(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "healthy"})
}

type replayRequest struct {
	ReplayURL string `json:"replayUrl"`
}

// respondFunc writes the 200 answer of a replay analyzed (and its log line).
type respondFunc func(w http.ResponseWriter, r *http.Request, logger *slog.Logger)

// analyzeFunc parses the decompressed replay and returns how to answer.
type analyzeFunc func(ctx context.Context, replay io.Reader) (respondFunc, error)

func (s *server) laning(w http.ResponseWriter, r *http.Request) {
	s.serveReplay(w, r, func(ctx context.Context, replay io.Reader) (respondFunc, error) {
		result, err := s.analyzer.Analyze(ctx, replay)
		if err != nil {
			return nil, err
		}
		return func(w http.ResponseWriter, _ *http.Request, logger *slog.Logger) {
			logger.Info("replay analyzed", "players", len(result.Players))
			writeJSON(w, http.StatusOK, newLaningResponse(result))
		}, nil
	})
}

// serveReplay is what every replay endpoint shares: the body ({ "replayUrl": ... }), the limit of parses at once,
// the time limit, the download and the translation of errors. analyze only parses the replay and says how to answer.
func (s *server) serveReplay(w http.ResponseWriter, r *http.Request, analyze analyzeFunc) {
	var request replayRequest
	if err := decodeJSON(w, r, &request); err != nil {
		writeProblem(w, http.StatusBadRequest, "Invalid request body", err.Error())
		return
	}
	if strings.TrimSpace(request.ReplayURL) == "" {
		writeProblem(w, http.StatusBadRequest, "Invalid request body", "replayUrl is required")
		return
	}

	started := time.Now()
	var respond respondFunc
	logger := s.logger.With("path", r.URL.Path)
	err := s.parser.Run(r.Context(), request.ReplayURL, func(ctx context.Context, stream io.Reader) error {
		var err error
		respond, err = analyze(ctx, stream)
		return err
	}, func() { respond(w, r, logger.With("elapsedMs", time.Since(started).Milliseconds())) })
	logger = logger.With("elapsedMs", time.Since(started).Milliseconds())
	if errors.Is(err, parsing.ErrBusy) {
		w.Header().Set("Retry-After", strconv.Itoa(retryAfterSeconds))
		writeProblem(w, http.StatusServiceUnavailable, "Busy", err.Error())
		return
	}
	if err != nil {
		s.writeAnalyzeError(w, r, logger, err)
		return
	}
}

func (s *server) writeAnalyzeError(w http.ResponseWriter, r *http.Request, logger *slog.Logger,
	err error) {
	if r.Context().Err() != nil {
		// The caller gave up (or the server is shutting down): nobody is left to read an answer.
		logger.Info("caller went away before the replay was analyzed", "error", err)
		return
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

var errTimedOut = parsing.ErrTimedOut

// classify maps an error to its HTTP status and ProblemDetails title.
func classify(err error) (int, string) {
	switch {
	case errors.Is(err, parsing.ErrInvalidURL):
		return http.StatusBadRequest, "Invalid replay URL"
	case errors.Is(err, parsing.ErrReplayNotFound):
		return http.StatusNotFound, "Replay not found"
	// Size limits are a property of the replay: retrying cannot help, so it is "unreadable" (422), not 502.
	case errors.Is(err, parsing.ErrTooLarge):
		return http.StatusUnprocessableEntity, "Replay too large"
	case errors.Is(err, parsing.ErrUnreadableReplay):
		return http.StatusUnprocessableEntity, "Unreadable replay"
	case errors.Is(err, errTimedOut):
		return http.StatusBadGateway, "Replay download timed out"
	case errors.Is(err, parsing.ErrDownloadFailed):
		return http.StatusBadGateway, "Replay download failed"
	default:
		return http.StatusInternalServerError, "Unexpected error"
	}
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

func (s *server) prefetch(w http.ResponseWriter, r *http.Request) {
	var request replayRequest
	if err := decodeJSON(w, r, &request); err != nil {
		writeProblem(w, 400, "Invalid request body", err.Error())
		return
	}
	if strings.TrimSpace(request.ReplayURL) == "" {
		writeProblem(w, 400, "Invalid request body", "replayUrl is required")
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), s.prefetchTimeout)
	defer cancel()
	n, err := s.prefetcher.Prefetch(ctx, request.ReplayURL)
	if errors.Is(err, parsing.ErrBusy) {
		w.Header().Set("Retry-After", strconv.Itoa(retryAfterSeconds))
		writeProblem(w, 503, "Busy", err.Error())
		return
	}
	if err != nil {
		if errors.Is(ctx.Err(), context.DeadlineExceeded) {
			err = fmt.Errorf("%w: %v", parsing.ErrTimedOut, err)
		}
		s.writeAnalyzeError(w, r, s.logger, err)
		return
	}
	writeJSON(w, 200, struct {
		Cached bool  `json:"cached"`
		Bytes  int64 `json:"bytes"`
	}{true, n})
}
