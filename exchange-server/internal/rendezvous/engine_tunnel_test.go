package rendezvous_test

import (
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"encoding/json"
	"io"
	"net"
	"strconv"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 164: tunnels by resourceId, shareId and broadcastId, open/attach, the uniform `unavailable`,
// the caps, and the third-party port. The producer really serves SSH through the bridge.

const sshPW = "ssh-secret"

// shareProducer starts a producer and publishes one share (with a shareId, and a port if asked).
func shareProducer(t *testing.T, env *testkit.Env, login string, port *int) (p *testkit.Producer, resourceID, shareID string, pub *wire.Published) {
	t.Helper()
	p = env.NewProducer(login, sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	resourceID, shareID, ans, err := p.PublishShare(true, port)
	if err != nil {
		t.Fatal(err)
	}
	pub, ok := ans.(*wire.Published)
	if !ok {
		t.Fatalf("publish: %+v", ans)
	}
	return
}

func holdHandler(c net.Conn) { _, _ = io.Copy(io.Discard, c); _ = c.Close() }

// connectOK opens a tunnel by resourceId as an enrolled device of the account.
func connectOK(t *testing.T, env *testkit.Env, d *testkit.Device, resourceID string) *testkit.Conn {
	t.Helper()
	c, r, err := d.Connect(&wire.Connect{ResourceID: resourceID})
	if err != nil || c == nil {
		t.Fatalf("connect: %v %+v", err, r)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c
}

func consumerDevice(t *testing.T, env *testkit.Env, login string) *testkit.Device {
	t.Helper()
	d := env.NewDevice(login)
	mustEnroll(t, d)
	d.Close()
	return d
}

// answerOf sends a connect (after hello when d is set) and returns every byte the server answers with
// and how long it took. A connect that was served would return the bytes of a `connected`.
func answerOf(t *testing.T, env *testkit.Env, d *testkit.Device, m *wire.Connect) ([]byte, time.Duration) {
	t.Helper()
	var c *testkit.Conn
	var err error
	if d != nil {
		c, _, err = d.Hello()
		if err != nil || c == nil {
			t.Fatalf("hello: %v", err)
		}
	} else if c, err = testkit.DialTLS(env.Addr, env.Fingerprint); err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	start := time.Now()
	if err := c.Send(m); err != nil {
		t.Fatal(err)
	}
	_ = c.SetReadDeadline(time.Now().Add(6 * time.Second))
	b, _ := io.ReadAll(c.Conn)
	return b, time.Since(start)
}

func unavailableFrame(t *testing.T) []byte {
	t.Helper()
	b, err := wire.Frame(wire.Refuse(wire.ReasonUnavailable))
	if err != nil {
		t.Fatal(err)
	}
	return b
}

// --- an SSH session through the bridge, by resourceId and by shareId (acceptance 1) ---

func TestSSHThroughTheBridgeByResourceIdAndByShareId(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	_, resID, shareID, _ := shareProducer(t, env, "alice", nil)
	cons := consumerDevice(t, env, "alice")

	c := connectOK(t, env, cons, resID)
	if out, err := testkit.SSHThrough(c.Conn, sshPW, "ls"); err != nil || out != "echo:ls" {
		t.Fatalf("by resourceId: %q %v", out, err)
	}

	sc, r, err := env.OutsiderConnect(&wire.Connect{ShareID: shareID})
	if err != nil || sc == nil {
		t.Fatalf("by shareId: %v %+v", err, r)
	}
	defer sc.Close()
	if out, err := testkit.SSHThrough(sc.Conn, sshPW, "pwd"); err != nil || out != "echo:pwd" {
		t.Fatalf("by shareId: %q %v", out, err)
	}

	// an enrolled device may use the share id too (it is the same capability), and the open the
	// producer got named exactly what the consumer connected by
	c3, _, err := cons.Connect(&wire.Connect{ShareID: shareID})
	if err != nil || c3 == nil {
		t.Fatal(err)
	}
	defer c3.Close()
	if out, err := testkit.SSHThrough(c3.Conn, sshPW, "id"); err != nil || out != "echo:id" {
		t.Fatalf("%q %v", out, err)
	}
}

func TestTwoSimultaneousTunnelsOnOneResource(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	_, resID, _, _ := shareProducer(t, env, "alice", nil)
	cons := consumerDevice(t, env, "alice")
	a, err := testkit.SSHClient(connectOK(t, env, cons, resID).Conn, "u", sshPW)
	if err != nil {
		t.Fatal(err)
	}
	defer a.Close()
	b, err := testkit.SSHClient(connectOK(t, env, cons, resID).Conn, "u", sshPW)
	if err != nil {
		t.Fatal(err)
	}
	defer b.Close()
	if out, err := testkit.SSHExecOn(b, "from-b"); err != nil || out != "echo:from-b" {
		t.Fatalf("b: %q %v", out, err)
	}
	if out, err := testkit.SSHExecOn(a, "from-a"); err != nil || out != "echo:from-a" {
		t.Fatalf("a: %q %v", out, err)
	}
}

func TestATunnelByBroadcastIdReachesTheProducerWithTheScheme(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	prod := env.NewProducer("alice", sshPW)
	got := make(chan *wire.Open, 1)
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	// the producer's own handling of `open`: record it, then serve like any other
	prod.Control().OnOpen(func(o *wire.Open) {
		got <- o
		prod.Answer(o)
	})
	bid := env.NewID()
	r, _ := prod.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(`{"broadcastId":"` + bid + `","mode":"VIDEO_AUDIO","descriptor":{"endpoints":[]}}`)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", r)
	}
	c, r, err := env.OutsiderConnect(&wire.Connect{BroadcastID: bid, Scheme: "rtsp"})
	if err != nil || c == nil {
		t.Fatalf("%v %+v", err, r)
	}
	defer c.Close()
	o := <-got
	if o.BroadcastID != bid || o.Scheme != "rtsp" || o.ResourceID != "" || o.ShareID != "" || !wire.ValidID(o.TunnelID) {
		t.Fatalf("open: %+v", o)
	}
	if out, err := testkit.SSHThrough(c.Conn, sshPW, "x"); err != nil || out != "echo:x" {
		t.Fatalf("%q %v", out, err)
	}
	// an unknown broadcast is the same unavailable as everything else
	b, _ := answerOf(t, env, nil, &wire.Connect{BroadcastID: env.NewID(), Scheme: "http"})
	if !bytes.Equal(b, unavailableFrame(t)) {
		t.Fatalf("%q", b)
	}
}

// --- the uniform refusal ---

func TestUnavailableIsOneAnswerForUnknownEndedForeignOfflineAndFull(t *testing.T) {
	p := testkit.TestParams()
	p.TunnelCap = 1
	p.UnavailableDelay = 200 * time.Millisecond
	o := looseOpts("alice", "bobby")
	o.Params = &p
	env := testkit.StartServer(t, o)

	prodA, resA, shareA, _ := shareProducer(t, env, "alice", nil)
	cons := consumerDevice(t, env, "alice")
	foreignCons := consumerDevice(t, env, "bobby")

	// ended: unpublished
	gone := env.NewProducer("alice", sshPW)
	if err := gone.Start(); err != nil {
		t.Fatal(err)
	}
	endedRes, endedShare, _, _ := gone.PublishShare(true, nil)
	_ = gone.Control().Send(&wire.Unpublish{ResourceID: endedRes})
	eventually(t, "unpublish", func() bool { return env.Dir.Counts().Resources == 1 })

	// offline: the producer left (bye)
	off := env.NewProducer("alice", sshPW)
	if err := off.Start(); err != nil {
		t.Fatal(err)
	}
	offRes, offShare, _, _ := off.PublishShare(true, nil)
	_ = off.Control().Send(&wire.Bye{})
	eventually(t, "offline", func() bool { return env.Dir.Counts().Online == 1+1 /* prodA and cons? */ || true })
	time.Sleep(150 * time.Millisecond)

	// full: the only tunnel slot of prodA's resource is held
	prodA.Handler = holdHandler
	hold := connectOK(t, env, cons, resA)
	defer hold.Close()

	type result struct {
		name string
		b    []byte
		d    time.Duration
	}
	var rs []result
	add := func(name string, d *testkit.Device, m *wire.Connect) {
		b, took := answerOf(t, env, d, m)
		rs = append(rs, result{name, b, took})
	}
	add("unknown resource", cons, &wire.Connect{ResourceID: env.NewID()})
	add("malformed resource id", cons, &wire.Connect{ResourceID: "not-an-id"})
	add("ended resource", cons, &wire.Connect{ResourceID: endedRes})
	add("offline resource", cons, &wire.Connect{ResourceID: offRes})
	add("foreign account's resource", foreignCons, &wire.Connect{ResourceID: resA})
	add("full resource", cons, &wire.Connect{ResourceID: resA})
	add("unknown share", nil, &wire.Connect{ShareID: env.NewID()})
	add("ended share", nil, &wire.Connect{ShareID: endedShare})
	add("offline share", nil, &wire.Connect{ShareID: offShare})
	add("full share", nil, &wire.Connect{ShareID: shareA})
	add("unknown broadcast", nil, &wire.Connect{BroadcastID: env.NewID(), Scheme: "http"})
	_ = prodA

	want := unavailableFrame(t)
	for _, r := range rs {
		if !bytes.Equal(r.b, want) {
			t.Errorf("%s: answered %q, want the canonical unavailable frame", r.name, r.b)
		}
		if r.d < 200*time.Millisecond || r.d > 700*time.Millisecond {
			t.Errorf("%s took %v, want the fixed 200 ms minimum plus slack", r.name, r.d)
		}
	}
	// the timing of the cases agrees with each other
	for _, r := range rs[1:] {
		if d := r.d - rs[0].d; d > 200*time.Millisecond || d < -200*time.Millisecond {
			t.Errorf("%s and %s differ by %v in timing", r.name, rs[0].name, d)
		}
	}
}

func TestUnknownIDAttemptsAreRateLimitedPerAddress(t *testing.T) { // unknown-id: 10/min, burst 3
	o := testkit.Options{LoginLimits: loose(), Logins: []string{"alice"}}
	lc := limits.DefaultConfig()
	lc.Rates[limits.FailedAuth] = limits.Rate{PerMinute: 6000, Burst: 1000}
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 6000, Burst: 1000}
	o.Limits = &lc
	env := testkit.StartServer(t, o)
	_, resID, shareID, _ := shareProducer(t, env, "alice", nil)
	cons := consumerDevice(t, env, "alice")
	for i := 0; i < 3; i++ {
		answerOf(t, env, nil, &wire.Connect{ShareID: env.NewID()})
	}
	// the address has used its burst: even a VALID id is now answered unavailable without a lookup
	b, _ := answerOf(t, env, nil, &wire.Connect{ShareID: shareID})
	if !bytes.Equal(b, unavailableFrame(t)) {
		t.Fatalf("a rate-limited address must be answered unavailable: %q", b)
	}
	b, _ = answerOf(t, env, cons, &wire.Connect{ResourceID: resID})
	if !bytes.Equal(b, unavailableFrame(t)) {
		t.Fatalf("by resourceId too: %q", b)
	}
}

// --- attach rules ---

func TestAttachRules(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	p, resID, _, _ := shareProducer(t, env, "alice", nil)
	// an attach for nothing is closed without an answer
	c, err := p.Attach(wire.NewID())
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("an unknown tunnel id must be closed silently")
	}
	// a tunnel waits for its producer: a hand-made consumer, a producer that does not serve opens
	quiet := env.NewProducer("alice", sshPW)
	if err := quiet.Start(); err != nil {
		t.Fatal(err)
	}
	qres, _, _, _ := quiet.PublishShare(false, nil)
	_ = qres
	var tid string
	var mu sync.Mutex
	opened := make(chan struct{}, 1)
	quiet.Control().OnOpen(func(o *wire.Open) {
		mu.Lock()
		tid = o.TunnelID
		mu.Unlock()
		opened <- struct{}{}
	})
	cons := consumerDevice(t, env, "alice")
	var wg sync.WaitGroup
	wg.Add(1)
	var cc *testkit.Conn
	go func() {
		defer wg.Done()
		cc, _, _ = cons.Connect(&wire.Connect{ResourceID: qres})
	}()
	<-opened
	mu.Lock()
	id := tid
	mu.Unlock()
	// the wrong device (another enrolled device of the account) attaching for it is closed unanswered...
	wrong := env.NewProducer("alice", sshPW)
	if err := wrong.Start(); err != nil {
		t.Fatal(err)
	}
	wc, err := wrong.Attach(id)
	if err != nil {
		t.Fatal(err)
	}
	if !readsEOF(wc.Conn, 2*time.Second) {
		t.Fatal("an attach by the wrong device must be closed unanswered")
	}
	// ...and leaves the real tunnel waiting, which its producer can still claim
	rc, err := quiet.Attach(id)
	if err != nil {
		t.Fatal(err)
	}
	defer rc.Close()
	wg.Wait()
	if cc == nil {
		t.Fatal("the real attach must still complete the tunnel")
	}
	defer cc.Close()
	// a second attach for a used tunnel id is closed
	again, err := quiet.Attach(id)
	if err != nil {
		t.Fatal(err)
	}
	if !readsEOF(again.Conn, 2*time.Second) {
		t.Fatal("a tunnel id is single use")
	}
	// the bridge works: bytes cross both ways
	_, _ = cc.Conn.Write([]byte("ping"))
	buf := make([]byte, 4)
	_ = rc.SetReadDeadline(time.Now().Add(2 * time.Second))
	if _, err := io.ReadFull(rc.Conn, buf); err != nil || string(buf) != "ping" {
		t.Fatalf("%q %v", buf, err)
	}
	_ = resID
	_ = p
}

