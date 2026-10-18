package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/provenance"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
)

// runMatch runs the POST /v1/match analyzer on a local replay and writes the document (indented) to out, or to the
// standard output when out is empty. Time and sizes go to the standard error, so the output stays valid JSON.
func runMatch(r io.Reader, out, manifest, source string) error {
	if manifest != "" {
		if out == "" {
			return fmt.Errorf("-manifest requires -out")
		}
		if _, err := os.Lstat(manifest); !os.IsNotExist(err) {
			return fmt.Errorf("manifest destination must be new")
		}
		if _, err := os.Lstat(out); !os.IsNotExist(err) {
			return fmt.Errorf("document destination must be new when using -manifest")
		}
	}
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
	if manifest == "" {
		return os.WriteFile(out, indented, 0o644)
	}
	f, err := os.OpenFile(out, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	_, writeErr := f.Write(indented)
	closeErr := f.Close()
	if writeErr != nil {
		return writeErr
	}
	if closeErr != nil {
		return closeErr
	}
	return provenance.WriteNew(manifest, out, source, match.Match.MatchID, match.SchemaVersion)
}
