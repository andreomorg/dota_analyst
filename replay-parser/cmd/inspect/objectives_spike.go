package main

import (
	"flag"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
	"os"
)

func init() {
	flag.Func("objectives-spike", "survey objective counters and events in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		if err = replay.SurveyObjectives(f, os.Stdout); err != nil {
			return err
		}
		os.Exit(0)
		return nil
	})
}