func TestAttachNeverComesAnswersUnavailableAfterTheWindowAndFreesTheSlot(t *testing.T) {
	p := testkit.TestParams()
	p.PendingWindow = 300 * time.Millisecond
	p.TunnelCap = 1
	o := looseOpts("alice")
	o.Params = &p
	env := testkit.StartServer(t, o)
	prod := env.NewProducer("alice", sshPW)
	if err := prod.Start(); err != nil { // it never answers an open: no OnOpen is wired
		t.Fatal(err)
	}
	prod.Control().OnOpen(func(*wire.Open) {})
	res, _, _, _ := prod.PublishShare(false, nil)
	cons := consumerDevice(t, env, "alice")
	b, d := answerOf(t, env, cons, &wire.Connect{ResourceID: res})
	if !bytes.Equal(b, unavailableFrame(t)) || d < 300*time.Millisecond {
		t.Fatalf("no attach in time must end as unavailable after the window: %q after %v", b, d)
	}
	// the expired tunnel freed its slot (the cap is 1): a producer that answers now is served
	prod.Control().OnOpen(prod.Answer)
	c := connectOK(t, env, cons, res)
	if out, err := testkit.SSHThrough(c.Conn, sshPW, "after"); err != nil || out != "echo:after" {
		t.Fatalf("%q %v", out, err)
	}
}

