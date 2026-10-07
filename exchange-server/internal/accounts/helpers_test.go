package accounts

import (
	"fmt"
	"io"
)

func sprintf(format string, a ...any) string { return fmt.Sprintf(format, a...) }

// seq is a random source that returns fixed bytes, then fails: a test that must know exactly which
// bytes the store drew.
type seq struct {
	b []byte
	i int
}

func (s *seq) Read(p []byte) (int, error) {
	if s.i >= len(s.b) {
		return 0, io.ErrUnexpectedEOF
	}
	n := copy(p, s.b[s.i:])
	s.i += n
	return n, nil
}
