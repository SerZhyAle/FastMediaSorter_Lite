package rendezvous_test

import (
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"errors"
	"io"
	"net"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

const sshPW = "ssh-secret"

type fakeClock struct {
	mu sync.Mutex
	t  time.Time
}

func newClock() *fakeClock { return &fakeClock{t: time.Date(2026, 10, 6, 12, 0, 0, 0, time.UTC)} }
func (c *fakeClock) Now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.t
}
func (c *fakeClock) Add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

func eventually(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatalf("timed out waiting for: %s", what)
}

// registered registers a fresh producer and starts its control loop.
func registered(t *testing.T, env *testkit.Env) *testkit.Producer {
	t.Helper()
	p := env.NewProducer(sshPW)
	reply, err := p.Register()
	if err != nil || reply.Type != wire.TypeRegistered {
		t.Fatalf("register: %v %+v", err, reply)
	}
	go p.Serve()
	t.Cleanup(p.Close)
	return p
}

// connect opens a consumer on a plain stream and returns it once `connected` arrived.
func connect(t *testing.T, env *testkit.Env, shareID string) net.Conn {
	t.Helper()
	c, err := testkit.DialPlain(env.Addr)
	if err != nil {
		t.Fatal(err)
	}
	m := wire.New(wire.TypeConnect)
	m.ShareID = shareID
	if err := c.Send(m); err != nil {
		t.Fatal(err)
	}
	reply, err := c.Recv(5 * time.Second)
	if err != nil || reply.Type != wire.TypeConnected {
		t.Fatalf("connect: %v %+v", err, reply)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c.Conn
}

// answer returns every byte the server sends to a consumer that asked for shareID, and how long it took.
func answer(t *testing.T, env *testkit.Env, shareID string) ([]byte, time.Duration) {
	t.Helper()
	c, err := testkit.DialPlain(env.Addr)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	m := wire.New(wire.TypeConnect)
	m.ShareID = shareID
	start := time.Now()
	if err := c.Send(m); err != nil {
		t.Fatal(err)
	}
	_ = c.SetReadDeadline(time.Now().Add(5 * time.Second))
	b, _ := io.ReadAll(c)
	return b, time.Since(start)
}

// rawFrame frames a JSON body by hand, for envelopes the codec would not build.
func rawFrame(body string) []byte {
	n := len(body)
	return append([]byte{byte(n >> 24), byte(n >> 16), byte(n >> 8), byte(n)}, body...)
}

func readsEOF(c net.Conn, within time.Duration) bool {
	_ = c.SetReadDeadline(time.Now().Add(within))
	_, err := c.Read(make([]byte, 1))
	if err == nil {
		return false
	}
	var ne net.Error
	return !(errors.As(err, &ne) && ne.Timeout())
}

// --- the listener: the first-byte split ---

func TestPlainRegisterIsRefusedWithTLSRequired(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	c, err := testkit.DialPlain(env.Addr)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	m := wire.New(wire.TypeRegister)
	m.Password, m.ShareID = env.Password, wire.NewID()
	if err := c.Send(m); err != nil {
		t.Fatal(err)
	}
	reply, err := c.Recv(3 * time.Second)
	if err != nil || reply.Type != wire.TypeRefused || reply.Reason != wire.ReasonTLSRequired {
		t.Fatalf("a plain register must be refused tls-required: %v %+v", err, reply)
	}
	if !readsEOF(c, 2*time.Second) {
		t.Fatal("the stream must be closed after the refusal")
	}
	if env.Server.Registrations() != 0 {
		t.Fatal("a plain register must never register")
	}
}

func TestPlainAttachIsRefusedWithTLSRequired(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	c, _ := testkit.DialPlain(env.Addr)
	defer c.Close()
	m := wire.New(wire.TypeAttach)
	m.ShareID, m.TunnelID = wire.NewID(), wire.NewID()
	_ = c.Send(m)
	reply, err := c.Recv(3 * time.Second)
	if err != nil || reply.Reason != wire.ReasonTLSRequired {
		t.Fatalf("%v %+v", err, reply)
	}
}

func TestGarbageFirstByteIsClosedSilently(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	c, err := net.Dial("tcp", env.Addr)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	_, _ = c.Write([]byte("GET / HTTP/1.1\r\nHost: x\r\n\r\n"))
	_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
	n, err := c.Read(make([]byte, 64))
	if n != 0 || err == nil {
		t.Fatalf("anything but 0x16 or 0x00 gets no answer at all: %d bytes, %v", n, err)
	}
}

func TestSilentClientHitsThePeekDeadline(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	c, _ := net.Dial("tcp", env.Addr)
	defer c.Close()
	start := time.Now()
	if !readsEOF(c, 4*time.Second) {
		t.Fatal("a client that sends nothing must be dropped")
	}
	if d := time.Since(start); d > 2500*time.Millisecond {
		t.Fatalf("dropped after %v, the peek deadline is 1 s", d)
	}
}

func TestStalledHandshakeHitsTheHandshakeDeadline(t *testing.T) {
	p := testkit.TestParams()
	p.HandshakeTimeout = 400 * time.Millisecond
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	c, _ := net.Dial("tcp", env.Addr)
	defer c.Close()
	_, _ = c.Write([]byte{0x16}) // a TLS record start, then nothing
	start := time.Now()
	if !readsEOF(c, 4*time.Second) {
		t.Fatal("a stalled handshake must be dropped")
	}
	if d := time.Since(start); d > 2*time.Second {
		t.Fatalf("dropped after %v, the handshake deadline is 400 ms", d)
	}
}

func TestSlowFirstFrameHitsTheFirstFrameDeadline(t *testing.T) {
	p := testkit.TestParams()
	p.FirstFrameTimeout = 400 * time.Millisecond
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	c, _ := net.Dial("tcp", env.Addr)
	defer c.Close()
	_, _ = c.Write([]byte{0x00, 0x00}) // half a length prefix
	start := time.Now()
	if !readsEOF(c, 4*time.Second) {
		t.Fatal("a half frame must be dropped")
	}
	if d := time.Since(start); d > 2*time.Second {
		t.Fatalf("dropped after %v", d)
	}
}

func TestEnvelopeRulesOnTheFirstFrame(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	// a known envelope that is invalid in the first state closes the stream
	c, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	_ = c.Send(wire.New(wire.TypeKeepalive))
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Error("keepalive as a first frame must close the stream")
	}
	// another schemaVersion closes it
	c2, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	_, _ = c2.Write(rawFrame(`{"schemaVersion":2,"type":"register"}`))
	if !readsEOF(c2.Conn, 2*time.Second) {
		t.Error("an unsupported schemaVersion must close the stream")
	}
	// an oversize length prefix closes it
	c3, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	_, _ = c3.Write([]byte{0x00, 0x01, 0x00, 0x00})
	if !readsEOF(c3.Conn, 2*time.Second) {
		t.Error("a frame above 16 KiB must close the stream")
	}
	// unknown types before the real envelope are ignored
	c4, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	_, _ = c4.Write(rawFrame(`{"schemaVersion":1,"type":"hologram","x":1}`))
	reg := wire.New(wire.TypeRegister)
	reg.Password, reg.ShareID = env.Password, wire.NewID()
	env.AddSecret(reg.ShareID)
	_ = c4.Send(reg)
	reply, err := c4.Recv(3 * time.Second)
	if err != nil || reply.Type != wire.TypeRegistered {
		t.Fatalf("an unknown type must be ignored, not break the stream: %v %+v", err, reply)
	}
	c4.Close()
}

