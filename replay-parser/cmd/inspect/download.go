package main

import (
	"context"
	"fmt"
	"io"
	"os"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

// downloadReplay saves a Valve replay, already decompressed (a plain .dem), so the other modes can read it as often as
// needed without downloading it again. It goes through the service's downloader: only *.valve.net, same size limits.
func downloadReplay(replayURL, path string) (err error) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
	defer cancel()
	replay, err := valve.NewDownloader(valve.Limits{}).Open(ctx, replayURL)
	if err != nil {
		return err
	}
	defer replay.Close()

	file, err := os.Create(path)
	if err != nil {
		return err
	}
	defer func() {
		if closeErr := file.Close(); err == nil {
			err = closeErr
		}
	}()
	written, err := io.Copy(file, replay)
	if err != nil {
		return err
	}
	fmt.Printf("saved %d MB (decompressed) to %s\n", written>>20, path)
	return nil
}