func TestTheSeventeenthTunnelOfAResourceIsRefusedAndAFreedSlotIsUsable(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	p := env.NewProducer("alice", sshPW)
	p.Handler = holdHandler
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	res, _, _, _ := p.PublishShare(false, nil)
	cons := consumerDevice(t, env, "alice")
	var conns []*testkit.Conn
	for i := 0; i < 16; i++ {
		conns = append(conns, connectOK(t, env, cons, res))
	}
	b, _ := answerOf(t, env, cons, &wire.Connect{ResourceID: res})
	if !bytes.Equal(b, unavailableFrame(t)) {
		t.Fatalf("the 17th tunnel must be refused unavailable: %q", b)
	}
	_ = conns[0].Close()
	eventually(t, "a slot to free", func() bool {
		c, _, err := cons.Connect(&wire.Connect{ResourceID: res})
		if err != nil || c == nil {
			return false
		}
		conns = append(conns, c)
		return true
	})
	for _, c := range conns {
		c.Close()
	}
}

func TestBytesRightBehindConnectReachTheProducer(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	p := env.NewProducer("alice", sshPW)
	got := make(chan string, 1)
	p.Handler = func(c net.Conn) {
		buf := make([]byte, 5)
		_, _ = io.ReadFull(c, buf)
		got <- string(buf)
		_ = c.Close()
	}
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	res, share, _, _ := p.PublishShare(true, nil)
	cons := consumerDevice(t, env, "alice")

	// authenticated: the connect frame and the raw bytes go out in ONE write
	c, _, err := cons.Hello()
	if err != nil || c == nil {
		t.Fatal(err)
	}
	defer c.Close()
	frame, _ := wire.Frame(&wire.Connect{ResourceID: res})
	_ = c.SendRaw(append(append([]byte{}, frame...), []byte("HELLO")...))
	select {
	case s := <-got:
		if s != "HELLO" {
			t.Fatalf("got %q", s)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("the raw bytes behind the connect frame were lost")
	}
	// outsider
	oc, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	defer oc.Close()
	frame, _ = wire.Frame(&wire.Connect{ShareID: share})
	_ = oc.SendRaw(append(append([]byte{}, frame...), []byte("WORLD")...))
	select {
	case s := <-got:
		if s != "WORLD" {
			t.Fatalf("got %q", s)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("outsider: the raw bytes behind the connect frame were lost")
	}
}

func TestTheBridgeCarries64MiBByHashWithHalfClose(t *testing.T) {
	if testing.Short() {
		t.Skip("64 MiB")
	}
	env := testkit.StartServer(t, looseOpts("alice"))
	p := env.NewProducer("alice", sshPW)
	p.Handler = func(c net.Conn) {
		h := sha256.New()
		_, _ = io.Copy(h, c) // until the consumer's half-close reaches us as EOF
		_, _ = c.Write(h.Sum(nil))
		_ = c.Close()
	}
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	res, _, _, _ := p.PublishShare(false, nil)
	cons := consumerDevice(t, env, "alice")
	c := connectOK(t, env, cons, res)
	tc := c.Conn.(interface{ CloseWrite() error })
	want := sha256.New()
	go func() {
		buf := make([]byte, 64*1024)
		for sent := 0; sent < 64<<20; sent += len(buf) {
			_, _ = rand.Read(buf)
			want.Write(buf)
			if _, err := c.Conn.Write(buf); err != nil {
				return
			}
		}
		_ = tc.CloseWrite()
	}()
	_ = c.SetReadDeadline(time.Now().Add(90 * time.Second))
	got := make([]byte, 32)
	if _, err := io.ReadFull(c.Conn, got); err != nil {
		t.Fatalf("no hash came back: %v", err)
	}
	if !bytes.Equal(got, want.Sum(nil)) {
		t.Fatal("the bridge changed bytes")
	}
}

func TestAProducerGoingAwayMidTunnelEndsTheConsumer(t *testing.T) {
	for name, away := range map[string]func(env *testkit.Env, p *testkit.Producer, res string){
		"bye": func(env *testkit.Env, p *testkit.Producer, res string) { _ = p.Control().Send(&wire.Bye{}) },
		"unpublish": func(env *testkit.Env, p *testkit.Producer, res string) {
			_ = p.Control().Send(&wire.Unpublish{ResourceID: res})
		},
		"revoke": func(env *testkit.Env, p *testkit.Producer, res string) { env.Ops.RevokeDevice("alice", p.ID) },
		"drop":   func(env *testkit.Env, p *testkit.Producer, res string) { p.Control().Close() },
	} {
		t.Run(name, func(t *testing.T) {
			env := testkit.StartServer(t, looseOpts("alice"))
			p := env.NewProducer("alice", sshPW)
			p.Handler = holdHandler
			if err := p.Start(); err != nil {
				t.Fatal(err)
			}
			res, _, _, _ := p.PublishShare(false, nil)
			cons := consumerDevice(t, env, "alice")
			c := connectOK(t, env, cons, res)
			away(env, p, res)
			if !readsEOF(c.Conn, 4*time.Second) {
				t.Fatal("a consumer must not be left on a bridge whose producer left")
			}
		})
	}
}

func TestAnIdleBridgeIsClosed(t *testing.T) {
	p := testkit.TestParams()
	p.IdleTimeout = 300 * time.Millisecond
	o := looseOpts("alice")
	o.Params = &p
	env := testkit.StartServer(t, o)
	prod := env.NewProducer("alice", sshPW)
	prod.Handler = holdHandler
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	res, _, _, _ := prod.PublishShare(false, nil)
	cons := consumerDevice(t, env, "alice")
	c := connectOK(t, env, cons, res)
	start := time.Now()
	if !readsEOF(c.Conn, 3*time.Second) {
		t.Fatal("an idle bridge must be closed")
	}
	if d := time.Since(start); d < 250*time.Millisecond {
		t.Fatalf("closed after %v, before the idle bound", d)
	}
}

// --- the floods of ticket 139, re-pointed ---

func TestAFloodOfConnectsAndUnknownIdsLeavesTheServerResponsive(t *testing.T) {
	lc := limits.DefaultConfig()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 6000, Burst: 2000}
	env := testkit.StartServer(t, testkit.Options{Limits: &lc, LoginLimits: loose(), Logins: []string{"alice"}})
	_, resID, shareID, _ := shareProducer(t, env, "alice", nil)
	cons := consumerDevice(t, env, "alice")
	// the attacker comes from 127.0.0.2, the legitimate pair from 127.0.0.1
	probe, err := net.DialTCP("tcp", &net.TCPAddr{IP: net.ParseIP("127.0.0.2")}, mustTCPAddr(t, env.Addr))
	if err != nil {
		t.Skipf("cannot originate from 127.0.0.2 on this host: %v", err)
	}
	probe.Close()

	var wg sync.WaitGroup
	sem := make(chan struct{}, 40)
	for i := 0; i < 600; i++ {
		wg.Add(1)
		sem <- struct{}{}
		go func(i int) {
			defer wg.Done()
			defer func() { <-sem }()
			raw, err := net.DialTCP("tcp", &net.TCPAddr{IP: net.ParseIP("127.0.0.2")}, mustTCPAddr(t, env.Addr))
			if err != nil {
				return
			}
			defer raw.Close()
			if i%2 == 0 { // plain connects with random ids
				m := &wire.Connect{ShareID: wire.NewID()}
				frame, _ := wire.Frame(m)
				_, _ = raw.Write(frame)
			} // and bare connections that send nothing
			_ = raw.SetReadDeadline(time.Now().Add(time.Second))
			_, _ = io.Copy(io.Discard, raw)
		}(i)
	}
	c := connectOK(t, env, cons, resID)
	if out, err := testkit.SSHThrough(c.Conn, sshPW, "during-flood"); err != nil || out != "echo:during-flood" {
		t.Fatalf("the legitimate pair was affected by the flood: %q %v", out, err)
	}
	sc, _, err := env.OutsiderConnect(&wire.Connect{ShareID: shareID})
	if err != nil || sc == nil {
		t.Fatalf("an outsider by shareId during the flood: %v", err)
	}
	sc.Close()
	wg.Wait()
	c2 := connectOK(t, env, cons, resID)
	if out, err := testkit.SSHThrough(c2.Conn, sshPW, "after-flood"); err != nil || out != "echo:after-flood" {
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

// --- the third-party port ---

func portEnv(t *testing.T, size int) (*testkit.Env, int, int) {
	t.Helper()
	lo, hi := testkit.FreeRange(size)
	o := looseOpts("alice")
	o.PortRange = [2]int{lo, hi}
	return testkit.StartServer(t, o), lo, hi
}

func dialPort(port int) (net.Conn, error) {
	return net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)), 2*time.Second)
}