// --- the producer ---

func TestRegisterAndKeepaliveEcho(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := env.NewProducer(sshPW)
	reply, err := p.Register()
	if err != nil || reply.Type != wire.TypeRegistered {
		t.Fatalf("%v %+v", err, reply)
	}
	if reply.KeepaliveSeconds != 30 || !wire.ValidID(reply.ResumeToken) || reply.Port != nil {
		t.Fatalf("registered answer: %+v", reply)
	}
	if err := p.SendKeepalive(); err != nil {
		t.Fatalf("the server must echo each keepalive: %v", err)
	}
	if env.Server.Registrations() != 1 {
		t.Fatal("not registered")
	}
}

func TestKeepaliveIntervalIsClamped(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	for _, c := range []struct{ propose, want int }{{0, 30}, {1, 10}, {45, 45}, {500, 120}} {
		p := env.NewProducer(sshPW)
		p.Keepalive = c.propose
		reply, err := p.Register()
		if err != nil || reply.Type != wire.TypeRegistered || reply.KeepaliveSeconds != c.want {
			t.Errorf("propose %d: want %d, got %+v %v", c.propose, c.want, reply, err)
		}
		p.Close()
	}
}

func TestWrongPasswordIsRefused(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := env.NewProducer(sshPW)
	p.Password = "Wrongpass1"
	reply, err := p.Register()
	if err != nil || reply.Type != wire.TypeRefused || reply.Reason != wire.ReasonBadPassword {
		t.Fatalf("%v %+v", err, reply)
	}
	if env.Server.Registrations() != 0 {
		t.Fatal("a wrong password must not register")
	}
}

