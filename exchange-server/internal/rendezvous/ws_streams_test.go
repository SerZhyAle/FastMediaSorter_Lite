package rendezvous_test

import (
	"bufio"
	"bytes"
	"crypto/tls"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"net/http"
	"os"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// These tests hold WEB-BINDING websocket-binding "Streams": every stream kind of the TLS opening - hello,
// enroll, register, an outsider connect, and with them the control, relay and clipboard streams they fix -
// runs on the WebSocket opening with the same engine behind it, so a browser device (product `fms-web`) can
// sign in, enrol, use the clipboard and relay through the server. They speak through a raw masked-frame
// client over real TLS, so the message layer is the one a browser sees: one message is one frame in an
// envelope phase, any run of bytes in a raw phase, and the empty message is a half-close. Each rule has a
// refusal next to its happy path.

// wsClosed is what recvMessage returns when the server ended the stream with a Close frame.
type wsClosed struct{ code uint16 }

func (e *wsClosed) Error() string { return "the server closed the WebSocket" }

// recvMessage reads the next data message (an empty message comes back as an empty slice). Pings and pongs are
// skipped; a Close is answered the way a browser does and returned as *wsClosed.
func (c *wsClient) recvMessage(within time.Duration) ([]byte, error) {
	deadline := time.Now().Add(within)
	for {
		left := time.Until(deadline)
		if left <= 0 {
			return nil, os.ErrDeadlineExceeded
		}
		op, p, err := c.frame(left)
		if err != nil {
			return nil, err
		}
		switch op {
		case 0x2:
			return p, nil
		case 0x8:
			code := uint16(1005)
			if len(p) >= 2 {
				code = binary.BigEndian.Uint16(p)
			}
			c.write(wsMasked(0x88, p))
			return nil, &wsClosed{code: code}
		case 0x9, 0xA:
		default:
			c.t.Fatalf("a server frame with opcode %d", op)
		}
	}
}

// recvEnvelope reads the next message as exactly one frame of 7.1 and decodes it.
func (c *wsClient) recvEnvelope(within time.Duration) (wire.Envelope, error) {
	c.t.Helper()
	p, err := c.recvMessage(within)
	if err != nil {
		return nil, err
	}
	if len(p) < 4 || int(binary.BigEndian.Uint32(p)) != len(p)-4 {
		c.t.Fatalf("a server message is not one frame: %d bytes", len(p))
	}
	e, err := wire.Decode(p[4:])
	if err != nil {
		c.t.Fatal(err)
	}
	return e, nil
}

// send writes one envelope as one binary message and requires the server to have taken it.
func (c *wsClient) send(e wire.Envelope) {
	c.t.Helper()
	if !c.sendTry(e) {
		c.t.Fatalf("the server did not take a %s", e.TypeName())
	}
}

// sendTry is send for a stream that may already be closed.
func (c *wsClient) sendTry(e wire.Envelope) bool {
	c.t.Helper()
	f, err := wire.EncodeFrame(e, wire.MaxFrameAuth)
	if err != nil {
		c.t.Fatal(err)
	}
	return c.write(wsMasked(0x82, f))
}

// sendBytes writes raw bytes as one binary message (a raw phase).
func (c *wsClient) sendBytes(b []byte) {
	c.t.Helper()
	if !c.write(wsMasked(0x82, b)) {
		c.t.Fatal("the server did not take the bytes")
	}
}

// halfClose sends the empty binary message: this side's end of its direction.
func (c *wsClient) halfClose() {
	c.t.Helper()
	if !c.write(wsMasked(0x82, nil)) {
		c.t.Fatal("the server did not take the half-close")
	}
}

// readBytes reads raw messages until it holds n bytes; an empty message or a Close before that fails.
func (c *wsClient) readBytes(n int, within time.Duration) []byte {
	c.t.Helper()
	var got []byte
	deadline := time.Now().Add(within)
	for len(got) < n {
		p, err := c.recvMessage(time.Until(deadline))
		if err != nil {
			c.t.Fatalf("after %d of %d raw bytes: %v", len(got), n, err)
		}
		if len(p) == 0 {
			c.t.Fatalf("the empty message came after %d of %d raw bytes", len(got), n)
		}
		got = append(got, p...)
	}
	return got
}

// expectClosedWith reads until the stream ends, requires that no data message comes first, and returns the
// Close code (0 when the transport ended without one).
func (c *wsClient) expectClosedWith(within time.Duration) uint16 {
	c.t.Helper()
	deadline := time.Now().Add(within)
	for {
		p, err := c.recvMessage(time.Until(deadline))
		var wc *wsClosed
		switch {
		case errors.As(err, &wc):
			return wc.code
		case isClosed(err):
			return 0
		case err != nil:
			c.t.Fatalf("the stream was not closed: %v", err)
		default:
			c.t.Fatalf("a data message (%d bytes) arrived where the stream had to end", len(p))
		}
	}
}

// wsExpect reads one envelope and requires its type.
func wsExpect[T wire.Envelope](t *testing.T, c *wsClient) T {
	t.Helper()
	e, err := c.recvEnvelope(5 * time.Second)
	if err != nil {
		var zero T
		t.Fatalf("waiting for %T: %v", zero, err)
	}
	v, ok := e.(T)
	if !ok {
		t.Fatalf("wanted %T, got %s %+v", v, e.TypeName(), e)
	}
	return v
}

// wsRefused reads one envelope, requires `refused` with the reason and then the stream's end with Close 1000
// ("every refused {reason} is the envelope of 7.10 followed by a close").
func wsRefused(t *testing.T, c *wsClient, want wire.Reason) {
	t.Helper()
	r := wsExpect[*wire.Refused](t, c)
	if r.Reason != string(want) {
		t.Fatalf("refused %q, want %q", r.Reason, want)
	}
	if code := c.expectClosedWith(5 * time.Second); code != 1000 {
		t.Fatalf("a refusal is followed by Close 1000, got %d", code)
	}
}

// wsDevice opens a WebSocket for a device of the server of env, same origin, and says hello.
func wsHello(t *testing.T, env *testkit.Env, d *testkit.Device, keepalive int) (*wsClient, *wire.Welcome) {
	t.Helper()
	c, status := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	if status != 101 {
		t.Fatalf("the opening answered %d", status)
	}
	c.send(&wire.Hello{DeviceID: d.ID, DeviceToken: d.Token, ProductVersion: "1.0", KeepaliveSeconds: keepalive})
	return c, wsExpect[*wire.Welcome](t, c)
}

// webDevice enrolls a device of product fms-web over TLS (the way a prior sign-in left it) and drops its stream.
func webDevice(t *testing.T, env *testkit.Env, login string) *testkit.Device {
	t.Helper()
	d := env.NewDevice(login)
	d.Product = "fms-web"
	d.Roles = []string{"clipboard"}
	mustEnroll(t, d)
	d.Close()
	env.AddSecret(d.Token)
	return d
}

// item 1: hello over the WebSocket

func TestHelloOverTheWebSocketSignsInAKnownDeviceAndListsItsProductRaw(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := webDevice(t, env, "alice")

	// the keepalive a page asks for is the contract's maximum, 120, and is echoed
	c, w := wsHello(t, env, d, 120)
	if w.Account != "alice" || w.KeepaliveSeconds != 120 || !strings.Contains(strings.Join(w.Features, ","), "clipboard") {
		t.Fatalf("welcome: %+v", w)
	}
	// the stream is authenticated: the first envelope after welcome fixes it as a control stream, and the
	// directory lists the browser device by the wire value `fms-web`, raw (device-product 1)
	c.send(&wire.List{})
	dir := wsExpect[*wire.Directory](t, c)
	rec := findRec(recs(dir.Devices), "deviceId", d.ID)
	if rec == nil {
		t.Fatalf("the device is not in the directory: %d devices", len(dir.Devices))
	}
	if p, _ := rec.Str("product"); p != "fms-web" {
		t.Fatalf("product listed as %q, want the raw wire value", p)
	}
	if p, _ := rec.Str("presence"); p != "online" {
		t.Fatalf("a device on a WebSocket control stream is online, got %q", p)
	}
	// a keepalive is answered one message for one frame
	c.send(&wire.Keepalive{})
	wsExpect[*wire.Keepalive](t, c)
	c.send(&wire.Bye{})
	if code := c.expectClosedWith(5 * time.Second); code != 1000 {
		t.Fatalf("after bye the stream ends with Close 1000, got %d", code)
	}
	env.AssertLogsClean(t)
}

func TestHelloOverTheWebSocketNeverGrantsMoreThanTheKeepaliveMaximum(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := webDevice(t, env, "alice")
	for _, tc := range []struct{ ask, want int }{{0, 30}, {1, 10}, {45, 45}, {500, 120}} {
		c, w := wsHello(t, env, d, tc.ask)
		if w.KeepaliveSeconds != tc.want {
			t.Errorf("asked %d, welcome says %d, want %d", tc.ask, w.KeepaliveSeconds, tc.want)
		}
		c.send(&wire.Bye{})
		c.expectClosedWith(5 * time.Second)
	}
}

func TestHelloOverTheWebSocketRefusesWhatTheTLSOpeningRefuses(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := webDevice(t, env, "alice")

	// a wrong token is the one refusal of every failed sign-in, and the stream ends
	bad := &testkit.Device{Env: env, Addr: env.Addr, Pin: env.Fingerprint, ID: d.ID, Login: "alice", Token: wire.NewToken()}
	c, status := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	if status != 101 {
		t.Fatalf("opening: %d", status)
	}
	c.send(&wire.Hello{DeviceID: bad.ID, DeviceToken: bad.Token})
	wsRefused(t, c, wire.ReasonBadCredentials)

	// the same refusal as on TLS, byte for byte (no second set of reasons)
	tc, r, err := bad.Hello()
	if err != nil || tc != nil {
		t.Fatalf("TLS hello with the wrong token: %v %+v", err, r)
	}
	want := frameBytes(t, r)
	c, _ = wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Hello{DeviceID: bad.ID, DeviceToken: bad.Token})
	msg, err := c.recvMessage(5 * time.Second)
	if err != nil || !bytes.Equal(msg, want) {
		t.Fatalf("the refusal on the WebSocket is %q (%v), on TLS %q", msg, err, want)
	}

	// an unknown device is the same answer
	c, _ = wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Hello{DeviceID: wire.NewID(), DeviceToken: d.Token})
	wsRefused(t, c, wire.ReasonBadCredentials)

	// and the good token still works afterwards: nothing the failures did locked the device out
	good, w := wsHello(t, env, d, 0)
	if w.Account != "alice" {
		t.Fatalf("welcome: %+v", w)
	}
	good.send(&wire.Bye{})
	good.expectClosedWith(5 * time.Second)
}