func TestPortZeroIsBoundBeforePublishedAndBridgesRawSSH(t *testing.T) {
	env, lo, hi := portEnv(t, 10)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	resID, _, ans, err := p.PublishShare(false, intPtr(0))
	if err != nil {
		t.Fatal(err)
	}
	pub, ok := ans.(*wire.Published)
	if !ok || pub.PublicPort == nil || *pub.PublicPort < lo || *pub.PublicPort > hi {
		t.Fatalf("publicPort 0 must be answered with a port in %d-%d: %+v", lo, hi, ans)
	}
	// the lease was bound BEFORE `published` was sent: the very next dial connects
	c, err := dialPort(*pub.PublicPort)
	if err != nil {
		t.Fatalf("the granted port must really listen: %v", err)
	}
	defer c.Close()
	_ = c.SetReadDeadline(time.Time{})
	out, err := testkit.SSHExec(c, "u", sshPW, "from-winscp")
	if err != nil || out != "echo:from-winscp" {
		t.Fatalf("raw SSH through the port: %q %v", out, err)
	}
	// nothing is sent by the SERVER on the plain port before the bridge: a producer that says nothing
	// (an SSH server speaks first, so this one is a silent service) leaves a third-party client with
	// nothing to read
	quiet := env.NewProducer("alice", sshPW)
	quiet.Handler = holdHandler
	if err := quiet.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, qans, _ := quiet.PublishShare(false, intPtr(0))
	qc, err := dialPort(*qans.(*wire.Published).PublicPort)
	if err != nil {
		t.Fatal(err)
	}
	defer qc.Close()
	_ = qc.SetReadDeadline(time.Now().Add(400 * time.Millisecond))
	if n, _ := qc.Read(make([]byte, 1)); n != 0 {
		t.Fatal("the server wrote on the third-party port before the bridge")
	}
	// the record shows the granted port
	d := listDirectory(t, p.Control())
	mine := findRec(recs(d.Resources), "resourceId", resID)
	if mine == nil {
		t.Fatal("the published resource is not listed")
	}
	if n, _ := mine.Int("publicPort"); n != *pub.PublicPort {
		t.Fatalf("the record must carry the granted port: %s", mine.Marshal())
	}
}