func TestMissedKeepaliveWindowEndsTheRegistration(t *testing.T) {
	ck := newClock()
	env := testkit.StartServer(t, testkit.Options{Now: ck.Now})
	p := env.NewProducer(sshPW)
	p.Keepalive = 10 // window = 3 x 10 s
	if reply, _ := p.Register(); reply.Type != wire.TypeRegistered {
		t.Fatal("not registered")
	}
	ck.Add(25 * time.Second)
	if err := p.SendKeepalive(); err != nil { // inside the window: extends it
		t.Fatal(err)
	}
	ck.Add(25 * time.Second) // 50 s since register, 25 since the keepalive
	time.Sleep(150 * time.Millisecond)
	if env.Server.Registrations() != 1 {
		t.Fatal("a keepalive must extend the window")
	}
	ck.Add(10 * time.Second) // 35 s without one: past 3 x 10 s
	eventually(t, "the registration to expire", func() bool { return env.Server.Registrations() == 0 })
	if !readsEOF(p.Control(), 2*time.Second) {
		t.Fatal("expiry must close the control stream")
	}
	// and the id now answers like an unknown one
	b, _ := answer(t, env, p.ShareID)
	if !bytes.Contains(b, []byte(wire.ReasonUnavailable)) {
		t.Fatalf("an expired id must be unavailable: %q", b)
	}
}

func TestIDCollisionAndResume(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := env.NewProducer(sshPW)
	first, _ := p.Register()
	if first.Type != wire.TypeRegistered {
		t.Fatal("not registered")
	}
	oldControl := p.Control()

	// a second producer claiming the same id without the token is refused
	q := env.NewProducer(sshPW)
	q.ShareID = p.ShareID
	reply, err := q.Register()
	if err != nil || reply.Reason != wire.ReasonIDCollision {
		t.Fatalf("collision: %v %+v", err, reply)
	}
	// the owner coming back with its token takes over, and the old stream is closed
	second, err := p.Register()
	if err != nil || second.Type != wire.TypeRegistered {
		t.Fatalf("resume: %v %+v", err, second)
	}
	if second.ResumeToken == first.ResumeToken {
		t.Fatal("the resume token must rotate")
	}
	if !readsEOF(oldControl, 2*time.Second) {
		t.Fatal("the superseded control stream must be closed")
	}
	if env.Server.Registrations() != 1 {
		t.Fatalf("a resume must not add a registration: %d", env.Server.Registrations())
	}
	// an old token no longer works
	p.Close()
	stale := env.NewProducer(sshPW)
	stale.ShareID = p.ShareID
	stale.Register() // refused (collision) - and to carry a stale token, set it by hand:
	raw, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	m := wire.New(wire.TypeRegister)
	m.Password, m.ShareID, m.ResumeToken = env.Password, p.ShareID, first.ResumeToken
	_ = raw.Send(m)
	if r, _ := raw.Recv(3 * time.Second); r.Reason != wire.ReasonIDCollision {
		t.Fatalf("a rotated-away token must be refused: %+v", r)
	}
}

func TestDroppedControlStreamKeepsTheRegistrationUntilTheWindowEnds(t *testing.T) {
	ck := newClock()
	env := testkit.StartServer(t, testkit.Options{Now: ck.Now})
	p := env.NewProducer(sshPW)
	p.Keepalive = 10
	p.Register()
	p.Close() // a drop, not an unregister
	time.Sleep(100 * time.Millisecond)
	if env.Server.Registrations() != 1 {
		t.Fatal("a dropped stream must leave the registration for a resume")
	}
	if _, err := p.Register(); err != nil { // resumes
		t.Fatal(err)
	}
	ck.Add(40 * time.Second)
	p.Close()
	eventually(t, "expiry after the drop", func() bool { return env.Server.Registrations() == 0 })
}

