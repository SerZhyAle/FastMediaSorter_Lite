package rendezvous_test

import (
	"bufio"
	"crypto/tls"
	"encoding/json"
	"errors"
	"io"
	"net"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/tlsid"
	"fms/exchange-server/internal/wire"
)

// These tests are ticket 161: the connection layer and the stream state machine, proved against a
// fake Backend. The real one (accounts, directory, tunnels) has its own tests in engine_*_test.go.

const (
	fakeToken = "dGhpcy1pcy1hLTMyLWJ5dGUtdG9rZW4tZm9yLXRlc3Q"
	devA      = "AAAAAAAAAAAAAAAAAAAAAA"
	devB      = "BBBBBBBBBBBBBBBBBBBBBA"
)

// fakeBackend authenticates one fixed login and records what the state machine hands it.
type fakeBackend struct {
	mu      sync.Mutex
	enrolls int
	hellos  int
	sweeps  []time.Time
	closed  bool

	controlFirst chan wire.Envelope
	controlDone  chan struct{}
	attached     chan fakeCall
	connected    chan fakeCall
	uploaded     chan fakeCall
	clipPut      chan fakeCall
	clipGet      chan fakeCall
	listened     chan string
	rawGot       chan string

	// pushBig makes Control send this many bytes of directory to the client.
	pushBig int

	// helloGate, when set, holds every Hello until it is closed: a stream that is being judged.
	helloGate chan struct{}
}

type fakeCall struct {
	sess *rendezvous.Session
	env  wire.Envelope
}

func newFake() *fakeBackend {
	return &fakeBackend{
		controlFirst: make(chan wire.Envelope, 4), controlDone: make(chan struct{}, 4),
		attached: make(chan fakeCall, 4), connected: make(chan fakeCall, 4), uploaded: make(chan fakeCall, 4),
		clipPut: make(chan fakeCall, 4), clipGet: make(chan fakeCall, 4),
		listened: make(chan string, 4), rawGot: make(chan string, 4),
	}
}

func (f *fakeBackend) body() wire.WelcomeBody {
	return wire.WelcomeBody{Account: "alice", KeepaliveSeconds: 30, Features: []string{"directory"}, ServerVersion: "test"}
}

func (f *fakeBackend) Enroll(st *rendezvous.Stream, e *wire.Enroll) rendezvous.Result {
	f.mu.Lock()
	f.enrolls++
	f.mu.Unlock()
	if e.Login != "alice" || e.Password != "Passw0rdTest" {
		return rendezvous.Result{Refuse: wire.ReasonBadCredentials}
	}
	return rendezvous.Result{
		Session: &rendezvous.Session{Account: "alice", DeviceID: e.DeviceID, Keepalive: 30 * time.Second},
		Reply:   &wire.Enrolled{DeviceToken: fakeToken, WelcomeBody: f.body()},
	}
}

func (f *fakeBackend) Register(st *rendezvous.Stream, e *wire.Register) rendezvous.Result {
	f.mu.Lock()
	f.enrolls++
	f.mu.Unlock()
	if e.Login != "alice" || e.Password != "Passw0rdTest" {
		return rendezvous.Result{Refuse: wire.ReasonBadCredentials}
	}
	return rendezvous.Result{
		Session: &rendezvous.Session{Account: "alice", DeviceID: e.DeviceID, Keepalive: 30 * time.Second},
		Reply:   &wire.Enrolled{DeviceToken: fakeToken, WelcomeBody: f.body()},
	}
}

func (f *fakeBackend) Hello(st *rendezvous.Stream, e *wire.Hello) rendezvous.Result {
	f.mu.Lock()
	f.hellos++
	f.mu.Unlock()
	if f.helloGate != nil {
		<-f.helloGate
	}
	if e.DeviceToken != fakeToken {
		return rendezvous.Result{Refuse: wire.ReasonBadCredentials}
	}
	return rendezvous.Result{
		Session: &rendezvous.Session{Account: "alice", DeviceID: e.DeviceID, Keepalive: 30 * time.Second},
		Reply:   &wire.Welcome{WelcomeBody: f.body()},
	}
}