// item 2: enroll and register

func TestEnrollOverTheWebSocketStoresTheBrowserDeviceAndRefusesAWrongPassword(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	id := env.NewID()

	// a wrong password: one refusal, a closed stream, nothing stored
	c, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Enroll{Login: "alice", Password: "Wrongpass1", DeviceID: id, DeviceName: "Browser", Product: "fms-web", Roles: []string{"clipboard"}})
	wsRefused(t, c, wire.ReasonBadCredentials)
	if _, ok := env.Accounts.Device("alice", id); ok {
		t.Fatal("a refused enrolment stored a device")
	}

	// the right password: enrolled, the token once, and the keepalive clamped to 120
	c, _ = wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: id, DeviceName: "Browser", Product: "fms-web",
		ProductVersion: "0.1", Platform: "web", Roles: []string{"clipboard"}, KeepaliveSeconds: 500})
	en := wsExpect[*wire.Enrolled](t, c)
	if !wire.ValidToken(en.DeviceToken) || en.Account != "alice" || en.KeepaliveSeconds != 120 {
		t.Fatalf("enrolled: %+v", en)
	}
	env.AddSecret(en.DeviceToken)
	stored, ok := env.Accounts.Device("alice", id)
	if !ok || stored.Product != "fms-web" || stored.Platform != "web" {
		t.Fatalf("the device is stored as %+v (%v): product and platform are kept raw", stored, ok)
	}
	// the enrolled stream is authenticated like a welcomed one: its first envelope fixes the role
	c.send(&wire.List{})
	dir := wsExpect[*wire.Directory](t, c)
	if rec := findRec(recs(dir.Devices), "deviceId", id); rec == nil {
		t.Fatal("the enrolled device is not in its own directory")
	} else if p, _ := rec.Str("product"); p != "fms-web" {
		t.Fatalf("listed as %q", p)
	}
	c.send(&wire.Bye{})
	c.expectClosedWith(5 * time.Second)

	// and the token it was given signs it in on another socket
	d := &testkit.Device{Env: env, Addr: env.Addr, Pin: env.Fingerprint, ID: id, Login: "alice", Token: en.DeviceToken}
	hc, w := wsHello(t, env, d, 0)
	if w.Account != "alice" {
		t.Fatalf("welcome: %+v", w)
	}
	hc.send(&wire.Bye{})
	hc.expectClosedWith(5 * time.Second)
	env.AssertLogsClean(t)
}

