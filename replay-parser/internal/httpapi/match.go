package httpapi

import (
	"bytes"
	"compress/gzip"
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"strconv"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// maxMatchResponseBytes caps the POST /v1/match answer (uncompressed). With the prediction models' sections (21/10/2026)
// a 30-minute match is ~1.8 MB and a long one went past 4.3 MB (gzip ~4x smaller through the tunnel), so the cap is
// 16 MB: anything near it still means a collector went wrong (event-by-event data, a loop) and is refused.
const maxMatchResponseBytes = 16 << 20

// match answers POST /v1/match: the same request, limits and errors as the laning rule, the whole match as the answer
// (the matchdata package is the contract).
func (s *server) match(w http.ResponseWriter, r *http.Request) {
	s.serveReplay(w, r, func(ctx context.Context, replay io.Reader) (respondFunc, error) {
		match, err := s.matchAnalyzer.Analyze(ctx, replay)
		if err != nil {
			return nil, err
		}
		return func(w http.ResponseWriter, r *http.Request, logger *slog.Logger) {
			s.writeMatch(w, r, logger, match)
		}, nil
	})
}

// writeMatch writes the document, gzip-compressed when the client accepts it (the answer goes through the ngrok
// tunnel: ~5x smaller).
func (s *server) writeMatch(w http.ResponseWriter, r *http.Request, logger *slog.Logger, match matchdata.Match) {
	body, err := json.Marshal(match)
	if err != nil {
		logger.Error("could not write the match as JSON", "error", err)
		writeProblem(w, http.StatusInternalServerError, "Unexpected error", "unexpected error writing the match")
		return
	}
	if len(body) > s.maxMatchBytes {
		logger.Error("match answer over the size limit: a collector produced too much data",
			"bytes", len(body), "limitBytes", s.maxMatchBytes)
		writeProblem(w, http.StatusInternalServerError, "Response too large",
			"the match document is larger than the service's limit")
		return
	}

	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Vary", "Accept-Encoding")
	compressed := acceptsGzip(r.Header.Get("Accept-Encoding"))
	if compressed {
		var buffer bytes.Buffer
		writer := gzip.NewWriter(&buffer)
		_, _ = writer.Write(body) // writing to memory cannot fail
		_ = writer.Close()
		w.Header().Set("Content-Encoding", "gzip")
		logger.Info("match analyzed", "bytes", len(body), "gzipBytes", buffer.Len())
		body = buffer.Bytes()
	} else {
		logger.Info("match analyzed", "bytes", len(body))
	}
	w.Header().Set("Content-Length", strconv.Itoa(len(body)))
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(body)
}

// acceptsGzip reads an Accept-Encoding header ("gzip, deflate, br", "gzip;q=0.5", "*"): true when gzip is accepted
// with a quality above zero (an explicit "gzip" wins over "*").
func acceptsGzip(header string) bool {
	accepted := map[string]bool{}
	for _, part := range strings.Split(header, ",") {
		coding, params, _ := strings.Cut(strings.TrimSpace(part), ";")
		coding = strings.ToLower(strings.TrimSpace(coding))
		if coding != "gzip" && coding != "*" {
			continue
		}
		accepted[coding] = true
		if name, value, found := strings.Cut(strings.TrimSpace(params), "="); found &&
			strings.EqualFold(strings.TrimSpace(name), "q") {
			quality, err := strconv.ParseFloat(strings.TrimSpace(value), 64)
			accepted[coding] = err == nil && quality > 0
		}
	}
	if gzipAccepted, mentioned := accepted["gzip"]; mentioned {
		return gzipAccepted
	}
	return accepted["*"]
}
