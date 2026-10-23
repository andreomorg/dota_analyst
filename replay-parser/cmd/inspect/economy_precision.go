package main

import (
	"flag"
	"os"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/replay"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
)

func init() {
	flag.Func("economy-precision", "inspect replicated economy counters in a local .dem and exit", func(path string) error {
		f, err := os.Open(path)
		if err != nil {
			return err
		}
		defer f.Close()
		input, err := valve.Decompress(f)
		if err != nil {
			return err
		}
		defer input.Close()
		if err := replay.InspectEconomyPrecision(input, os.Stdout); err != nil {
			return err
		}
		os.Exit(0)
		return nil
	})
}