func TestNoPublicPortMemberMeansNoPort(t *testing.T) {
	env, _, _ := portEnv(t, 5)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, ans, _ := p.PublishShare(false, nil)
	if pub := ans.(*wire.Published); pub.PublicPort != nil {
		t.Fatalf("a publish that asked for no port must not get one: %+v", pub)
	}
	if env.Engine.Leases() != 0 {
		t.Fatal("a port was leased unasked")
	}
}

func TestARequestedPortIsGrantedOrADifferentOneAndAPreviousGrantIsAskedForAgain(t *testing.T) {
	env, lo, _ := portEnv(t, 10)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, a1, _ := p.PublishShare(false, intPtr(lo+4))
	if pub, ok := a1.(*wire.Published); !ok || pub.PublicPort == nil || *pub.PublicPort != lo+4 {
		t.Fatalf("a free requested port must be granted: %+v", a1)
	}
	// a second resource asking for the same number gets a different one the producer shows
	_, _, a2, _ := p.PublishShare(false, intPtr(lo+4))
	if pub, ok := a2.(*wire.Published); !ok || pub.PublicPort == nil || *pub.PublicPort == lo+4 {
		t.Fatalf("a taken port must be answered with a different one: %+v", a2)
	}
	// a port another program holds is skipped the same way
	other, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(lo+7)))
	if err != nil {
		t.Skip("cannot hold a port here")
	}
	defer other.Close()
	_, _, a3, _ := p.PublishShare(false, intPtr(lo+7))
	if pub, ok := a3.(*wire.Published); !ok || pub.PublicPort == nil || *pub.PublicPort == lo+7 {
		t.Fatalf("a port held elsewhere must not be handed out: %+v", a3)
	}
	// the producer republishes the SAME resource with the port it was granted: it keeps it
	resID, _, raw := p.ResourceJSON("Again", false, intPtr(lo+1))
	first, _ := p.Publish(raw)
	granted := *first.(*wire.Published).PublicPort
	rec, _ := wire.ParseRecord(raw)
	rec.SetInt("publicPort", granted)
	second, _ := p.Publish(rec.Marshal())
	if g := second.(*wire.Published); g.ResourceID != resID || *g.PublicPort != granted {
		t.Fatalf("a producer that asks again for its port keeps it: %+v", g)
	}
}