func TestUnregisterHasNoReplyAndEndsTheRegistration(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := registered(t, env)
	// A tunnel in flight dies with its registration.
	_ = p.Control().Send(wire.New(wire.TypeUnregister))
	eventually(t, "unregister to land", func() bool { return env.Server.Registrations() == 0 })
	b, _ := answer(t, env, p.ShareID)
	if !bytes.Contains(b, []byte(wire.ReasonUnavailable)) {
		t.Fatalf("after unregister the id is unavailable: %q", b)
	}
}

func TestControlStreamOutOfStateEnvelopeClosesIt(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := env.NewProducer(sshPW)
	p.Register()
	m := wire.New(wire.TypeConnect)
	m.ShareID = wire.NewID()
	_ = p.Control().Send(m)
	if !readsEOF(p.Control(), 2*time.Second) {
		t.Fatal("a connect on a control stream must close it")
	}
	// an unknown type is ignored
	q := env.NewProducer(sshPW)
	if r, _ := q.Register(); r.Type != wire.TypeRegistered {
		t.Fatal("not registered")
	}
	_, _ = q.Control().Write(rawFrame(`{"schemaVersion":1,"type":"hologram","x":1}`))
	if err := q.SendKeepalive(); err != nil {
		t.Fatalf("the control stream must survive an unknown envelope: %v", err)
	}
}

func TestCapacity(t *testing.T) {
	p := testkit.TestParams()
	p.MaxRegistrations = 2
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	registered(t, env)
	registered(t, env)
	third := env.NewProducer(sshPW)
	reply, _ := third.Register()
	if reply.Reason != wire.ReasonCapacity {
		t.Fatalf("the third registration must be refused capacity: %+v", reply)
	}
}

// --- the consumer and the bridge ---

func TestConsumerBridgesSSHOverPlainAndTLS(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := registered(t, env)

	plain := connect(t, env, p.ShareID)
	out, err := testkit.SSHExec(plain, "u", sshPW, "ls")
	if err != nil || out != "echo:ls" {
		t.Fatalf("plain consumer: %q %v", out, err)
	}

	tc, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	m := wire.New(wire.TypeConnect)
	m.ShareID = p.ShareID
	_ = tc.Send(m)
	if r, err := tc.Recv(5 * time.Second); err != nil || r.Type != wire.TypeConnected {
		t.Fatalf("tls consumer: %v %+v", err, r)
	}
	out, err = testkit.SSHExec(tc.Conn, "u", sshPW, "pwd")
	if err != nil || out != "echo:pwd" {
		t.Fatalf("TLS consumer: %q %v", out, err)
	}
}

func TestTwoSimultaneousTunnelsOnOneShare(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := registered(t, env)
	a, err := testkit.SSHClient(connect(t, env, p.ShareID), "u", sshPW)
	if err != nil {
		t.Fatal(err)
	}
	defer a.Close()
	b, err := testkit.SSHClient(connect(t, env, p.ShareID), "u", sshPW)
	if err != nil {
		t.Fatal(err)
	}
	defer b.Close()
	// both are open at once and both work, in either order
	if out, err := testkit.SSHExecOn(b, "from-b"); err != nil || out != "echo:from-b" {
		t.Fatalf("b: %q %v", out, err)
	}
	if out, err := testkit.SSHExecOn(a, "from-a"); err != nil || out != "echo:from-a" {
		t.Fatalf("a: %q %v", out, err)
	}
}

// holdHandler keeps a tunnel open until the consumer closes it.
func holdHandler(c net.Conn) { _, _ = io.Copy(io.Discard, c); _ = c.Close() }

// holdingProducer registers a producer whose tunnels stay open until the consumer closes them.
func holdingProducer(t *testing.T, env *testkit.Env) *testkit.Producer {
	t.Helper()
	p := env.NewProducer(sshPW)
	p.Handler = holdHandler
	if r, err := p.Register(); err != nil || r.Type != wire.TypeRegistered {
		t.Fatalf("register: %v %+v", err, r)
	}
	go p.Serve()
	t.Cleanup(p.Close)
	return p
}

