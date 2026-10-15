package rendezvous_test

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"io"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 229: the clipboard slot of DEVICE-EXCHANGE section 22 on the wire - the spec's section 4
// list, end to end over TLS on loopback. A put is `clipboard-put`, then `clipboard-ready`, then
// exactly size raw bytes, then `clipboard-stored`; a get answers `clipboard-value` plus the bytes,
// `clipboard-empty`, or a refusal (item BW, BY).

// clipEnv is a server whose clipboard knobs are test-sized: the probe window is short, so an
// oversize put and a completed one are judged in milliseconds. A caller's Params survive.
func clipEnv(t *testing.T, o testkit.Options) *testkit.Env {
	t.Helper()
	p := testkit.TestParams()
	if o.Params != nil {
		p = *o.Params
	}
	p.ClipboardProbe = 200 * time.Millisecond
	o.Params = &p
	return testkit.StartServer(t, o)
}

// halfClose closes the write side the way a sender says "that was all of it" (a TLS stream's
// CloseWrite): the server sees EOF after the bytes it has.
func halfClose(c *testkit.Conn) error {
	if cw, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return cw.CloseWrite()
	}
	return c.Close()
}

// clipHello opens a data stream with hello + clipboard-put and returns the connection after
// `clipboard-ready`, or the envelope that came instead (with a closed connection).
func clipHello(t *testing.T, d *testkit.Device, m *wire.ClipboardPut) (*testkit.Conn, wire.Envelope, error) {
	t.Helper()
	c, r, err := d.Hello()
	if err != nil || c == nil {
		return c, r, err
	}
	if err := c.Send(m); err != nil {
		return nil, nil, err
	}
	ans, err := c.Recv(5 * time.Second)
	if err != nil {
		return nil, nil, err
	}
	return c, ans, nil
}

func clipGet(t *testing.T, d *testkit.Device, m *wire.ClipboardGet) (*testkit.Conn, wire.Envelope, error) {
	t.Helper()
	c, r, err := d.Hello()
	if err != nil || c == nil {
		return c, r, err
	}
	if err := c.Send(m); err != nil {
		return nil, nil, err
	}
	ans, err := c.Recv(5 * time.Second)
	if err != nil {
		return nil, nil, err
	}
	return c, ans, nil
}

// clipPutBytes puts data of a kind whole: ready, the bytes, the write side closed, and the
// `clipboard-stored` answer.
func clipPutBytes(t *testing.T, d *testkit.Device, kind string, data []byte) *wire.ClipboardStored {
	t.Helper()
	m := &wire.ClipboardPut{Kind: kind, Size: int64(len(data))}
	if kind == "image" {
		m.MimeType = "image/png"
	}
	c, ans, err := clipHello(t, d, m)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := c.Conn.Write(data); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(c); err != nil {
		t.Fatal(err)
	}
	st, err := c.Recv(5 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	s, ok := st.(*wire.ClipboardStored)
	if !ok {
		t.Fatalf("want clipboard-stored, got %+v", st)
	}
	return s
}

func clipValue(t *testing.T, d *testkit.Device) (*wire.ClipboardValue, []byte) {
	t.Helper()
	c, ans, err := clipGet(t, d, &wire.ClipboardGet{})
	if err != nil {
		t.Fatal(err)
	}
	v, ok := ans.(*wire.ClipboardValue)
	if !ok {
		t.Fatalf("want clipboard-value, got %+v", ans)
	}
	data := make([]byte, v.Size)
	if _, err := io.ReadFull(c.Conn, data); err != nil {
		t.Fatalf("reading the %d raw bytes: %v", v.Size, err)
	}
	return v, data
}

func TestACompletePutReplacesAndAGetReturnsItWhole(t *testing.T) {
	env := clipEnv(t, looseOpts("alice", "bob"))
	alice := env.NewDevice("alice")
	if _, err := alice.Enroll(); err != nil {
		t.Fatal(err)
	}
	stored := clipPutBytes(t, alice, "text", []byte("hello from the phone"))
	if stored.SHA256 != shaHex([]byte("hello from the phone")) {
		t.Fatalf("sha256 %q is not over the bytes", stored.SHA256)
	}
	v, data := clipValue(t, alice)
	if !bytes.Equal(data, []byte("hello from the phone")) || v.Kind != "text" || v.ClipID != stored.ClipID {
		t.Fatalf("the get did not return the put: %+v %q", v, data)
	}
	if v.SourceDeviceID != alice.ID || v.SourceDeviceName != alice.Name || v.StoredAt == "" {
		t.Fatalf("the members the server sets are missing: %+v", v)
	}
	// the second complete put replaces the first; the old clipId is no longer the value
	second := clipPutBytes(t, alice, "text", []byte("second value"))
	if second.ClipID == stored.ClipID {
		t.Fatal("a stored value has a clipId new for every stored value")
	}
	c, ans, err := clipGet(t, alice, &wire.ClipboardGet{ClipID: stored.ClipID})
	if err != nil {
		t.Fatal(err)
	}
	r, ok := ans.(*wire.Refused)
	if !ok || r.Reason != string(wire.ReasonUnavailable) {
		t.Fatalf("a clipId that is no longer the value must be unavailable: %+v", ans)
	}
	_ = c.Close()
	v, data = clipValue(t, alice)
	if !bytes.Equal(data, []byte("second value")) || v.ClipID != second.ClipID {
		t.Fatalf("the replaced value is not what a get returns: %+v", v)
	}

	// another account never reads the slot
	bob := env.NewDevice("bob")
	if _, err := bob.Enroll(); err != nil {
		t.Fatal(err)
	}
	c, ans, err = clipGet(t, bob, &wire.ClipboardGet{})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardEmpty); !ok {
		t.Fatalf("another account must see an empty clipboard: %+v", ans)
	}
	_ = c.Close()
	ctl, err := bob.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	bi, err := ctl.Request(&wire.ClipboardInfo{}, 3*time.Second, wire.TypeClipboardState)
	if err != nil {
		t.Fatal(err)
	}
	if st := bi.(*wire.ClipboardState); st.Value != nil {
		t.Fatalf("another account's clipboard-info shows a value: %+v", st.Value)
	}

	// `clipboard-info` on the control stream shows the value and its members
	actl, err := alice.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	bi, err = actl.Request(&wire.ClipboardInfo{}, 3*time.Second, wire.TypeClipboardState)
	if err != nil {
		t.Fatal(err)
	}
	st := bi.(*wire.ClipboardState)
	if st.Value == nil || st.Value.ClipID != second.ClipID || st.Value.Kind != "text" || st.Value.SourceDeviceID != alice.ID {
		t.Fatalf("clipboard-info does not show the value: %+v", st.Value)
	}
}