func TestRegisterOverTheWebSocketUsesTheInvitationOnceAndRefusesAUsedOne(t *testing.T) {
	env := testkit.StartServer(t, noAccounts())
	code := mustInvite(t, env, "member")
	reg := func(login string) *wire.Register {
		return &wire.Register{Invitation: code, Login: login, Password: testkit.TestPassword, DeviceID: env.NewID(), DeviceName: "Browser",
			Product: "fms-web", Platform: "web", Roles: []string{"clipboard"}}
	}

	c, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(reg("webuser"))
	en := wsExpect[*wire.Enrolled](t, c)
	if en.Account != "webuser" || en.AccountKind != "member" || !wire.ValidToken(en.DeviceToken) {
		t.Fatalf("enrolled: %+v", en)
	}
	if !env.Accounts.Exists("webuser") || len(env.Accounts.Devices("webuser")) != 1 {
		t.Fatal("the account and its first device must exist")
	}
	c.send(&wire.Bye{})
	c.expectClosedWith(5 * time.Second)

	// the used invitation is refused as on TLS, and stores nothing
	c, _ = wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(reg("second"))
	wsRefused(t, c, wire.ReasonInvitationInvalid)
	if env.Accounts.Exists("second") {
		t.Fatal("a refused registration stored an account")
	}
}

