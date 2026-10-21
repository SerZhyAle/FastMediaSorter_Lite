package rendezvous

import "time"

// The whole test binary runs with a shorter wait for the peer's Close than production (one second):
// the wait is what every refused stream of a negative test would otherwise pay. It is set once, before
// any test starts, so no running server reads it while it changes. The tests that depend on it read
// wsCloseWait and never assume a number.
func init() { wsCloseWait = 500 * time.Millisecond }
