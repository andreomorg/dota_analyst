package valve

import (
	"bytes"
	"compress/bzip2"
	"io"
	"testing"

	"github.com/klauspost/compress/zstd"
)

func TestDecompress(t *testing.T) {
	content := []byte("PBDEMS2\x00 replay bytes")

	zstdEncoded := func() []byte {
		var buffer bytes.Buffer
		encoder, err := zstd.NewWriter(&buffer)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := encoder.Write(content); err != nil {
			t.Fatal(err)
		}
		if err := encoder.Close(); err != nil {
			t.Fatal(err)
		}
		return buffer.Bytes()
	}()

	tests := []struct {
		name  string
		input []byte
	}{
		{"zstd (what Valve serves as .dem.bz2)", zstdEncoded},
		{"plain .dem", content},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			reader, err := Decompress(bytes.NewReader(test.input))
			if err != nil {
				t.Fatal(err)
			}
			defer reader.Close()
			got, err := io.ReadAll(reader)
			if err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(got, content) {
				t.Errorf("got %q, want %q", got, content)
			}
		})
	}
}

// The standard library has no bzip2 writer, so only the detection is checked: a bzip2 header must not be read as
// plain bytes.
func TestDecompress_Bzip2HeaderUsesBzip2(t *testing.T) {
	reader, err := Decompress(bytes.NewReader([]byte("BZh9 not really bzip2")))
	if err != nil {
		t.Fatal(err)
	}
	defer reader.Close()
	if _, err := io.ReadAll(reader); err == nil {
		t.Fatal("expected a bzip2 error for an invalid bzip2 stream, got plain bytes")
	} else if _, ok := err.(bzip2.StructuralError); !ok {
		t.Fatalf("expected bzip2.StructuralError, got %T: %v", err, err)
	}
}

func TestDecompress_EmptyInput(t *testing.T) {
	if _, err := Decompress(bytes.NewReader(nil)); err == nil {
		t.Fatal("expected an error for an empty stream")
	}
}