// item 3: an outsider connect relays bytes both ways and passes the half-close

func TestAnOutsiderConnectOverTheWebSocketRelaysBothWaysAndHalfCloses(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	// the producer end is a plain byte service: it reads until the consumer's half-close reaches it, answers
	// with what it read and ends its own direction, which the splice must hand back to the page as the empty
	// message
	prod := env.NewProducer("alice", sshPW)
	prod.Handler = func(c net.Conn) {
		data, _ := io.ReadAll(c)
		_, _ = c.Write(append([]byte("echo:"), data...))
		if cw, ok := c.(interface{ CloseWrite() error }); ok {
			_ = cw.CloseWrite()
		}
		_, _ = io.Copy(io.Discard, c)
		_ = c.Close()
	}
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	_, shareID, ans, err := prod.PublishShare(true, nil)
	if err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("publish: %v %+v", err, ans)
	}

	c, status := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	if status != 101 {
		t.Fatalf("opening: %d", status)
	}
	c.send(&wire.Connect{ShareID: shareID})
	wsExpect[*wire.Connected](t, c)

	// the raw phase: the bytes of several messages arrive as one stream, whatever the cut
	c.sendBytes([]byte("ping-"))
	c.sendBytes([]byte("pong"))
	c.halfClose()
	got := c.readBytes(len("echo:ping-pong"), 5*time.Second)
	if string(got) != "echo:ping-pong" {
		t.Fatalf("the producer answered %q", got)
	}
	// the producer ended its direction: the page sees the empty message, then the stream ends with 1000
	end, err := c.recvMessage(5 * time.Second)
	if err != nil || len(end) != 0 {
		t.Fatalf("after the answer the half-close is the empty message, got %d bytes, %v", len(end), err)
	}
	if code := c.expectClosedWith(5 * time.Second); code != 1000 {
		t.Fatalf("the finished relay ends with Close 1000, got %d", code)
	}
}

