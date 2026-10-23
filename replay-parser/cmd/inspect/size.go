package main

import (
	"bytes"
	"compress/gzip"
)

func gzipSize(raw []byte) int {
	var buffer bytes.Buffer
	writer := gzip.NewWriter(&buffer)
	_, _ = writer.Write(raw)
	_ = writer.Close()
	return buffer.Len()
}
