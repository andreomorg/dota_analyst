package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
)

// runMatch runs the POST /v1/match analyzer on a local replay and writes the document (indented) to out, or to the
// standard output when out is empty. Time and sizes go to the standard error, so the output stays valid JSON.
func runMatch(r io.Reader, out string) error {
	started := time.Now()
	match, err := replay.MatchAnalyzer{}.Analyze(context.Background(), r)
	if err != nil {
		return err
	}
	elapsed := time.Since(started)

	compact, err := json.Marshal(match)
	if err != nil {
		return err
	}
	fmt.Fprintf(os.Stderr, "parsed in %s; JSON %.1f KB (gzip %.1f KB)\n", elapsed.Round(time.Millisecond),
		float64(len(compact))/1024, float64(gzipSize(compact))/1024)

	indented, err := json.MarshalIndent(match, "", "  ")
	if err != nil {
		return err
	}
	indented = append(indented, '\n')
	if out == "" {
		_, err = os.Stdout.Write(indented)
		return err
	}
	return os.WriteFile(out, indented, 0o644)
}