func TestAnOutsiderConnectOverTheWebSocketIsRefusedWhereTheTLSOpeningRefuses(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	_, resID, _, _ := shareProducer(t, env, "alice", nil)

	// an id that is nobody's is the uniform `unavailable`, one message and a close - never `connected`
	c, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Connect{ShareID: env.NewID()})
	wsRefused(t, c, wire.ReasonUnavailable)

	// a resourceId is never a capability: an outsider that offers one is closed with no answer at all
	c, _ = wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Connect{ResourceID: resID})
	c.expectClosedWith(5 * time.Second)
}

// clipboard over the WebSocket (22 items BW and BY)

func TestTheClipboardPutAndGetWorkOverTheWebSocketAndTheEmptyMessageEndsAPutAtOnce(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardProbe = 3 * time.Second // a put that is not half-closed would wait this long for a stray byte
	o := looseOpts("alice")
	o.Params = &p
	env := testkit.StartServer(t, o)
	d := webDevice(t, env, "alice")
	value := []byte("hello from the browser")

	// put says hello, declares size, sends body in two messages and then the empty message
	put := func(declared int, body []byte) *wsClient {
		t.Helper()
		c, _ := wsHello(t, env, d, 0)
		c.send(&wire.ClipboardPut{Kind: "text", Size: int64(declared)})
		wsExpect[*wire.ClipboardReady](t, c)
		half := len(body) / 2
		c.sendBytes(body[:half])
		c.sendBytes(body[half:])
		return c
	}

	c := put(len(value), value)
	start := time.Now()
	c.halfClose()
	stored := wsExpect[*wire.ClipboardStored](t, c)
	if took := time.Since(start); took > time.Second {
		t.Fatalf("the put was stored %v after the empty message: it waited for the probe window", took)
	}
	if stored.SHA256 != shaHex(value) {
		t.Fatalf("sha256 %q is not over the bytes", stored.SHA256)
	}
	if code := c.expectClosedWith(5 * time.Second); code != 1000 {
		t.Fatalf("a finished put ends with Close 1000, got %d", code)
	}

	// the get returns the value whole: clipboard-value, then exactly size raw bytes, then Close 1000
	g, _ := wsHello(t, env, d, 0)
	g.send(&wire.ClipboardGet{})
	v := wsExpect[*wire.ClipboardValue](t, g)
	if v.Kind != "text" || v.Size != int64(len(value)) || v.ClipID != stored.ClipID || v.SourceDeviceID != d.ID {
		t.Fatalf("clipboard-value: %+v", v)
	}
	if got := g.readBytes(int(v.Size), 5*time.Second); !bytes.Equal(got, value) {
		t.Fatalf("the get returned %q", got)
	}
	if code := g.expectClosedWith(5 * time.Second); code != 1000 {
		t.Fatalf("after the last byte of a get the stream ends with Close 1000, got %d", code)
	}

	// a put that ends short stores nothing and the earlier value stays
	short := put(10, []byte("abcd"))
	short.halfClose()
	for {
		m, err := short.recvMessage(5 * time.Second)
		if err != nil {
			break
		}
		if e, derr := wire.Decode(m[min(4, len(m)):]); derr == nil && e.TypeName() == wire.TypeClipboardStored {
			t.Fatal("a put of 4 of 10 bytes was stored")
		}
	}
	g, _ = wsHello(t, env, d, 0)
	g.send(&wire.ClipboardGet{})
	v = wsExpect[*wire.ClipboardValue](t, g)
	if v.ClipID != stored.ClipID || !bytes.Equal(g.readBytes(int(v.Size), 5*time.Second), value) {
		t.Fatalf("the earlier value did not stay: %+v", v)
	}

	// a clipId that is no longer the value is `unavailable`, one message and a close
	g2, _ := wsHello(t, env, d, 0)
	g2.send(&wire.ClipboardGet{ClipID: wire.NewID()})
	wsRefused(t, g2, wire.ReasonUnavailable)
}