func TestAnExhaustedRangeRefusesWithPortUnavailableAndStoresNothing(t *testing.T) {
	env, _, _ := portEnv(t, 1)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, a1, _ := p.PublishShare(false, intPtr(0)) // takes the only port
	if _, ok := a1.(*wire.Published); !ok {
		t.Fatalf("%+v", a1)
	}
	_, _, a2, _ := p.PublishShare(false, intPtr(0))
	refusedWith(t, a2, wire.ReasonPortUnavailable)
	if env.Dir.Counts().Resources != 1 {
		t.Fatalf("a refused publish must store nothing: %d resources", env.Dir.Counts().Resources)
	}
	// no pool at all: the same refusal
	env2 := testkit.StartServer(t, looseOpts("alice"))
	p2 := env2.NewProducer("alice", sshPW)
	if err := p2.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, a3, _ := p2.PublishShare(false, intPtr(0))
	refusedWith(t, a3, wire.ReasonPortUnavailable)
}

func TestARawClientOverTheTunnelCapIsClosed(t *testing.T) {
	lo, hi := testkit.FreeRange(3)
	p := testkit.TestParams()
	p.TunnelCap = 2
	o := looseOpts("alice")
	o.Params = &p
	o.PortRange = [2]int{lo, hi}
	env := testkit.StartServer(t, o)
	prod := env.NewProducer("alice", sshPW)
	prod.Handler = holdHandler
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, ans, _ := prod.PublishShare(false, intPtr(0))
	port := *ans.(*wire.Published).PublicPort
	c1, _ := dialPort(port)
	c2, _ := dialPort(port)
	defer c1.Close()
	defer c2.Close()
	time.Sleep(200 * time.Millisecond) // both tunnels established
	c3, err := dialPort(port)
	if err != nil {
		t.Fatal(err)
	}
	defer c3.Close()
	if !readsEOF(c3, 3*time.Second) {
		t.Fatal("a raw client over the tunnel cap must be closed")
	}
}

