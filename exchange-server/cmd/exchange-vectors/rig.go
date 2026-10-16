package main

import (
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"runtime"
	"sync"
	"time"

	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The rig is the real server - the engine, the directory, the accounts store, the relay, the cast
// table, the connection layer over real TLS on loopback - with the three things a stable byte needs
// injected: the clock (one instant, moved only by the script), the id and token sources (seeded
// counters, version-proof) and the third-party port pool (a pool whose binds are not sockets, so a
// granted port is the same on every machine). Nothing of the server is stubbed beyond those.

// instant is the one moment every generated timestamp is taken from.
var instant = time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)

// publicEndpoint is the documentation address every generated `welcome` announces (RFC 6761 / 5737 names).
const publicEndpoint = "exchange.example.net:22022"

type clock struct {
	mu sync.Mutex
	t  time.Time
}

func (c *clock) Now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.t
}

func (c *clock) Add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

// seeded is a deterministic byte source: SHA-256 in counter mode over a label. The same label always
// gives the same bytes, on every Go version (math/rand's stream is not a promise we want to depend on).
type seeded struct {
	mu      sync.Mutex
	label   string
	counter uint64
	buf     []byte
}

func newSeeded(label string) *seeded { return &seeded{label: label} }

func (s *seeded) Read(p []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for n := 0; n < len(p); {
		if len(s.buf) == 0 {
			var ctr [8]byte
			binary.BigEndian.PutUint64(ctr[:], s.counter)
			s.counter++
			sum := sha256.Sum256(append([]byte("fms-exchange-vectors/"+s.label+"/"), ctr[:]...))
			s.buf = sum[:]
		}
		c := copy(p[n:], s.buf)
		s.buf = s.buf[c:]
		n += c
	}
	return len(p), nil
}

// genFailure is what a script panics with: Fatalf of the harness, a protocol surprise, a timeout.
type genFailure string

func (f genFailure) Error() string { return string(f) }

func failf(format string, args ...any) { panic(genFailure(fmt.Sprintf(format, args...))) }

// genTB is the harness the test kit wants (testkit.TB) for a program that is not a test.
type genTB struct {
	cleanups []func()
	dirs     []string
}

func (g *genTB) Helper()                           {}
func (g *genTB) Fatalf(format string, args ...any) { failf(format, args...) }
func (g *genTB) Cleanup(f func())                  { g.cleanups = append(g.cleanups, f) }
func (g *genTB) TempDir() string {
	d, err := os.MkdirTemp("", "exchange-vectors-")
	if err != nil {
		failf("temp dir: %v", err)
	}
	g.dirs = append(g.dirs, d)
	return d
}

// close runs the cleanups last-in first-out and removes the directories; a panic in one is returned.
func (g *genTB) close() (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("%v", r)
		}
		for _, d := range g.dirs {
			_ = os.RemoveAll(d)
		}
	}()
	for i := len(g.cleanups) - 1; i >= 0; i-- {
		g.cleanups[i]()
	}
	return nil
}

// fakeListener is a listener that is not a socket: the pool leases it, nothing ever connects.
type fakeListener struct {
	port int
	once sync.Once
	done chan struct{}
}

func (l *fakeListener) Accept() (net.Conn, error) {
	<-l.done
	return nil, net.ErrClosed
}
func (l *fakeListener) Close() error   { l.once.Do(func() { close(l.done) }); return nil }
func (l *fakeListener) Addr() net.Addr { return &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1), Port: l.port} }

func fakeBind(_ string, port int) (net.Listener, error) {
	return &fakeListener{port: port, done: make(chan struct{})}, nil
}

// rigOpts says what a transcript needs of the server.
type rigOpts struct {
	relay  bool
	cast   bool
	pool   bool
	params func(*rendezvous.Params)
	logins []string
	// pace turns the password pacer on with an interval of 2 s and a sleep that does not sleep, on the scenario's clock.
	pace bool
	// sealed turns the `sealed` feature on (27 item DP); the older scenarios run without it, as they always did.
	sealed bool
}

// rig is one server of one scenario.
type rig struct {
	tb    *genTB
	env   *testkit.Env
	clock *clock
	// sessions are every stream the script opened, kept reachable until the scenario ends: an
	// unreferenced connection is closed by the runtime's finalizer, which would end a control stream
	// (and so a broadcast) in the middle of a scenario.
	mu       sync.Mutex
	sessions []*sess
}

// newRig starts a server for a scenario. seed names the scenario: every id, token and code is drawn
// from sources seeded with it, so the scenario's bytes are a function of the code and nothing else.
func newRig(seed string, o rigOpts) *rig {
	r := &rig{tb: &genTB{}, clock: &clock{t: instant}}
	wire.SetRandom(newSeeded(seed + "/ids"))
	params := testkit.TestParams()
	params.UnavailableDelay = 5 * time.Millisecond
	if o.params != nil {
		o.params(&params)
	}
	opt := testkit.Options{
		Now: r.clock.Now, Params: &params, Logins: o.logins, PublicEndpoint: publicEndpoint,
		WithRelay: o.relay, Cast: o.cast, ServerVersion: "vectors",
		AccountsRand: newSeeded(seed + "/accounts"), EngineRand: newSeeded(seed + "/tunnels"), Sealed: o.sealed,
	}
	if o.pace {
		opt.PaceInterval, opt.Sleep = 2*time.Second, func(time.Duration) {}
	}
	if o.relay {
		opt.RelayConfig = func(c *rendezvous.RelayConfig) { c.Grace = 30 * time.Second }
	}
	if o.pool {
		opt.Pool = ports.NewPool(40000, 40009, "127.0.0.1", fakeBind, r.clock.Now)
	}
	r.env = testkit.StartServer(r.tb, opt)
	return r
}

// close ends the scenario's server and puts the id source back.
func (r *rig) close() error {
	r.mu.Lock()
	ss := r.sessions
	r.mu.Unlock()
	defer runtime.KeepAlive(ss)
	if os.Getenv("EXV_DEBUG") != "" {
		for _, l := range r.env.Log.Lines() {
			fmt.Fprintln(os.Stderr, l)
		}
	}
	r.env.Server.Close()
	err := r.tb.close()
	wire.SetRandom(nil)
	return err
}

// until polls a condition of the server's own state - the scripts wait for an effect that is
// asynchronous inside the server (a device going offline) before they make the next, ordered step.
func (r *rig) until(what string, cond func() bool) {
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	failf("timed out waiting for %s", what)
}

// newID draws an id from the scenario's seeded source.
func (r *rig) newID() string { return wire.NewID() }

var errNoFrame = errors.New("no frame")

func hexOf(b []byte) string { return hex.EncodeToString(b) }

var _ io.Reader = (*seeded)(nil)