// item 2 (presence): the tab is offline when its socket closes

func TestAWebSocketControlStreamIsOfflineAsSoonAsItsSocketCloses(t *testing.T) {
	endings := map[string]func(c *wsClient){
		"the page sends Close":     func(c *wsClient) { c.write(wsMasked(0x88, wsClosePayload(1000))) },
		"the transport is dropped": func(c *wsClient) { _ = c.conn.Close() },
		"bye then close":           func(c *wsClient) { c.send(&wire.Bye{}) },
	}
	for name, end := range endings {
		t.Run(name, func(t *testing.T) {
			env := testkit.StartServer(t, looseOpts("alice"))
			observer := env.NewDevice("alice")
			mustEnroll(t, observer)
			oc, err := observer.Online()
			if err != nil {
				t.Fatal(err)
			}
			web := webDevice(t, env, "alice")
			oc.Drain()

			c, _ := wsHello(t, env, web, 120)
			c.send(&wire.Subscribe{}) // the first envelope after welcome fixes a control stream
			online := false
			for !online {
				ch, err := oc.Await(3*time.Second, wire.TypeChanged)
				if err != nil {
					t.Fatalf("the observer was not told the device came online: %v", err)
				}
				for _, r := range recs(ch.(*wire.Changed).Upserts.Devices) {
					if id, _ := r.Str("deviceId"); id == web.ID {
						p, _ := r.Str("presence")
						online = p == "online"
					}
				}
			}
			// an open socket keeps the device online: the keepalive window (3 x 120 s) is not what ends it
			if ch, err := oc.Await(300*time.Millisecond, wire.TypeChanged); err == nil {
				t.Fatalf("a device with an open socket changed: %+v", ch)
			}

			end(c)
			gone := false
			for !gone {
				ch, err := oc.Await(2*time.Second, wire.TypeChanged)
				if err != nil {
					t.Fatalf("the observer was not told the device went offline within 2 s of the socket closing: %v", err)
				}
				for _, r := range recs(ch.(*wire.Changed).Upserts.Devices) {
					if id, _ := r.Str("deviceId"); id == web.ID {
						p, _ := r.Str("presence")
						gone = p == "offline"
					}
				}
			}
		})
	}
}

// a second stream kind on one socket is refused

