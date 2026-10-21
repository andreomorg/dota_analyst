// Command replay-parser is the HTTP service the C# API calls to read a Dota 2 replay: the laning phase
// (POST /v1/laning) or the whole match (POST /v1/match).
//
// Configuration (environment variables):
//
//	PORT                   port to listen on (default 8081)
//	REPLAY_PARSER_API_KEY  bearer token required by POST /v1/laning and /v1/match; empty = no authentication (local only)
//	MAX_CONCURRENT_PARSES  replays parsed at once; more requests get 503 (default 1)
//	MAX_DOWNLOAD_MB        compressed replay size limit (default 200)
//	MAX_DECOMPRESSED_MB    decompressed replay size limit (default 1024)
//	REQUEST_TIMEOUT        download + parse limit per request, as a Go duration (default 3m)
//
// With -healthcheck it calls its own /health and exits 0 or 1 (for Docker: the image has no shell or curl).
package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/httpapi"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

// Docker sends SIGKILL 10 s after SIGTERM; requests still running after this are cut.
const shutdownTimeout = 8 * time.Second

func main() {
	healthcheck := flag.Bool("healthcheck", false, "call this service's /health and exit 0 (healthy) or 1")
	flag.Parse()

	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	config, err := loadConfig()
	if err != nil {
		logger.Error("invalid configuration", "error", err)
		os.Exit(1)
	}
	if *healthcheck {
		os.Exit(checkHealth(config.port))
	}
	if err := run(config, logger); err != nil {
		logger.Error("server stopped", "error", err)
		os.Exit(1)
	}
}

type config struct {
	cacheDir   string
	cacheMax   int64
	prefetches int
	port       string
	api        httpapi.Config
	limits     valve.Limits
}

func loadConfig() (config, error) {
	var c config
	var errs []error
	c.port = os.Getenv("PORT")
	if c.port == "" {
		c.port = "8081"
	}
	if port, err := strconv.Atoi(c.port); err != nil || port < 1 || port > 65535 {
		errs = append(errs, fmt.Errorf("PORT %q is not a port number", c.port))
	}
	c.cacheDir = os.Getenv("REPLAY_CACHE_DIR")
	c.cacheMax = int64(envInt("REPLAY_CACHE_MAX_BYTES", int(valve.DefaultCacheMaxBytes), &errs))
	c.prefetches = envInt("MAX_CONCURRENT_PREFETCHES", 4, &errs)
	c.api.APIKey = os.Getenv("REPLAY_PARSER_API_KEY")
	c.api.MaxConcurrentParses = envInt("MAX_CONCURRENT_PARSES", httpapi.DefaultMaxConcurrentParses, &errs)
	c.limits.MaxDownloadBytes = int64(envInt("MAX_DOWNLOAD_MB", int(valve.DefaultMaxDownloadBytes>>20), &errs)) << 20
	c.limits.MaxDecompressedBytes =
		int64(envInt("MAX_DECOMPRESSED_MB", int(valve.DefaultMaxDecompressedBytes>>20), &errs)) << 20
	c.api.RequestTimeout = httpapi.DefaultRequestTimeout
	if value := os.Getenv("REQUEST_TIMEOUT"); value != "" {
		timeout, err := time.ParseDuration(value)
		if err != nil || timeout <= 0 {
			errs = append(errs, fmt.Errorf("REQUEST_TIMEOUT %q is not a positive duration (e.g. 3m)", value))
		}
		c.api.RequestTimeout = timeout
	}
	return c, errors.Join(errs...)
}

// envInt reads a positive integer. A bad value is an error, not a silent default: a typo should stop the service.
func envInt(name string, fallback int, errs *[]error) int {
	value := os.Getenv(name)
	if value == "" {
		return fallback
	}
	number, err := strconv.Atoi(value)
	if err != nil || number <= 0 {
		*errs = append(*errs, fmt.Errorf("%s %q is not a positive integer", name, value))
		return fallback
	}
	return number
}

func run(c config, logger *slog.Logger) error {
	if c.api.APIKey == "" {
		logger.Warn("REPLAY_PARSER_API_KEY is empty: POST /v1/laning and /v1/match accept requests without a token " +
			"(local use only)")
	}

	cache, err := valve.NewCache(valve.NewDownloader(c.limits), c.cacheDir, c.cacheMax, c.prefetches)
	if err != nil {
		return err
	}
	if err := cache.Cleanup(); err != nil {
		return err
	}
	options := []httpapi.Option{httpapi.WithMatchAnalyzer(replay.MatchAnalyzer{})}
	if c.cacheDir != "" {
		options = append(options, httpapi.WithPrefetcher(cache))
	}
	handler := httpapi.NewHandler(cache, replay.Analyzer{}, c.api, logger,
		options...)

	server := &http.Server{
		Addr:              ":" + c.port,
		Handler:           handler,
		ReadHeaderTimeout: 10 * time.Second,
		ReadTimeout:       30 * time.Second,
		// Longer than the request timeout, so a slow parse still gets its answer (or its 502) written.
		WriteTimeout: c.api.RequestTimeout + 30*time.Second,
		IdleTimeout:  2 * time.Minute,
		ErrorLog:     slog.NewLogLogger(logger.Handler(), slog.LevelWarn),
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		ticker := time.NewTicker(time.Minute)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if err := cache.Cleanup(); err != nil {
					logger.Warn("cache cleanup failed", "error", err)
				}
			}
		}
	}()

	listener, err := net.Listen("tcp", server.Addr)
	if err != nil {
		return err
	}
	logger.Info("listening",
		"port", c.port,
		"authentication", c.api.APIKey != "",
		"maxConcurrentParses", c.api.MaxConcurrentParses,
		"maxDownloadBytes", c.limits.MaxDownloadBytes,
		"maxDecompressedBytes", c.limits.MaxDecompressedBytes,
		"requestTimeout", c.api.RequestTimeout.String())

	serveErr := make(chan error, 1)
	go func() { serveErr <- server.Serve(listener) }()

	select {
	case err := <-serveErr:
		return err
	case <-ctx.Done():
	}
	logger.Info("shutting down")
	shutdownCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
	defer cancel()
	if err := server.Shutdown(shutdownCtx); err != nil {
		// Close cancels the requests still running (their contexts end), which stops their downloads and parses.
		server.Close()
		return fmt.Errorf("graceful shutdown: %w", err)
	}
	return nil
}

func checkHealth(port string) int {
	client := &http.Client{Timeout: 3 * time.Second}
	response, err := client.Get("http://127.0.0.1:" + port + "/health")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		fmt.Fprintln(os.Stderr, "status", response.StatusCode)
		return 1
	}
	return 0
}