func (f *fakeBackend) Control(cs *rendezvous.ControlStream, first wire.Envelope) {
	f.controlFirst <- first
	defer func() { f.controlDone <- struct{}{} }()
	if f.pushBig > 0 {
		pad := json.RawMessage(`{"x":"` + strings.Repeat("a", f.pushBig) + `"}`)
		if err := cs.Send(&wire.Directory{Resources: []json.RawMessage{pad}}); err != nil {
			return
		}
	}
	e := first
	for {
		switch e.(type) {
		case *wire.Keepalive:
			if cs.Send(&wire.Keepalive{}) != nil {
				return
			}
		case *wire.Bye:
			return
		}
		var err error
		if e, err = cs.Next(); err != nil {
			return
		}
	}
}

func (f *fakeBackend) Attach(st *rendezvous.Stream, sess *rendezvous.Session, e *wire.Attach) {
	defer st.Close()
	f.attached <- fakeCall{sess, e}
}

func (f *fakeBackend) Connect(st *rendezvous.Stream, sess *rendezvous.Session, e *wire.Connect) {
	defer st.Close()
	f.connected <- fakeCall{sess, e}
	if err := st.Send(&wire.Connected{}); err != nil {
		return
	}
	buf := make([]byte, 5)
	if _, err := io.ReadFull(st.Conn(), buf); err == nil {
		f.rawGot <- string(buf)
	}
}

func (f *fakeBackend) Upload(st *rendezvous.Stream, sess *rendezvous.Session, e *wire.Upload) {
	defer st.Close()
	f.uploaded <- fakeCall{sess, e}
	_ = st.Send(&wire.UploadAccepted{})
}

func (f *fakeBackend) ClipboardPut(st *rendezvous.Stream, sess *rendezvous.Session, e *wire.ClipboardPut) {
	defer st.Close()
	f.clipPut <- fakeCall{sess, e}
	_ = st.Send(&wire.ClipboardReady{})
}

func (f *fakeBackend) ClipboardGet(st *rendezvous.Stream, sess *rendezvous.Session, e *wire.ClipboardGet) {
	defer st.Close()
	f.clipGet <- fakeCall{sess, e}
	_ = st.Send(&wire.ClipboardEmpty{})
}

func (f *fakeBackend) Listen(st *rendezvous.Stream, id string) {
	defer st.Close()
	f.listened <- id
	_ = st.WriteRaw([]byte("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"))
}

func (f *fakeBackend) Sweep(now time.Time) {
	f.mu.Lock()
	f.sweeps = append(f.sweeps, now)
	f.mu.Unlock()
}

func (f *fakeBackend) Close() {
	f.mu.Lock()
	f.closed = true
	f.mu.Unlock()
}

func (f *fakeBackend) counts() (enrolls, hellos int) {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.enrolls, f.hellos
}

// layerEnv is a Server over a fake backend on loopback.
type layerEnv struct {
	srv  *rendezvous.Server
	addr string
	fp   string
	be   *fakeBackend
	log  *logx.Capture
}

