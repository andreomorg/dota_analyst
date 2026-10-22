package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
)

func runHeroTimelineEvidence(r io.Reader, out string) error {
	report, err := replay.InspectHeroState(context.Background(), r)
	if err != nil {
		return err
	}
	data, err := json.Marshal(report)
	if err != nil {
		return err
	}
	if out == "" {
		_, err = fmt.Println(string(data))
		return err
	}
	return os.WriteFile(out, data, 0600)
}