func TestTunnelCap(t *testing.T) {
	p := testkit.TestParams()
	p.TunnelCap = 2
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	prod := holdingProducer(t, env)
	c1 := connect(t, env, prod.ShareID)
	c2 := connect(t, env, prod.ShareID)
	b, _ := answer(t, env, prod.ShareID)
	if !bytes.Contains(b, []byte(wire.ReasonUnavailable)) {
		t.Fatalf("the third tunnel must be refused unavailable: %q", b)
	}
	// a freed slot is usable again
	_ = c1.Close()
	eventually(t, "a slot to free", func() bool {
		c, err := testkit.DialPlain(env.Addr)
		if err != nil {
			return false
		}
		defer c.Close()
		m := wire.New(wire.TypeConnect)
		m.ShareID = prod.ShareID
		_ = c.Send(m)
		r, err := c.Recv(2 * time.Second)
		return err == nil && r.Type == wire.TypeConnected
	})
	_ = c2.Close()
}

func TestAttachNeverComesAnswersUnavailable(t *testing.T) {
	p := testkit.TestParams()
	p.PendingWindow = 300 * time.Millisecond
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	prod := env.NewProducer(sshPW)
	prod.Register() // registered, but its control loop never answers an open
	b, d := answer(t, env, prod.ShareID)
	if !bytes.Contains(b, []byte(wire.ReasonUnavailable)) || d < 300*time.Millisecond {
		t.Fatalf("no attach in time must end as unavailable after the window: %q after %v", b, d)
	}
}

func TestAttachRules(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	p := env.NewProducer(sshPW)
	p.Register()
	// an attach for nothing is closed without an answer
	c, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	m := wire.New(wire.TypeAttach)
	m.ShareID, m.TunnelID = p.ShareID, wire.NewID()
	_ = c.Send(m)
	_ = c.SetReadDeadline(time.Now().Add(2 * time.Second))
	if n, err := c.Read(make([]byte, 8)); n != 0 || err == nil {
		t.Fatalf("an unknown tunnel id must be closed silently: %d %v", n, err)
	}
	// a tunnel id is single use and bound to its share
	prod := registered(t, env)
	var seen string
	var mu sync.Mutex
	got := make(chan string, 1)
	prod.Secret = func(s string) {
		mu.Lock()
		seen = s
		mu.Unlock()
		select {
		case got <- s:
		default:
		}
	}
	prod.Handler = func(c net.Conn) { _, _ = io.Copy(io.Discard, c) }
	go func() { _, _ = io.Copy(io.Discard, connect(t, env, prod.ShareID)) }()
	select {
	case <-got:
	case <-time.After(3 * time.Second):
		t.Fatal("no open arrived")
	}
	mu.Lock()
	tid := seen
	mu.Unlock()
	// the real attach already happened (the Serve loop did it); replaying the id must fail
	time.Sleep(100 * time.Millisecond)
	r, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	rep := wire.New(wire.TypeAttach)
	rep.ShareID, rep.TunnelID = prod.ShareID, tid
	_ = r.Send(rep)
	_ = r.SetReadDeadline(time.Now().Add(2 * time.Second))
	if n, err := r.Read(make([]byte, 8)); n != 0 || err == nil {
		t.Fatalf("a replayed tunnel id must be closed silently: %d %v", n, err)
	}
}

func TestBridgeCarries64MiBByHashWithHalfClose(t *testing.T) {
	if testing.Short() {
		t.Skip("64 MiB")
	}
	env := testkit.StartServer(t, testkit.Options{})
	prod := env.NewProducer(sshPW)
	prod.Handler = func(c net.Conn) {
		h := sha256.New()
		_, _ = io.Copy(h, c) // until the consumer's half-close reaches us as EOF
		_, _ = c.Write(h.Sum(nil))
		_ = c.Close()
	}
	if r, _ := prod.Register(); r.Type != wire.TypeRegistered {
		t.Fatal("not registered")
	}
	go prod.Serve()
	defer prod.Close()

	c := connect(t, env, prod.ShareID).(*net.TCPConn)
	want := sha256.New()
	go func() {
		buf := make([]byte, 64*1024)
		for sent := 0; sent < 64<<20; sent += len(buf) {
			_, _ = rand.Read(buf)
			want.Write(buf)
			if _, err := c.Write(buf); err != nil {
				return
			}
		}
		_ = c.CloseWrite()
	}()
	_ = c.SetReadDeadline(time.Now().Add(60 * time.Second))
	got := make([]byte, 32)
	if _, err := io.ReadFull(c, got); err != nil {
		t.Fatalf("no hash came back: %v", err)
	}
	// the writer goroutine finished before CloseWrite, which preceded the producer's answer
	if !bytes.Equal(got, want.Sum(nil)) {
		t.Fatal("the bridge changed bytes")
	}
}