func startLayer(t *testing.T, mutate func(*rendezvous.Config)) *layerEnv {
	t.Helper()
	dir := t.TempDir()
	id, err := tlsid.LoadOrCreate(filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem"), nil)
	if err != nil {
		t.Fatal(err)
	}
	e := &layerEnv{be: newFake(), fp: id.Fingerprint, log: logx.NewCapture()}
	p := testkit.TestParams()
	cfg := rendezvous.Config{
		Params: p, TLS: &tls.Config{Certificates: []tls.Certificate{id.Cert}, MinVersion: tls.VersionTLS12},
		Limiter: limits.NewLimiter(limits.DefaultConfig(), nil), PreAuth: limits.NewPreAuth(16),
		Backend: e.be, Log: e.log.Logger,
	}
	if mutate != nil {
		mutate(&cfg)
	}
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	e.addr = ln.Addr().String()
	e.srv = rendezvous.New(cfg)
	go func() { _ = e.srv.Serve(ln) }()
	t.Cleanup(e.srv.Close)
	return e
}

func (e *layerEnv) tls(t *testing.T) *testkit.Conn {
	t.Helper()
	c, err := testkit.DialTLS(e.addr, e.fp)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c
}

func (e *layerEnv) plain(t *testing.T) *testkit.Conn {
	t.Helper()
	c, err := testkit.DialPlain(e.addr)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c
}

func enrollFrame() *wire.Enroll {
	return &wire.Enroll{Login: "alice", Password: "Passw0rdTest", DeviceID: devA, DeviceName: "Phone", Product: "fms-android"}
}

// enrolled opens a TLS stream and enrolls; the stream is then authenticated.
func (e *layerEnv) enrolled(t *testing.T) *testkit.Conn {
	t.Helper()
	c := e.tls(t)
	if err := c.Send(enrollFrame()); err != nil {
		t.Fatal(err)
	}
	r, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := r.(*wire.Enrolled); !ok {
		t.Fatalf("enroll: %+v", r)
	}
	c.Authenticated()
	return c
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

// refusal reads one envelope and requires a refusal with that reason, then a closed stream.
func refusal(t *testing.T, c *testkit.Conn, want wire.Reason) {
	t.Helper()
	r, err := c.Recv(3 * time.Second)
	f, ok := r.(*wire.Refused)
	if err != nil || !ok || f.Reason != string(want) {
		t.Fatalf("want refused %s, got %v %+v", want, err, r)
	}
	if f.SchemaVersion != wire.SchemaVersion {
		t.Fatalf("the refusal carries schemaVersion %d", f.SchemaVersion)
	}
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("the stream must be closed after the refusal")
	}
}

func noCall(t *testing.T, ch chan fakeCall, what string) {
	t.Helper()
	select {
	case <-ch:
		t.Fatalf("%s was called", what)
	case <-time.After(150 * time.Millisecond):
	}
}

// --- the first-byte split ---

func TestTheFirstByteSplitReachesTheRightOpening(t *testing.T) {
	env := startLayer(t, nil)

	// 0x16 -> TLS, then 0x00 -> an envelope stream: enroll works
	c := env.enrolled(t)
	_ = c

	// 0x00 outside TLS -> a plain envelope stream, answered only with a refusal
	p := env.plain(t)
	_ = p.Send(enrollFrame())
	refusal(t, p, wire.ReasonTLSRequired)

	// inside TLS an ASCII letter -> HTTP, handed to the backend for a good request
	tc := env.tls(t)
	_, _ = tc.Write([]byte("GET /v2/b/" + devB + "/stream HTTP/1.1\r\nHost: x\r\n\r\n"))
	br := bufio.NewReader(tc)
	_ = tc.SetReadDeadline(time.Now().Add(3 * time.Second))
	line, err := br.ReadString('\n')
	if err != nil || !strings.HasPrefix(line, "HTTP/1.1 200") {
		t.Fatalf("a good GET: %q %v", line, err)
	}
	select {
	case id := <-env.be.listened:
		if id != devB {
			t.Fatalf("the backend heard of broadcast %q", id)
		}
	case <-time.After(time.Second):
		t.Fatal("the backend never saw the listen request")
	}
}

func TestAnyOtherFirstByteIsClosedWithoutAnAnswer(t *testing.T) {
	env := startLayer(t, nil)
	for name, b := range map[string][]byte{
		"0x80":         {0x80, 0x01, 0x02},
		"a letter":     []byte("GET / HTTP/1.1\r\nHost: x\r\n\r\n"), // outside TLS an HTTP request is not an opening
		"0x01":         {0x01},
		"0xff":         {0xff, 0xff, 0xff, 0xff},
		"a null byte+": {0x00, 0x00, 0x00}, // a plain frame that never finishes: dropped by the first-frame deadline
	} {
		c, err := net.Dial("tcp", env.addr)
		if err != nil {
			t.Fatal(err)
		}
		_, _ = c.Write(b)
		_ = c.SetReadDeadline(time.Now().Add(4 * time.Second))
		n, err := c.Read(make([]byte, 64))
		if n != 0 || err == nil {
			t.Errorf("%s: got %d bytes and %v, want a silent close", name, n, err)
		}
		_ = c.Close()
	}
	// inside TLS a byte that is neither 0x00 nor a letter closes too
	tc := env.tls(t)
	_, _ = tc.Write([]byte{0x80, 0x01})
	if !readsEOF(tc.Conn, 3*time.Second) {
		t.Error("a stray first byte inside TLS must close the stream")
	}
}

func TestHTTPOpeningRules(t *testing.T) {
	env := startLayer(t, nil)
	do := func(req string) string {
		tc := env.tls(t)
		_, _ = tc.Write([]byte(req))
		_ = tc.SetReadDeadline(time.Now().Add(3 * time.Second))
		line, _ := bufio.NewReader(tc).ReadString('\n')
		return strings.TrimSpace(line)
	}
	good := "/v2/b/" + devB + "/stream"
	for name, c := range map[string]struct{ req, want string }{
		"any other path":             {"GET /nope HTTP/1.1\r\nHost: x\r\n\r\n", "HTTP/1.1 404 Not Found"},
		"any other path, any method": {"POST /nope HTTP/1.1\r\nHost: x\r\n\r\n", "HTTP/1.1 404 Not Found"},
		"a malformed broadcast id":   {"GET /v2/b/short/stream HTTP/1.1\r\nHost: x\r\n\r\n", "HTTP/1.1 404 Not Found"},
		"HEAD on the listen URL":     {"HEAD " + good + " HTTP/1.1\r\nHost: x\r\n\r\n", "HTTP/1.1 405 Method Not Allowed"},
		"POST on the listen URL":     {"POST " + good + " HTTP/1.1\r\nHost: x\r\n\r\n", "HTTP/1.1 405 Method Not Allowed"},
	} {
		if got := do(c.req); got != c.want {
			t.Errorf("%s: got %q, want %q", name, got, c.want)
		}
	}
	select {
	case id := <-env.be.listened:
		t.Fatalf("a refused request reached the backend: %q", id)
	case <-time.After(100 * time.Millisecond):
	}
	// a head that never ends is dropped
	tc := env.tls(t)
	_, _ = tc.Write([]byte("GET " + good + " HTTP/1.1\r\n" + strings.Repeat("X-Pad: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n", 400)))
	if !readsEOF(tc.Conn, 3*time.Second) {
		t.Error("an oversize request head must close the stream")
	}
}

// --- what a plain stream may do ---

func TestPlainStreamIsAnsweredOnlyWithARefusal(t *testing.T) {
	env := startLayer(t, nil)
	hello := &wire.Hello{DeviceID: devA, DeviceToken: fakeToken}
	cases := map[string]wire.Envelope{
		"enroll":            enrollFrame(),
		"hello":             hello,
		"connect shareId":   &wire.Connect{ShareID: devB},
		"connect broadcast": &wire.Connect{BroadcastID: devB, Scheme: "rtsp"},
		"connect resource":  &wire.Connect{ResourceID: devB},
	}
	for name, e := range cases {
		c := env.plain(t)
		if err := c.Send(e); err != nil {
			t.Fatal(err)
		}
		t.Run(name, func(t *testing.T) { refusal(t, c, wire.ReasonTLSRequired) })
	}
	if en, h := env.be.counts(); en != 0 || h != 0 {
		t.Fatalf("a plain stream reached the backend: %d enrolls, %d hellos", en, h)
	}
	noCall(t, env.be.connected, "Connect")
	// every other envelope is invalid in the state and closes without an answer
	for _, e := range []wire.Envelope{&wire.Keepalive{}, &wire.List{}, &wire.Publish{Resource: json.RawMessage(`{}`)}, &wire.Attach{TunnelID: devA}} {
		c := env.plain(t)
		_ = c.Send(e)
		if !readsEOF(c.Conn, 2*time.Second) {
			t.Errorf("%T on a plain stream must close it", e)
		}
	}
}

func TestSchemaVersionRules(t *testing.T) {
	env := startLayer(t, nil)
	legacy := testkit.RawFrame(`{"schemaVersion":1,"type":"register","password":"x","shareId":"` + devA + `"}`)

	// the legacy wire, plain and TLS: refused with the server's own schemaVersion, then closed
	p := env.plain(t)
	_ = p.SendRaw(legacy)
	refusal(t, p, wire.ReasonVersionUnsupported)
	tc := env.tls(t)
	_ = tc.SendRaw(legacy)
	refusal(t, tc, wire.ReasonVersionUnsupported)

	// any other value, or none, closes without an answer
	for _, body := range []string{
		`{"schemaVersion":3,"type":"keepalive"}`, `{"type":"keepalive"}`, `{"schemaVersion":0,"type":"enroll"}`,
		`{"schemaVersion":"2","type":"keepalive"}`, `not json`, `[]`,
	} {
		for name, c := range map[string]*testkit.Conn{"plain": env.plain(t), "tls": env.tls(t)} {
			_ = c.SendRaw(testkit.RawFrame(body))
			if !readsEOF(c.Conn, 2*time.Second) {
				t.Errorf("%s %s: must close without an answer", name, body)
			}
		}
	}

	// an enroll missing a required member is malformed: closed without an answer, the backend never asked
	tc2 := env.tls(t)
	_ = tc2.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"enroll","login":"alice","password":"x"}`))
	if !readsEOF(tc2.Conn, 2*time.Second) {
		t.Error("a malformed enroll must close the stream")
	}
	if en, _ := env.be.counts(); en != 0 {
		t.Errorf("a malformed enroll reached the backend")
	}
}

func TestUnknownTypesAndMembersAreIgnored(t *testing.T) {
	env := startLayer(t, nil)
	c := env.tls(t)
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"hologram","x":1}`))
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"enroll","login":"alice","password":"Passw0rdTest","deviceId":"` + devA + `","deviceName":"P","product":"x","future":{"a":[1]}}`))
	r, err := c.Recv(3 * time.Second)
	if _, ok := r.(*wire.Enrolled); err != nil || !ok {
		t.Fatalf("an unknown type before the real envelope must be ignored: %v %+v", err, r)
	}
	// and a known envelope that is invalid in the opening state closes the stream
	c2 := env.tls(t)
	_ = c2.Send(&wire.Keepalive{})
	if !readsEOF(c2.Conn, 2*time.Second) {
		t.Error("keepalive as a first frame must close the stream")
	}
}

func TestTooManyUnknownTypesCloseTheStream(t *testing.T) {
	env := startLayer(t, nil)
	c := env.tls(t)
	for i := 0; i < 80; i++ {
		if c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"hologram"}`)) != nil {
			break
		}
	}
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Error("a stream of nothing but unknown types must be closed")
	}
}

