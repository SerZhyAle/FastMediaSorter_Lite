package rendezvous_test

import (
	"encoding/json"
	"net"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The leftovers of tickets 161-164 that ticket 165 closes: the pre-auth slot, the keepalive clamp of
// hello, a null receiver, and the HTTP opening on the real engine.

// 161 rule 3: a connection holds its pre-auth slot until it AUTHENTICATES, not merely until its first
// envelope arrives.
func TestThePreAuthSlotIsHeldUntilTheStreamAuthenticates(t *testing.T) {
	pa := limits.NewPreAuth(16)
	env := startLayer(t, func(c *rendezvous.Config) { c.PreAuth = pa })
	env.be.helloGate = make(chan struct{})

	c := env.tls(t)
	if err := c.Send(&wire.Hello{DeviceID: devA, DeviceToken: fakeToken}); err != nil {
		t.Fatal(err)
	}
	// the first envelope has arrived and the backend is judging it: the stream is not authenticated yet
	eventually(t, "the backend to be judging the hello", func() bool { _, h := env.be.counts(); return h == 1 })
	time.Sleep(150 * time.Millisecond)
	if pa.Len() != 1 {
		t.Fatalf("the slot must be held while the stream is unauthenticated, the pool holds %d", pa.Len())
	}
	close(env.be.helloGate)
	if _, err := c.Recv(3 * time.Second); err != nil { // welcome
		t.Fatal(err)
	}
	eventually(t, "the slot to be given back at authentication", func() bool { return pa.Len() == 0 })
	// the stream is still open and still a stream of its device
	if err := c.Send(&wire.Keepalive{}); err != nil {
		t.Fatal(err)
	}
	if e, err := c.Recv(3 * time.Second); err != nil || e.TypeName() != wire.TypeKeepalive {
		t.Fatalf("%v %v", e, err)
	}
}

func TestARefusedStreamGivesItsSlotBackWhenItCloses(t *testing.T) {
	pa := limits.NewPreAuth(16)
	env := startLayer(t, func(c *rendezvous.Config) { c.PreAuth = pa })
	c := env.tls(t)
	if err := c.Send(&wire.Hello{DeviceID: devA, DeviceToken: "bm90LXRoZS10b2tlbi1ub3QtdGhlLXRva2VuLW5vdC10aGU"}); err != nil {
		t.Fatal(err)
	}
	e, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, e, wire.ReasonBadCredentials)
	eventually(t, "the slot to be given back", func() bool { return pa.Len() == 0 })
}

func TestAnOutsiderConnectDoesNotHoldASlotWhileItWaitsForItsProducer(t *testing.T) {
	pa := limits.NewPreAuth(16)
	env := startLayer(t, func(c *rendezvous.Config) { c.PreAuth = pa })
	c := env.tls(t)
	if err := c.Send(&wire.Connect{ShareID: devB}); err != nil {
		t.Fatal(err)
	}
	if e, err := c.Recv(3 * time.Second); err != nil || e.TypeName() != wire.TypeConnected {
		t.Fatalf("%v %v", e, err)
	}
	// the fake backend now waits for 5 raw bytes: the stream is the bridge's, not the pre-auth pool's
	if pa.Len() != 0 {
		t.Fatalf("%d slots held by a stream that was handed over", pa.Len())
	}
}

func TestAStreamThatNeverSendsAFrameHoldsASlotUntilItIsDropped(t *testing.T) {
	pa := limits.NewPreAuth(16)
	env := startLayer(t, func(c *rendezvous.Config) { c.PreAuth = pa })
	raw, err := net.Dial("tcp", env.addr)
	if err != nil {
		t.Fatal(err)
	}
	defer raw.Close()
	_, _ = raw.Write([]byte{0x00}) // the first byte of a plain frame, then silence
	eventually(t, "the silent stream to hold a slot", func() bool { return pa.Len() == 1 })
	eventually(t, "the silent stream to be dropped and its slot returned", func() bool { return pa.Len() == 0 })
}

func TestHelloKeepaliveProposalIsClamped(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	for _, c := range []struct{ propose, want int }{{0, 30}, {1, 10}, {9, 10}, {10, 10}, {45, 45}, {120, 120}, {121, 120}, {100000, 120}} {
		conn, err := testkit.DialTLS(env.Addr, env.Fingerprint)
		if err != nil {
			t.Fatal(err)
		}
		if err := conn.Send(&wire.Hello{DeviceID: d.ID, DeviceToken: d.Token, KeepaliveSeconds: c.propose}); err != nil {
			t.Fatal(err)
		}
		e, err := conn.Recv(3 * time.Second)
		if err != nil {
			t.Fatal(err)
		}
		w, ok := e.(*wire.Welcome)
		if !ok || w.KeepaliveSeconds != c.want {
			t.Fatalf("proposed %d: %+v, want %d", c.propose, e, c.want)
		}
		_ = conn.Close()
	}
}

func TestAnEnrollWithANullReceiverEnrollsADeviceWithNone(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	c, err := testkit.DialTLS(env.Addr, env.Fingerprint)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	frame := `{"schemaVersion":2,"type":"enroll","login":"alice","password":"` + testkit.TestPassword + `","deviceId":"` + d.ID +
		`","deviceName":"Null","product":"fms-windows","roles":["resource-consumer"],"receiver":null}`
	if err := c.SendRaw(testkit.RawFrame(frame)); err != nil {
		t.Fatal(err)
	}
	e, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := e.(*wire.Enrolled); !ok {
		t.Fatalf("a null receiver must not make the enroll malformed: %+v", e)
	}
	c.Authenticated()
	if err := c.Send(&wire.List{}); err != nil {
		t.Fatal(err)
	}
	e, err = c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	dir := e.(*wire.Directory)
	var rec map[string]json.RawMessage
	for _, raw := range dir.Devices {
		var m map[string]json.RawMessage
		_ = json.Unmarshal(raw, &m)
		if string(m["deviceId"]) == `"`+d.ID+`"` {
			rec = m
		}
	}
	if rec == nil {
		t.Fatal("the device is not in the directory")
	}
	if _, has := rec["receiver"]; has {
		t.Fatalf("a null receiver is absent from the record: %s", rec["receiver"])
	}
}

// the HTTP opening on the REAL engine, with no relay handler: the listen path is a 404 (the engine's
// own), every other path 404, every other method 405 - the layer test covers the same on a fake
func TestTheHTTPOpeningOnTheRealEngineWithNoRelay(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	id := env.NewID()
	for _, c := range []struct {
		name, req, method string
		want              int
	}{
		{"the listen path, no relay", getRequest(id), "GET", 404},
		{"another path", "GET /index.html HTTP/1.1\r\nHost: x\r\n\r\n", "GET", 404},
		{"HEAD", "HEAD " + wire.ListenPath(id) + " HTTP/1.1\r\nHost: x\r\n\r\n", "HEAD", 405},
		{"POST", "POST " + wire.ListenPath(id) + " HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n", "POST", 405},
	} {
		if code, _ := statusOf(t, env, c.req, c.method); code != c.want {
			t.Errorf("%s: %d, want %d", c.name, code, c.want)
		}
	}
}