func TestProducerDisappearingMidTunnelEndsTheConsumer(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	prod := holdingProducer(t, env)
	c := connect(t, env, prod.ShareID)
	_ = prod.Control().Send(wire.New(wire.TypeUnregister))
	if !readsEOF(c, 3*time.Second) {
		t.Fatal("a consumer must not be left on a bridge whose registration ended")
	}
}

func TestIdleBridgeIsClosed(t *testing.T) {
	p := testkit.TestParams()
	p.IdleTimeout = 300 * time.Millisecond
	env := testkit.StartServer(t, testkit.Options{Params: &p})
	prod := holdingProducer(t, env)
	c := connect(t, env, prod.ShareID)
	start := time.Now()
	if !readsEOF(c, 3*time.Second) {
		t.Fatal("an idle bridge must be closed")
	}
	if d := time.Since(start); d < 250*time.Millisecond {
		t.Fatalf("closed after %v, before the idle bound", d)
	}
}

func TestBytesAfterConnectedAreNotLost(t *testing.T) {
	// The consumer may send its first raw bytes right behind the connect frame; the first-byte
	// peek must not swallow them.
	env := testkit.StartServer(t, testkit.Options{})
	prod := env.NewProducer(sshPW)
	got := make(chan string, 1)
	prod.Handler = func(c net.Conn) {
		buf := make([]byte, 5)
		_, _ = io.ReadFull(c, buf)
		got <- string(buf)
		_ = c.Close()
	}
	prod.Register()
	go prod.Serve()
	defer prod.Close()

	c, _ := testkit.DialPlain(env.Addr)
	defer c.Close()
	m := wire.New(wire.TypeConnect)
	m.ShareID = prod.ShareID
	frame, _ := wire.Frame(m)
	_, _ = c.Write(append(frame, []byte("HELLO")...)) // one write: frame and raw bytes together
	select {
	case s := <-got:
		if s != "HELLO" {
			t.Fatalf("got %q", s)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("the raw bytes behind the connect frame were lost")
	}
}

// --- ticket 136 T-5: unknown, ended and full are one answer ---

func TestUnavailableIsOneAnswerForUnknownEndedAndFull(t *testing.T) {
	p := testkit.TestParams()
	p.TunnelCap = 1
	p.UnavailableDelay = 200 * time.Millisecond
	lc := limits.DefaultConfig()
	lc.Rates[limits.UnknownID] = limits.Rate{PerMinute: 600, Burst: 100}
	env := testkit.StartServer(t, testkit.Options{Params: &p, Limits: &lc})

	// ended
	gone := env.NewProducer(sshPW)
	gone.Register()
	_ = gone.Control().Send(wire.New(wire.TypeUnregister))
	eventually(t, "unregister", func() bool { return env.Server.Registrations() == 0 })

	// full
	full := holdingProducer(t, env)
	hold := connect(t, env, full.ShareID)
	defer hold.Close()

	type result struct {
		name string
		b    []byte
		d    time.Duration
	}
	var rs []result
	for name, id := range map[string]string{"unknown": wire.NewID(), "ended": gone.ShareID, "full": full.ShareID, "malformed": "not-an-id"} {
		b, d := answer(t, env, id)
		rs = append(rs, result{name, b, d})
	}
	ref := rs[0]
	for _, r := range rs {
		if !bytes.Equal(r.b, ref.b) {
			t.Errorf("%s answered differently from %s:\n %q\n %q", r.name, ref.name, r.b, ref.b)
		}
		if r.d < 200*time.Millisecond || r.d > 600*time.Millisecond {
			t.Errorf("%s took %v, want the fixed 200 ms delay plus slack", r.name, r.d)
		}
		if d := r.d - ref.d; d > 150*time.Millisecond || d < -150*time.Millisecond {
			t.Errorf("%s and %s differ by %v in timing", r.name, ref.name, d)
		}
	}
	if !bytes.Contains(ref.b, []byte(wire.ReasonUnavailable)) {
		t.Fatalf("the shared answer must be unavailable: %q", ref.b)
	}
	// the answer is exactly one frame
	fr, _ := wire.Frame(wire.Refused(wire.ReasonUnavailable))
	if !bytes.Equal(ref.b, fr) {
		t.Fatalf("the answer is not the canonical unavailable frame: %q vs %q", ref.b, fr)
	}
}

func TestUnknownIDAttemptsAreRateLimitedPerAddress(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{}) // unknown-id: 10/min, burst 3
	prod := registered(t, env)
	for i := 0; i < 3; i++ {
		answer(t, env, wire.NewID())
	}
	// The address has used its burst: even a VALID id is now answered unavailable without a lookup,
	// so id guessing is bounded by the bucket and not by the network.
	b, _ := answer(t, env, prod.ShareID)
	if !bytes.Contains(b, []byte(wire.ReasonUnavailable)) {
		t.Fatalf("a rate-limited address must be answered unavailable: %q", b)
	}
}