// --- frame limits follow the stream's state ---

func TestFrameLimitsFollowTheStreamState(t *testing.T) {
	env := startLayer(t, nil)
	pad := func(n int, extra string) string {
		return `{"schemaVersion":2,"type":"enroll","login":"alice","password":"Passw0rdTest","deviceId":"` + devA +
			`","deviceName":"P","product":"x","pad":"` + strings.Repeat("a", n) + `"` + extra + `}`
	}
	// pre-authentication: a frame of exactly 16384 bytes passes, one more closes the stream
	exact := pad(wire.MaxFrame-len(pad(0, "")), "")
	if len(exact) != wire.MaxFrame {
		t.Fatalf("test frame is %d bytes", len(exact))
	}
	c := env.tls(t)
	_ = c.SendRaw(testkit.RawFrame(exact))
	if r, err := c.Recv(3 * time.Second); err != nil {
		t.Fatalf("a frame of %d bytes before authentication: %v", wire.MaxFrame, err)
	} else if _, ok := r.(*wire.Enrolled); !ok {
		t.Fatalf("%+v", r)
	}
	over := env.tls(t)
	_ = over.SendRaw(testkit.RawFrame(pad(wire.MaxFrame-len(pad(0, ""))+1, "")))
	if !readsEOF(over.Conn, 2*time.Second) {
		t.Error("a frame above 16384 bytes before authentication must close the stream")
	}

	// after authentication, in both directions, up to 262144
	env.be.pushBig = 200 * 1024
	auth := env.enrolled(t)
	big := `{"schemaVersion":2,"type":"list","pad":"` + strings.Repeat("a", 150*1024) + `"}`
	_ = auth.SendRaw(testkit.RawFrame(big))
	first := <-env.be.controlFirst
	if _, ok := first.(*wire.List); !ok {
		t.Fatalf("a 150 KiB frame after authentication: %+v", first)
	}
	got, err := auth.Recv(5 * time.Second)
	if d, ok := got.(*wire.Directory); err != nil || !ok || len(d.Resources) != 1 || len(d.Resources[0]) < 200*1024 {
		t.Fatalf("the server must be able to push a 200 KiB frame to an authenticated stream: %v %T", err, got)
	}
	tooBig := env.enrolled(t)
	_ = tooBig.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"list","pad":"` + strings.Repeat("a", wire.MaxFrameAuth) + `"}`))
	if !readsEOF(tooBig.Conn, 2*time.Second) {
		t.Error("a frame above 262144 bytes after authentication must close the stream")
	}
}

// --- what the first envelope after welcome decides ---

func TestTheFirstEnvelopeAfterAuthenticationFixesTheRole(t *testing.T) {
	env := startLayer(t, nil)

	c := env.enrolled(t)
	_ = c.Send(&wire.Attach{TunnelID: devB})
	call := <-env.be.attached
	if call.sess == nil || call.sess.DeviceID != devA || call.env.(*wire.Attach).TunnelID != devB {
		t.Fatalf("attach: %+v", call)
	}

	c = env.enrolled(t)
	_ = c.Send(&wire.Connect{ResourceID: devB})
	call = <-env.be.connected
	if call.sess == nil || call.env.(*wire.Connect).ResourceID != devB {
		t.Fatalf("connect: %+v", call)
	}
	if r, err := c.Recv(2 * time.Second); err != nil {
		t.Fatal(err)
	} else if _, ok := r.(*wire.Connected); !ok {
		t.Fatalf("%+v", r)
	}

	c = env.enrolled(t)
	_ = c.Send(&wire.Upload{BroadcastID: devB, ContentType: "audio/aac"})
	call = <-env.be.uploaded
	if call.sess == nil || call.env.(*wire.Upload).ContentType != "audio/aac" {
		t.Fatalf("upload: %+v", call)
	}

	for _, first := range []wire.Envelope{&wire.List{}, &wire.Subscribe{}, &wire.Keepalive{}, &wire.PairingRequest{}} {
		c = env.enrolled(t)
		_ = c.Send(first)
		got := <-env.be.controlFirst
		if got.TypeName() != first.TypeName() {
			t.Fatalf("%T as the first envelope made %T", first, got)
		}
	}
	// an unknown type before the deciding envelope is skipped
	c = env.enrolled(t)
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"hologram"}`))
	_ = c.Send(&wire.List{})
	if got := <-env.be.controlFirst; got.TypeName() != wire.TypeList {
		t.Fatalf("%T", got)
	}
}

