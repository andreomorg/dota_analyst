package httpapi

import (
	"net/http"
	"runtime/debug"
	"time"
)

// statusRecorder remembers the status code a handler wrote, for the access log and the panic handler.
type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(status int) {
	if r.status == 0 {
		r.status = status
	}
	r.ResponseWriter.WriteHeader(status)
}

func (r *statusRecorder) Write(p []byte) (int, error) {
	if r.status == 0 {
		r.status = http.StatusOK
	}
	return r.ResponseWriter.Write(p)
}

// logRequests writes one log line per request (except healthy health checks). Only method, path, status and
// duration: never headers (the token is in one) nor bodies.
func (s *server) logRequests(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		started := time.Now()
		recorder := &statusRecorder{ResponseWriter: w}
		next.ServeHTTP(recorder, r)
		if r.URL.Path == "/health" && recorder.status == http.StatusOK {
			return // the Docker health check calls it every 30 s; logging each one would bury the useful lines
		}
		s.logger.Info("request",
			"method", r.Method,
			"path", r.URL.Path,
			"status", recorder.status,
			"elapsedMs", time.Since(started).Milliseconds())
	})
}

// recoverPanics answers 500 instead of dropping the connection when a handler panics (a corrupt replay can make a
// parser panic), and keeps the stack in the log.
func (s *server) recoverPanics(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		recorder, ok := w.(*statusRecorder)
		if !ok {
			recorder = &statusRecorder{ResponseWriter: w}
		}
		defer func() {
			recovered := recover()
			if recovered == nil {
				return
			}
			if recovered == http.ErrAbortHandler {
				panic(recovered) // net/http's own way to abort a response; let it through
			}
			s.logger.Error("panic handling request", "panic", recovered, "stack", string(debug.Stack()))
			if recorder.status == 0 {
				writeProblem(recorder, http.StatusInternalServerError, "Unexpected error", "unexpected error")
			}
		}()
		next.ServeHTTP(recorder, r)
	})
}