// --- ticket 139: limits on registrations ---

func TestFailedRegistrationsAreRateLimitedBeforeTheHash(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{}) // failed-register: 5/min, burst 2
	bad := func() string {
		p := env.NewProducer(sshPW)
		p.Password = "Wrongpass1"
		r, err := p.Register()
		if err != nil {
			t.Fatal(err)
		}
		return r.Reason
	}
	if bad() != wire.ReasonBadPassword || bad() != wire.ReasonBadPassword {
		t.Fatal("the first two failures are ordinary refusals")
	}
	callsBefore := env.Verifier.Calls()
	if r := bad(); r != wire.ReasonRateLimited {
		t.Fatalf("the third failure must be rate-limited: %q", r)
	}
	if env.Verifier.Calls() != callsBefore {
		t.Fatal("a rate-limited address must be turned away BEFORE the hash runs")
	}
}

func TestPasswordFloodRunsAtMostBurstVerificationsAndTwoAtOnce(t *testing.T) {
	lc := limits.DefaultConfig()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 6000, Burst: 1000}
	env := testkit.StartServer(t, testkit.Options{Limits: &lc})
	var wg sync.WaitGroup
	var mu sync.Mutex
	reasons := map[string]int{}
	var slowest time.Duration
	for i := 0; i < 50; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			p := env.NewProducer(sshPW)
			p.Password = "Wrongpass1"
			start := time.Now()
			r, err := p.Register()
			d := time.Since(start)
			mu.Lock()
			defer mu.Unlock()
			if err != nil {
				reasons["error"]++
				return
			}
			reasons[r.Reason]++
			if r.Reason == wire.ReasonRateLimited && d > slowest {
				slowest = d
			}
		}()
	}
	wg.Wait()
	if c := env.Verifier.Calls(); c > 2 {
		t.Errorf("%d verifications ran from one address, the burst is 2", c)
	}
	if env.Verifier.Peak() > 2 {
		t.Errorf("%d verifications at once, the bound is 2", env.Verifier.Peak())
	}
	// Of 50 parallel wrong passwords at most the burst (2) is answered bad-password; every other
	// one is rate-limited or dropped by the pre-auth pool, which evicts the oldest unauthenticated
	// stream when the flood fills it - that is the pool doing its job, not a failure.
	if reasons[wire.ReasonBadPassword] > 2 || reasons[wire.ReasonRateLimited]+reasons["error"] < 48 {
		t.Errorf("the flood was not held to the burst: %v", reasons)
	}
	if slowest > time.Second {
		t.Errorf("a rate-limited registration took %v: it must not wait for a hash", slowest)
	}
}