// TestAGetDuringAPutReturnsThePreviousValue: the sender stalls mid-upload after `clipboard-ready`
// and a reader gets the previous value whole; the finished upload then replaces it.
func TestAGetDuringAPutReturnsThePreviousValue(t *testing.T) {
	env := clipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, d, "text", []byte("previous"))

	putConn, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 15})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := putConn.Conn.Write([]byte("half")); err != nil {
		t.Fatal(err)
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("previous")) {
		t.Fatalf("a get during a put must return the previous value, got %q", data)
	}
	// the upload finishes and replaces the value at its last byte
	if _, err := putConn.Conn.Write([]byte("-of-the-new")); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(putConn); err != nil {
		t.Fatal(err)
	}
	st, err := putConn.Recv(5 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := st.(*wire.ClipboardStored); !ok {
		t.Fatalf("want clipboard-stored, got %+v", st)
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("half-of-the-new")) {
		t.Fatalf("the completed upload is not the value: %q", data)
	}
}

// TestAnInterruptedPutLeavesTheEarlierValue: a stream that closes before size bytes (and one that
// goes idle) is dropped with the previous value kept.
func TestAnInterruptedPutLeavesTheEarlierValue(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardProbe, p.ClipboardIdle = 200*time.Millisecond, 300*time.Millisecond
	env := clipEnv(t, testkit.Options{Logins: []string{"alice"}, Params: &p})
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, d, "text", []byte("kept"))

	c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 100})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := c.Conn.Write([]byte("only a few")); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(c); err != nil {
		t.Fatal(err)
	}
	// the server closes the stream with nothing stored
	_ = c.SetReadDeadline(time.Now().Add(5 * time.Second))
	if _, err := c.Conn.Read(make([]byte, 1)); err == nil {
		t.Fatal("an interrupted put's stream must be closed")
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("kept")) {
		t.Fatalf("the interrupted put must leave the earlier value: %q", data)
	}

	// the idle window: a put that delivers no byte for ClipboardIdle is closed and dropped
	c2, ans2, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 100})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans2.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans2)
	}
	_ = c2.SetReadDeadline(time.Now().Add(5 * time.Second))
	if _, err := c2.Conn.Read(make([]byte, 1)); err == nil {
		t.Fatal("an idle put's stream must be closed")
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("kept")) {
		t.Fatalf("the idle put must leave the earlier value: %q", data)
	}
}

