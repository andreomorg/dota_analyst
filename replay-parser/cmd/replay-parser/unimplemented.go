package main

import (
	"context"
	"errors"
	"io"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
)

// unimplementedAnalyzer is a TEMPORARY placeholder until card 02's replay.Analyzer is merged; every parse fails
// with 500. TODO(card 02 merge): delete this file and use replay.Analyzer{} in main.go.
type unimplementedAnalyzer struct{}

func (unimplementedAnalyzer) Analyze(context.Context, io.Reader) (laning.Result, error) {
	return laning.Result{}, errors.New("replay analyzer not implemented yet (card 02)")
}