func TestVerifierBusyAnswersCapacityAndKeepsTheToken(t *testing.T) {
	release := make(chan struct{})
	started := make(chan struct{}, 1)
	env := testkit.StartServer(t, testkit.Options{
		VerifyConc: 1, VerifyWait: 100 * time.Millisecond,
		Verify: func(string) bool {
			select {
			case started <- struct{}{}:
			default:
			}
			<-release
			return true
		},
	})
	// the only verification slot is held by a hash in flight
	first := env.NewProducer(sshPW)
	done := make(chan wire.Message, 1)
	go func() { r, _ := first.Register(); done <- r }()
	<-started

	second := env.NewProducer(sshPW)
	reply, err := second.Register()
	if err != nil || reply.Type != wire.TypeRefused || reply.Reason != wire.ReasonCapacity {
		t.Fatalf("a busy verifier must answer capacity: %v %+v", err, reply)
	}
	close(release)
	if r := <-done; r.Type != wire.TypeRegistered {
		t.Fatalf("the held verification must finish normally: %+v", r)
	}
	// our own busy state is not the client's failure: it did not cost the address a token
	for i := 0; i < 2; i++ {
		p := env.NewProducer(sshPW)
		if r, _ := p.Register(); r.Type != wire.TypeRegistered {
			t.Fatalf("registration %d after the busy spell: %+v", i, r)
		}
	}
}

// --- ticket 139 T-5: a flood from one address does not touch another ---

func TestFloodFromOneAddressLeavesAnotherUnaffected(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	// the attacker comes from 127.0.0.2, the legitimate pair from 127.0.0.1
	probe, err := net.DialTCP("tcp", &net.TCPAddr{IP: net.ParseIP("127.0.0.2")}, mustTCPAddr(t, env.Addr))
	if err != nil {
		t.Skipf("cannot originate from 127.0.0.2 on this host: %v", err)
	}
	probe.Close()

	var wg sync.WaitGroup
	sem := make(chan struct{}, 40)
	for i := 0; i < 1000; i++ {
		wg.Add(1)
		sem <- struct{}{}
		go func() {
			defer wg.Done()
			defer func() { <-sem }()
			c, err := net.DialTCP("tcp", &net.TCPAddr{IP: net.ParseIP("127.0.0.2")}, mustTCPAddr(t, env.Addr))
			if err != nil {
				return
			}
			defer c.Close()
			m := wire.New(wire.TypeConnect)
			m.ShareID = wire.NewID()
			frame, _ := wire.Frame(m)
			_, _ = c.Write(frame)
			_ = c.SetReadDeadline(time.Now().Add(time.Second))
			_, _ = io.Copy(io.Discard, c)
		}()
	}

	// meanwhile a legitimate producer registers and a consumer bridges
	prod := registered(t, env)
	out, err := testkit.SSHExec(connect(t, env, prod.ShareID), "u", sshPW, "during-flood")
	if err != nil || out != "echo:during-flood" {
		t.Fatalf("the legitimate pair was affected by the flood: %q %v", out, err)
	}
	wg.Wait()
	// and still works after it
	out, err = testkit.SSHExec(connect(t, env, prod.ShareID), "u", sshPW, "after-flood")
	if err != nil || out != "echo:after-flood" {
		t.Fatalf("after the flood: %q %v", out, err)
	}
}

func mustTCPAddr(t *testing.T, s string) *net.TCPAddr {
	t.Helper()
	a, err := net.ResolveTCPAddr("tcp", s)
	if err != nil {
		t.Fatal(err)
	}
	return a
}

// --- logs and disk ---

func TestLogsNameSessionsByHashOnly(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	prod := registered(t, env)
	out, err := testkit.SSHExec(connect(t, env, prod.ShareID), "u", sshPW, "x")
	if err != nil || out != "echo:x" {
		t.Fatal(err)
	}
	_ = prod.Control().Send(wire.New(wire.TypeUnregister))
	eventually(t, "unregister", func() bool { return env.Server.Registrations() == 0 })
	lines := env.Log.Lines()
	if len(lines) == 0 {
		t.Fatal("the server logged nothing")
	}
	joined := strings.Join(lines, "\n")
	if !strings.Contains(joined, "share=") {
		t.Fatalf("session lines must name the session by its 8-hex fingerprint:\n%s", joined)
	}
	// AssertLogsClean (registered by StartServer) also runs at cleanup; call it now for a clear message.
	env.AssertLogsClean(t)
}

var _ = rendezvous.DefaultParams
