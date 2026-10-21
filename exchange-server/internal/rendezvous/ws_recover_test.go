package rendezvous_test

import (
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/wire"
)

// panicBackend is a backend whose Hello panics: the shape of "a frame nobody foresaw reached a handler".
type panicBackend struct{ rendezvous.Backend }

func (panicBackend) Hello(*rendezvous.Stream, *wire.Hello) rendezvous.Result {
	panic("backend bug")
}

// One bad connection must never end the process (audit G-1): a panic on a connection's goroutine is
// logged and that connection closed, and the server goes on serving everyone else. Without the recover
// in Server.handle this test does not fail - it kills the test binary.
func TestAPanickingHandlerClosesItsConnectionAndTheServerLives(t *testing.T) {
	env := startLayer(t, func(cfg *rendezvous.Config) { cfg.Backend = panicBackend{Backend: cfg.Backend} })

	c := env.tls(t)
	if err := c.Send(&wire.Hello{DeviceID: devA, DeviceToken: fakeToken}); err != nil {
		t.Fatal(err)
	}
	if !readsEOF(c.Conn, 3*time.Second) {
		t.Fatal("the connection whose handler panicked was not closed")
	}

	// the stream closes while the panic unwinds, so the log line follows the EOF a moment later
	logged := false
	for deadline := time.Now().Add(3 * time.Second); !logged && time.Now().Before(deadline); time.Sleep(10 * time.Millisecond) {
		for _, line := range env.log.Lines() {
			if strings.Contains(line, "connection-panic") {
				logged = true
				if !strings.Contains(line, "backend bug") {
					t.Errorf("the log line does not say what panicked: %q", line)
				}
				if strings.Contains(line, "goroutine ") {
					t.Errorf("the log line carries a stack: %q", line)
				}
			}
		}
	}
	if !logged {
		t.Fatal("the panic was not logged")
	}

	// the server lives: another connection is served by the same backend
	env.enrolled(t)
}