func TestASecondStreamKindOnOneWebSocketIsRefused(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32})
	if err != nil {
		t.Fatal(err)
	}
	o := looseOpts("alice")
	o.Admin = config.Admin{On: true, PasswordRecord: hash}
	env := testkit.StartServer(t, o)
	d := webDevice(t, env, "alice")

	// hello, then an enroll or an admin-auth on the same socket: the control stream does not take another kind
	for name, second := range map[string]wire.Envelope{
		"enroll":     &wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: env.NewID(), DeviceName: "x", Product: "fms-web"},
		"hello":      &wire.Hello{DeviceID: d.ID, DeviceToken: d.Token},
		"admin-auth": &wire.AdminAuth{Password: "SecretAdmin1"},
		"connect":    &wire.Connect{ShareID: env.NewID()},
	} {
		t.Run("hello then "+name, func(t *testing.T) {
			c, _ := wsHello(t, env, d, 0)
			c.send(&wire.Subscribe{}) // fixes the control stream
			c.sendTry(second)
			c.expectClosedWith(5 * time.Second) // closed, and no admin-auth-ok or other answer first
		})
	}

	// an enroll, then an admin-auth: an authenticated device stream never becomes an operator's
	c, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	c.send(&wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: env.NewID(), DeviceName: "x", Product: "fms-web"})
	wsExpect[*wire.Enrolled](t, c)
	c.sendTry(&wire.AdminAuth{Password: "SecretAdmin1"})
	c.expectClosedWith(5 * time.Second)

	// an administration stream, then a device kind: the first envelope fixed it, a second one closes it
	for name, second := range map[string]wire.Envelope{
		"hello":   &wire.Hello{DeviceID: d.ID, DeviceToken: d.Token},
		"enroll":  &wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: env.NewID(), DeviceName: "x", Product: "fms-web"},
		"connect": &wire.Connect{ShareID: env.NewID()},
	} {
		t.Run("admin-auth then "+name, func(t *testing.T) {
			a, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
			a.send(&wire.AdminAuth{Password: "SecretAdmin1"})
			wsExpect[*wire.AdminAuthOK](t, a)
			a.sendTry(second)
			a.expectClosedWith(5 * time.Second)
		})
	}

	// the happy path next to them: one kind per socket works, and an administration stream still serves
	// a request after a keepalive it did not need
	a, _ := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
	a.send(&wire.AdminAuth{Password: "SecretAdmin1"})
	ok := wsExpect[*wire.AdminAuthOK](t, a)
	a.send(&wire.Keepalive{})
	a.send(&wire.AdminRequest{ID: 1, Token: ok.Token, Op: "status"})
	if r := wsExpect[*wire.AdminReply](t, a); !r.OK {
		t.Fatalf("status over the WebSocket: %+v", r)
	}
}

// item 4: the Origin rule for a device stream

func TestADeviceStreamOverTheWebSocketFollowsTheOriginRule(t *testing.T) {
	o := looseOpts("alice")
	o.Admin = config.Admin{Origins: []string{"https://app.example"}}
	env := testkit.StartServer(t, o)
	d := webDevice(t, env, "alice")
	host := env.Addr

	sameOrigin := "https://" + host
	for name, origin := range map[string]string{
		"the server's own origin (the page it serves)":   sameOrigin,
		"no Origin (a native client)":                    "",
		"an origin the operator lists (remote consumer)": "https://app.example",
		"a listed origin spelled with its default port":  "https://APP.example:443",
	} {
		t.Run("accepted: "+name, func(t *testing.T) {
			c, status := wsOpen(t, env.Addr, host, origin)
			if status != 101 {
				t.Fatalf("opening answered %d, want 101", status)
			}
			c.send(&wire.Hello{DeviceID: d.ID, DeviceToken: d.Token})
			if w := wsExpect[*wire.Welcome](t, c); w.Account != "alice" {
				t.Fatalf("welcome: %+v", w)
			}
			c.send(&wire.Bye{})
			c.expectClosedWith(5 * time.Second)
		})
	}

	for name, origin := range map[string]string{
		"another site": "https://evil.example",
		"a name that merely starts like a listed one": "https://app.example.evil.example",
		"the server's host on another port":           "https://127.0.0.1:1",
		"the server's host over http":                 "http://" + host,
		"the opaque origin":                           "null",
		"a listed origin over http":                   "http://app.example",
		"a listed origin on another port":             "https://app.example:8443",
		"an origin with a path":                       sameOrigin + "/x",
	} {
		t.Run("refused: "+name, func(t *testing.T) {
			c, status := wsOpen(t, env.Addr, host, origin)
			if status != 403 || c != nil {
				t.Fatalf("opening answered %d, want 403", status)
			}
		})
	}
}

// the client address is the TCP peer