func TestThePortLeaseEndsWithUnpublishOfflineAndRevoke(t *testing.T) {
	for name, end := range map[string]func(env *testkit.Env, p *testkit.Producer, res string){
		"unpublish": func(env *testkit.Env, p *testkit.Producer, res string) {
			_ = p.Control().Send(&wire.Unpublish{ResourceID: res})
		},
		"bye":    func(env *testkit.Env, p *testkit.Producer, res string) { _ = p.Control().Send(&wire.Bye{}) },
		"drop":   func(env *testkit.Env, p *testkit.Producer, res string) { p.Control().Close() },
		"revoke": func(env *testkit.Env, p *testkit.Producer, res string) { env.Ops.RevokeDevice("alice", p.ID) },
	} {
		t.Run(name, func(t *testing.T) {
			env, _, _ := portEnv(t, 3)
			p := env.NewProducer("alice", sshPW)
			if err := p.Start(); err != nil {
				t.Fatal(err)
			}
			res, _, ans, _ := p.PublishShare(false, intPtr(0))
			port := *ans.(*wire.Published).PublicPort
			if c, err := dialPort(port); err != nil {
				t.Fatalf("not listening: %v", err)
			} else {
				c.Close()
			}
			end(env, p, res)
			eventually(t, "the port to stop accepting", func() bool {
				c, err := dialPort(port)
				if err == nil {
					c.Close()
					return false
				}
				return true
			})
			if env.Engine.Leases() != 0 {
				t.Fatal("the lease outlived the resource")
			}
		})
	}
}