func TestAnAuthenticatedStreamThatSaysNothingIsClosed(t *testing.T) {
	p := testkit.TestParams()
	p.FirstFrameTimeout = 300 * time.Millisecond
	env := startLayer(t, func(c *rendezvous.Config) { c.Params = p })
	c := env.enrolled(t)
	start := time.Now()
	if !readsEOF(c.Conn, 3*time.Second) {
		t.Fatal("a stream that never says what it is must be closed")
	}
	if d := time.Since(start); d > 2*time.Second {
		t.Fatalf("closed after %v", d)
	}
}

func TestControlStreamKeepsRunningAndTheBackendSeesEveryEnvelope(t *testing.T) {
	env := startLayer(t, nil)
	c := env.enrolled(t)
	_ = c.Send(&wire.Keepalive{})
	<-env.be.controlFirst
	if r, err := c.Recv(2 * time.Second); err != nil {
		t.Fatal(err)
	} else if _, ok := r.(*wire.Keepalive); !ok {
		t.Fatalf("%+v", r)
	}
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"hologram","x":1}`))
	_ = c.Send(&wire.Keepalive{})
	if _, err := c.Recv(2 * time.Second); err != nil {
		t.Fatalf("the control stream must survive an unknown envelope: %v", err)
	}
	_ = c.Send(&wire.Bye{})
	select {
	case <-env.be.controlDone:
	case <-time.After(2 * time.Second):
		t.Fatal("the backend's control loop did not end on bye")
	}
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("the stream must be closed once the control loop returns")
	}
}

// --- outsiders and the bytes behind connect ---

func TestOutsiderConnectNeedsNoHelloButNeverByResourceId(t *testing.T) {
	env := startLayer(t, nil)
	for _, e := range []*wire.Connect{{ShareID: devB}, {BroadcastID: devB, Scheme: "http"}} {
		c := env.tls(t)
		_ = c.Send(e)
		call := <-env.be.connected
		if call.sess != nil {
			t.Fatalf("an outsider has no session: %+v", call.sess)
		}
		if r, err := c.Recv(2 * time.Second); err != nil {
			t.Fatal(err)
		} else if _, ok := r.(*wire.Connected); !ok {
			t.Fatalf("%+v", r)
		}
	}
	c := env.tls(t)
	_ = c.Send(&wire.Connect{ResourceID: devB})
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("a resourceId is never a capability: connect by it without hello closes the stream")
	}
	noCall(t, env.be.connected, "Connect")
}

func TestBytesRightBehindConnectAreNotLost(t *testing.T) {
	env := startLayer(t, nil)
	frame, _ := wire.Frame(&wire.Connect{ShareID: devB})

	// outsider, TLS: the frame and the raw bytes in one write
	c := env.tls(t)
	_ = c.SendRaw(append(append([]byte{}, frame...), []byte("HELLO")...))
	if got := <-env.be.rawGot; got != "HELLO" {
		t.Fatalf("outsider: got %q", got)
	}

	// authenticated: the connect frame and the raw bytes together
	a := env.enrolled(t)
	_ = a.SendRaw(append(append([]byte{}, frame...), []byte("WORLD")...))
	if got := <-env.be.rawGot; got != "WORLD" {
		t.Fatalf("authenticated: got %q", got)
	}
}

// --- timeouts, the pre-auth pool, the connection rate ---

func TestSilentClientHitsThePeekDeadline(t *testing.T) {
	env := startLayer(t, nil)
	c, _ := net.Dial("tcp", env.addr)
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
	env := startLayer(t, func(c *rendezvous.Config) { c.Params = p })
	c, _ := net.Dial("tcp", env.addr)
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
	env := startLayer(t, func(c *rendezvous.Config) { c.Params = p })
	c, _ := net.Dial("tcp", env.addr)
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

func TestIdleConnectionsCannotStarveTheServer(t *testing.T) {
	// a pool of 3 pre-auth slots and a connection rate that lets the flood in
	lc := limits.DefaultConfig()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 6000, Burst: 1000}
	env := startLayer(t, func(c *rendezvous.Config) {
		c.PreAuth = limits.NewPreAuth(3)
		c.Limiter = limits.NewLimiter(lc, nil)
		c.Params.PeekTimeout = 30 * time.Second
	})
	var idle []net.Conn
	for i := 0; i < 12; i++ {
		c, err := net.Dial("tcp", env.addr)
		if err != nil {
			t.Fatal(err)
		}
		idle = append(idle, c)
		defer c.Close()
	}
	time.Sleep(100 * time.Millisecond)
	// a real client still gets through: the oldest unauthenticated streams were evicted
	c := env.enrolled(t)
	_ = c
	evicted := 0
	for _, c := range idle[:6] {
		if readsEOF(c, 200*time.Millisecond) {
			evicted++
		}
	}
	if evicted < 3 {
		t.Fatalf("only %d of the oldest idle connections were evicted", evicted)
	}
}

func TestConnectionRatePerAddress(t *testing.T) {
	lc := limits.DefaultConfig()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 1, Burst: 3}
	env := startLayer(t, func(c *rendezvous.Config) { c.Limiter = limits.NewLimiter(lc, nil) })
	ok, dropped := 0, 0
	for i := 0; i < 8; i++ {
		c, err := net.Dial("tcp", env.addr)
		if err != nil {
			t.Fatal(err)
		}
		_, _ = c.Write(testkit.RawFrame(`{"schemaVersion":2,"type":"keepalive"}`))
		if readsEOF(c, 300*time.Millisecond) {
			dropped++
		} else {
			ok++
		}
		_ = c.Close()
	}
	// every connection is closed by the keepalive (invalid in the state) - what counts is the log
	_ = ok
	_ = dropped
	found := false
	for _, l := range env.log.Lines() {
		if strings.Contains(l, "connection-refused") && strings.Contains(l, "rate-limited") {
			found = true
		}
	}
	if !found {
		t.Fatalf("a burst of 8 connections against a bucket of 3 was not rate-limited:\n%s", strings.Join(env.log.Lines(), "\n"))
	}
}

func TestSweepRunsWithTheServersClockAndCloseTellsTheBackend(t *testing.T) {
	var mu sync.Mutex
	now := time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)
	env := startLayer(t, func(c *rendezvous.Config) {
		c.Now = func() time.Time { mu.Lock(); defer mu.Unlock(); return now }
	})
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		env.be.mu.Lock()
		n := len(env.be.sweeps)
		env.be.mu.Unlock()
		if n > 0 {
			break
		}
		time.Sleep(20 * time.Millisecond)
	}
	env.be.mu.Lock()
	if len(env.be.sweeps) == 0 || !env.be.sweeps[0].Equal(now) {
		t.Fatalf("sweeps: %v", env.be.sweeps)
	}
	env.be.mu.Unlock()
	c := env.enrolled(t)
	env.srv.Close()
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("closing the server must close its streams")
	}
	env.be.mu.Lock()
	closed := env.be.closed
	env.be.mu.Unlock()
	if !closed {
		t.Fatal("the backend was not told")
	}
}

func TestNoListenerStateSurvivesAFailedHandshake(t *testing.T) {
	env := startLayer(t, nil)
	// a client that speaks TLS 1.0 only is refused at the handshake
	c, err := tls.Dial("tcp", env.addr, &tls.Config{InsecureSkipVerify: true, MaxVersion: tls.VersionTLS10})
	if err == nil {
		_ = c.Close()
		t.Fatal("TLS below 1.2 must be refused")
	}
	// and a client that offers an ALPN protocol is still served (none is selected)
	cc, err := tls.Dial("tcp", env.addr, &tls.Config{InsecureSkipVerify: true, NextProtos: []string{"h2"}})
	if err != nil {
		t.Fatal(err)
	}
	defer cc.Close()
	if st := cc.ConnectionState(); st.NegotiatedProtocol != "" || st.Version < tls.VersionTLS12 {
		t.Fatalf("alpn %q version %x", st.NegotiatedProtocol, st.Version)
	}
}
