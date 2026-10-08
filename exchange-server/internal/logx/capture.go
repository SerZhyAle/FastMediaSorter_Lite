package logx

import (
	"io"
	"strings"
	"sync"
)

// TB is the part of testing.TB the assertion needs; naming it keeps "testing" out of the binary.
type TB interface {
	Helper()
	Fatalf(format string, args ...any)
}

// Capture is a Logger whose lines are kept in memory, for tests that must prove what a log does
// not contain. It is part of the package (not a _test file) so every package's tests can use it.
type Capture struct {
	*Logger
	mu    sync.Mutex
	lines []string
	also  io.Writer // every line is also written here (a log file the test will scan), or nil
}

type captureWriter struct{ c *Capture }

func (w captureWriter) Write(p []byte) (int, error) {
	w.c.mu.Lock()
	w.c.lines = append(w.c.lines, strings.Split(strings.TrimRight(string(p), "\n"), "\n")...)
	also := w.c.also
	w.c.mu.Unlock()
	if also != nil {
		_, _ = also.Write(p)
	}
	return len(p), nil
}

// NewCapture returns a capturing logger.
func NewCapture() *Capture {
	c := &Capture{}
	c.Logger = New(captureWriter{c})
	return c
}

// NewCaptureAlso is NewCapture that also writes every line to w: a test that runs a scenario against a
// log FILE keeps the in-memory lines for its assertions and the file for the data-directory scan.
func NewCaptureAlso(w io.Writer) *Capture {
	c := NewCapture()
	c.also = w
	return c
}

// Lines returns a copy of the lines written so far.
func (c *Capture) Lines() []string {
	c.mu.Lock()
	defer c.mu.Unlock()
	return append([]string(nil), c.lines...)
}

// AssertNone fails the test if any line contains one of the secrets (a share id, a tunnel id).
func (c *Capture) AssertNone(t TB, secrets ...string) {
	t.Helper()
	for _, line := range c.Lines() {
		for _, s := range secrets {
			if s != "" && strings.Contains(line, s) {
				t.Fatalf("a log line carries a secret: %q", line)
			}
		}
	}
}