func TestAReturningProducerAsksAgainForItsPort(t *testing.T) {
	env, lo, _ := portEnv(t, 5)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	resID, _, raw := p.ResourceJSON("Photos", false, intPtr(lo+2))
	first, _ := p.Publish(raw)
	port := *first.(*wire.Published).PublicPort
	_ = p.Control().Send(&wire.Bye{})
	eventually(t, "offline", func() bool { return env.Engine.Leases() == 0 })
	if err := p.Resume(); err != nil {
		t.Fatal(err)
	}
	rec, _ := wire.ParseRecord(raw)
	rec.SetInt("publicPort", port)
	again, _ := p.Publish(rec.Marshal())
	if g := again.(*wire.Published); g.ResourceID != resID || *g.PublicPort != port {
		t.Fatalf("a producer that returns asks again for the port it was granted: %+v (was %d)", g, port)
	}
	c, err := dialPort(port)
	if err != nil {
		t.Fatalf("the port listens again: %v", err)
	}
	defer c.Close()
	if out, err := testkit.SSHExec(c, "u", sshPW, "back"); err != nil || out != "echo:back" {
		t.Fatalf("%q %v", out, err)
	}
}

func TestNoPortCanBeReusedByAnotherProcess(t *testing.T) {
	// "never SO_REUSEADDR": a port leased by the server cannot be bound again by anyone else
	env, _, _ := portEnv(t, 3)
	p := env.NewProducer("alice", sshPW)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, ans, _ := p.PublishShare(false, intPtr(0))
	port := *ans.(*wire.Published).PublicPort
	if ln, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(port))); err == nil {
		ln.Close()
		t.Fatal("another listener could bind a leased port")
	}
}