// TestTheDeclaredAndActualSizeLimitsKeepTheOldValue: a put that declares more than the account's
// `clipboardBytes` is refused before a byte flows, and one that sends more than it declared is
// dropped after its last declared byte (items BW and BZ).
func TestTheDeclaredAndActualSizeLimitsKeepTheOldValue(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardProbe = 200 * time.Millisecond
	p.ClipboardBytes = 16
	env := clipEnv(t, testkit.Options{Logins: []string{"alice"}, Params: &p})
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, d, "text", []byte("kept"))

	// declared: one byte above the account's limit, refused with nothing read
	c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 17})
	if err != nil {
		t.Fatal(err)
	}
	r, ok := ans.(*wire.Refused)
	if !ok || r.Reason != string(wire.ReasonLimitReached) {
		t.Fatalf("want refused limit-reached, got %+v", ans)
	}
	_ = c.Close()

	// the limit of 0 turns the clipboard off for the account
	zero := int64(0)
	if err := env.Ops.SetAccountLimits("alice", &accounts.AccountLimits{ClipboardMaxBytes: &zero}); err != nil {
		t.Fatal(err)
	}
	c, ans, err = clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 4})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonLimitReached) {
		t.Fatalf("a clipboard of 0 bytes must refuse every put: %+v", ans)
	}
	_ = c.Close()
	if err := env.Ops.SetAccountLimits("alice", nil); err != nil {
		t.Fatal(err)
	}

	// actual: two bytes beyond the declared size; the put is dropped and the old value stays
	putConn, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 4})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := putConn.Conn.Write([]byte("four!")); err != nil {
		t.Fatal(err)
	}
	_ = putConn.SetReadDeadline(time.Now().Add(5 * time.Second))
	if _, err := putConn.Conn.Read(make([]byte, 1)); err == nil {
		t.Fatal("an oversize put's stream must be closed with nothing stored")
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("kept")) {
		t.Fatalf("the oversize put must leave the old value: %q", data)
	}
}

// TestAGuestIsRefusedBothWays: the put and the get are one answer, `not-permitted`; the state is
// an empty one (22 item BY answers the info, ticket 229 rule 1 the two directions).
func TestAGuestIsRefusedBothWays(t *testing.T) {
	env := clipEnv(t, looseOpts("alice"))
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	alice := env.NewDevice("alice")
	if _, err := alice.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, alice, "text", []byte("member value"))

	g := env.NewDevice("visitor")
	if _, err := g.Enroll(); err != nil {
		t.Fatal(err)
	}
	c, ans, err := clipHello(t, g, &wire.ClipboardPut{Kind: "text", Size: 4})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonNotPermitted) {
		t.Fatalf("a guest's put must be not-permitted: %+v", ans)
	}
	_ = c.Close()
	c, ans, err = clipGet(t, g, &wire.ClipboardGet{})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonNotPermitted) {
		t.Fatalf("a guest's get must be not-permitted: %+v", ans)
	}
	_ = c.Close()
	// the member's value survived both refusals
	if _, data := clipValue(t, alice); !bytes.Equal(data, []byte("member value")) {
		t.Fatalf("the guest's refusals touched the member's value: %q", data)
	}
	// the info is answered an empty state
	ctl, err := g.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	bi, err := ctl.Request(&wire.ClipboardInfo{}, 3*time.Second, wire.TypeClipboardState)
	if err != nil {
		t.Fatal(err)
	}
	if st := bi.(*wire.ClipboardState); st.Value != nil {
		t.Fatalf("a guest's clipboard-info must show no value: %+v", st.Value)
	}
}

// TestTheSecondOverlappingPutIsCapacity: the account's one upload in flight, the second send
// refused `capacity` with the first finishing whole (ticket 229 T-1, items BX and BZ).
func TestTheSecondOverlappingPutIsCapacity(t *testing.T) {
	env := clipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, d, "text", []byte("kept"))

	first, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 5})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	second, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 5})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonCapacity) {
		t.Fatalf("the second overlapping put must be capacity: %+v", ans)
	}
	_ = second.Close()
	// the first finishes and is the value
	if _, err := first.Conn.Write([]byte("late!")); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(first); err != nil {
		t.Fatal(err)
	}
	st, err := first.Recv(5 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := st.(*wire.ClipboardStored); !ok {
		t.Fatalf("want clipboard-stored, got %+v", st)
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte("late!")) {
		t.Fatalf("the later completion must be the value: %q", data)
	}
}