// wsOpenWith is wsOpen with extra request headers (a proxy's, or a page's own forgery of one).
func wsOpenWith(t *testing.T, addr, origin string, extra ...string) *wsClient {
	t.Helper()
	conn, err := tls.Dial("tcp", addr, &tls.Config{InsecureSkipVerify: true})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = conn.Close() })
	req := "GET /v2/ws HTTP/1.1\r\nHost: " + addr + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
		"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" +
		"Sec-WebSocket-Protocol: fms-exchange-v2\r\nOrigin: " + origin + "\r\n" + strings.Join(extra, "\r\n") + "\r\n\r\n"
	if _, err := conn.Write([]byte(req)); err != nil {
		t.Fatal(err)
	}
	br := bufio.NewReader(conn)
	resp, err := http.ReadResponse(br, nil)
	if err != nil {
		t.Fatal(err)
	}
	if resp.StatusCode != 101 {
		t.Fatalf("opening: %d", resp.StatusCode)
	}
	return &wsClient{t: t, conn: conn, br: br}
}

func TestTheAuditNamesTheTCPPeerAndTheWebSocketNeverAForwardedAddress(t *testing.T) {
	var capture auditCapture
	opts := looseOpts("alice")
	opts.Audit = logx.NewAudit(&capture)
	env := testkit.StartServer(t, opts)
	d := webDevice(t, env, "alice")

	c := wsOpenWith(t, env.Addr, "https://"+env.Addr, "X-Forwarded-For: 203.0.113.77", "Forwarded: for=203.0.113.78", "X-Real-IP: 203.0.113.79")
	peer := c.conn.LocalAddr().String()
	c.send(&wire.Hello{DeviceID: d.ID, DeviceToken: d.Token})
	wsExpect[*wire.Welcome](t, c)
	c.send(&wire.Subscribe{})
	eventually(t, "the device.connect audit line of the WebSocket", func() bool {
		return strings.Contains(capture.String(), "@ "+peer+" WebSocket] ")
	})
	c.send(&wire.Bye{})
	c.expectClosedWith(5 * time.Second)
	eventually(t, "the device.disconnect audit line", func() bool { return strings.Contains(capture.String(), "OP=device.disconnect") })
	if got := capture.String(); strings.Contains(got, "203.0.113.") {
		t.Fatalf("a forwarded address reached the audit: %s", got)
	}
	for _, line := range strings.Split(capture.String(), "\n") {
		if strings.Contains(line, "OP=device.connect") && !strings.Contains(line, "@ "+peer+" WebSocket] ") {
			t.Fatalf("the connect of a WebSocket device is not attributed to its TCP peer and its transport: %s", line)
		}
	}
}

// the first frame is judged as on the TLS opening

func TestTheFirstFrameOfAWebSocketIsJudgedLikeTheOneOfATLSStream(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := webDevice(t, env, "alice")
	open := func() *wsClient {
		c, status := wsOpen(t, env.Addr, env.Addr, "https://"+env.Addr)
		if status != 101 {
			t.Fatalf("opening: %d", status)
		}
		return c
	}

	// the legacy wire is refused with the server's own schemaVersion and the stream ends
	c := open()
	c.write(wsMasked(0x82, testkit.RawFrame(`{"schemaVersion":1,"type":"hello","deviceId":"`+d.ID+`","deviceToken":"`+d.Token+`"}`)))
	wsRefused(t, c, wire.ReasonVersionUnsupported)

	// any other version, no version, or a malformed envelope ends the stream with no answer
	for _, body := range []string{
		`{"schemaVersion":3,"type":"keepalive"}`, `{"type":"keepalive"}`, `{"schemaVersion":"2","type":"keepalive"}`,
		`{"schemaVersion":2,"type":"enroll","login":"alice","password":"x"}`, `not json`, `[]`,
	} {
		c := open()
		c.write(wsMasked(0x82, testkit.RawFrame(body)))
		c.expectClosedWith(5 * time.Second)
	}

	// an unknown type is ignored, and the stream still takes its hello
	c = open()
	c.write(wsMasked(0x82, testkit.RawFrame(`{"schemaVersion":2,"type":"from-the-future","x":1}`)))
	c.send(&wire.Hello{DeviceID: d.ID, DeviceToken: d.Token})
	wsExpect[*wire.Welcome](t, c)
	c.send(&wire.Bye{})
	c.expectClosedWith(5 * time.Second)
}