// TestTheAccountErasuresTakeTheValueWithThem: a person's deletion, the operator's removal and the
// kind becoming a guest each leave no value; a server restart leaves none (memory only).
func TestTheAccountErasuresTakeTheValueWithThem(t *testing.T) {
	env := clipEnv(t, looseOpts("alice", "bob"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, d, "text", []byte("mine"))

	// a restart (a new engine over the same stores) holds nothing
	values, held := env.Engine.Clipboard().Counts()
	if values != 1 || held != 4 {
		t.Fatalf("before the restart: %d values, %d bytes", values, held)
	}
	env.Stop()
	env2 := clipEnv(t, testkit.Options{Logins: []string{"alice", "bob"}, DataDir: env.DataDir})
	if v := env2.Engine.Clipboard().Get("alice"); v != nil {
		t.Fatal("a restarted server holds a clipboard value")
	}
	// the slot is filled again, then erased by the holder's deletion
	d.Rebind(env2)
	clipPutBytes(t, d, "text", []byte("mine again"))
	if err := env2.Ops.RemoveAccount("alice"); err != nil {
		t.Fatal(err)
	}
	if v := env2.Engine.Clipboard().Get("alice"); v != nil {
		t.Fatal("a deleted account's value survived")
	}
	// the operator changes an account's kind to guest: the slot is erased
	b := env2.NewDevice("bob")
	if _, err := b.Enroll(); err != nil {
		t.Fatal(err)
	}
	clipPutBytes(t, b, "text", []byte("bob's"))
	if _, err := env2.Ops.SetKind("bob", "guest"); err != nil {
		t.Fatal(err)
	}
	if v := env2.Engine.Clipboard().Get("bob"); v != nil {
		t.Fatal("an account that became a guest kept its value")
	}
}

// TestNothingOfAValueReachesDiskOrLog: a run with a recognisable payload leaves the data directory
// on the manifest's allowed list, and neither the payload nor its name is in any file or log line,
// in any encoding the manifest scan knows (22 item CC, ticket 167's rule 2).
func TestNothingOfAValueReachesDiskOrLog(t *testing.T) {
	lo, hi := testkit.FreeRange(2)
	o := looseOpts("alice")
	o.PortRange = [2]int{lo, hi}
	o.LogFile = true
	env := clipEnv(t, o)
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	payload := "clip-payload-needle-" + wire.NewID()
	fileName := "clip-name-needle-" + wire.NewID() + ".bin"
	clipPutBytes(t, d, "text", []byte(payload))
	m := &wire.ClipboardPut{Kind: "file", Size: int64(len(payload)), MimeType: "application/octet-stream", Name: fileName}
	c, ans, err := clipHello(t, d, m)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := c.Conn.Write([]byte(payload)); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(c); err != nil {
		t.Fatal(err)
	}
	if _, err := c.Recv(5 * time.Second); err != nil {
		t.Fatal(err)
	}
	if _, data := clipValue(t, d); !bytes.Equal(data, []byte(payload)) {
		t.Fatalf("the get did not return the file value: %q", data)
	}
	if v := dataDirViolations(env.DataDir, manifestAllowed, []string{payload, fileName}, []string{payload, fileName}); len(v) != 0 {
		t.Fatalf("a clipboard value or its name reached disk or a log: %v", v)
	}
}

// -race over concurrent puts and gets through the wire (spec 229 section 4): four devices of four
// accounts put and get over TLS at once, and every account ends on its own last whole value.
func TestConcurrentPutsAndGetsOverTheWire(t *testing.T) {
	env := clipEnv(t, looseOpts("alice", "bob", "carol", "dave"))
	devices := make([]*testkit.Device, 4)
	for i, login := range []string{"alice", "bob", "carol", "dave"} {
		d := env.NewDevice(login)
		if _, err := d.Enroll(); err != nil {
			t.Fatal(err)
		}
		devices[i] = d
	}
	var wg sync.WaitGroup
	for i, d := range devices {
		wg.Add(1)
		go func(n int, d *testkit.Device) {
			defer wg.Done()
			for round := 0; round < 6; round++ {
				payload := []byte(strings.Repeat(string(rune('a'+n)), 200+round))
				stored := clipPutBytes(t, d, "text", payload)
				v, got := clipValue(t, d)
				if !bytes.Equal(got, payload) || v.ClipID != stored.ClipID {
					t.Errorf("account %d round %d: the value is not the last complete put", n, round)
					return
				}
			}
		}(i, d)
	}
	wg.Wait()
}

func shaHex(b []byte) string {
	sum := sha256.Sum256(b)
	return hex.EncodeToString(sum[:])
}

// TestARejectedPutEnvelopeClosesTheStream: a clipboard-put outside the bounds of item BW (a file
// with no name, a text with metadata, size 0) is an invalid envelope and closes the stream with no
// answer (7.1).
func TestARejectedPutEnvelopeClosesTheStream(t *testing.T) {
	env := clipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	for _, m := range []*wire.ClipboardPut{
		{Kind: "file", Size: 4},
		{Kind: "text", Size: 4, Name: "x"},
		{Kind: "text", Size: 0},
		{Kind: "folder", Size: 4},
		{Kind: "file", Size: 4, MimeType: "application/octet-stream", Name: "../escape"},
	} {
		c, _, err := d.Hello()
		if err != nil || c == nil {
			t.Fatalf("hello: %v", err)
		}
		if err := c.Send(m); err != nil {
			t.Fatal(err)
		}
		_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
		if _, err := c.Conn.Read(make([]byte, 1)); err == nil {
			t.Fatalf("%+v: an invalid clipboard-put must close the stream", m)
		}
		_ = c.Close()
	}
}
