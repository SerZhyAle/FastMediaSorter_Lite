package rendezvous_test

import (
	"bytes"
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 234, item DN of DEVICE-EXCHANGE 27: the sealed clipboard kind, end to end over TLS on loopback.
// A sealed `clipboard-put` declares `size` (the sealed bytes) and `sealed` (the box) and nothing that
// describes the content; the server stores both as it stores any value - the order of item BX, the size
// checks of item BY and the limit of item BZ are about the sealed bytes - and `clipboard-state` and
// `clipboard-value` carry kind `sealed` and the box. The bytes are ciphertext the server never looks at.

// sealedClipBox is the box of the `one-segment` member of the `clipboard` vectors (e2e-v1.json): a real
// box in the compact form a sender writes. The server reads none of it.
const sealedClipBox = `{"v":1,"from":"dev-phone","fromKey":"BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw","n":"BC2hdMS2v4zmsfK-","ct":"6YkXLfZy1UuSVEcgoZBshhxhf_t4lNg-uCTbfefMg2M6NhJeMmzRRuuZvnmgRpWL2VUHbphySPhXvV-PUNgQTS4Qwq7AaOLXFpMoFGW4EqAOsW4c-IBcpD7FnhC-lHwUueTu4Ru6zTSUDDf_JngW6nfVshcqoxhXgDHnGjZFVRwnMhGLAUUK4926ivkOczAqTELkCPcqtAXo36FAweIUrrAig0oUJ0dHFLw5dWWfEGTaay48QPStrr31kfnn","keys":[{"to":"dev-tablet","enc":"BGaIjZ4FjWNcq-clSbHiM3Ruan93aXH51o0iq4BeNOvsfcpqPTvbhO0iuCV3jOw8dFHL6vMUzCVukZn5PPfzXZA","wk":"6mTnH4Hg63mQR--UVIfxNX3m29c574IYNK01g1Tx_CHNjcHyd4iuRbRz_TQ2_sfR"}]}`

// sealedClipEnv is clipEnv with the `sealed` feature on, the configuration a sealed client talks to.
func sealedClipEnv(t *testing.T, o testkit.Options) *testkit.Env {
	t.Helper()
	o.Sealed = true
	return clipEnv(t, o)
}

// sealedClipBytes returns n random bytes: what a sealed value looks like to the server.
func sealedClipBytes(t testing.TB, n int) []byte {
	t.Helper()
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		t.Fatal(err)
	}
	return b
}

// sealedClipDigestBox is a box that names the digest of the bytes it goes with, so a reader that gets a
// box beside somebody else's bytes can tell.
func sealedClipDigestBox(data []byte) string { return `{"v":1,"d":"` + shaHex(data) + `"}` }

// clipPutWhole puts data under the declaration m (its Size is set here): ready, the bytes, the write side
// closed, and the `clipboard-stored` answer.
func clipPutWhole(t *testing.T, d *testkit.Device, m *wire.ClipboardPut, data []byte) *wire.ClipboardStored {
	t.Helper()
	m.Size = int64(len(data))
	c, ans, err := clipHello(t, d, m)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
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

func sealedClipPut(t *testing.T, d *testkit.Device, box string, data []byte) *wire.ClipboardStored {
	t.Helper()
	return clipPutWhole(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Sealed: json.RawMessage(box)}, data)
}

// clipStateOf asks `clipboard-info` on the device's control stream; nil is an empty clipboard.
func clipStateOf(t *testing.T, ctl *testkit.Control) *wire.ClipboardValueBody {
	t.Helper()
	ans, err := ctl.Request(&wire.ClipboardInfo{}, 3*time.Second, wire.TypeClipboardState)
	if err != nil {
		t.Fatal(err)
	}
	return ans.(*wire.ClipboardState).Value
}

// requireSealedBody checks the members of a sealed value as `clipboard-state` and `clipboard-value` show them.
func requireSealedBody(t *testing.T, who string, v *wire.ClipboardValueBody, stored *wire.ClipboardStored, box string, data []byte) {
	t.Helper()
	if v == nil {
		t.Fatalf("%s: the clipboard shows no value", who)
	}
	if v.Kind != wire.ClipKindSealed {
		t.Fatalf("%s: kind %q is not sealed", who, v.Kind)
	}
	if string(v.Sealed) != box {
		t.Fatalf("%s: the box is not the one that was sent:\n got %s\nwant %s", who, v.Sealed, box)
	}
	if v.MimeType != "" || v.Name != "" {
		t.Fatalf("%s: a sealed value shows a mimeType or a name: %+v", who, v)
	}
	if v.ClipID != stored.ClipID || v.StoredAt != stored.StoredAt || v.SHA256 != stored.SHA256 || v.SHA256 != shaHex(data) {
		t.Fatalf("%s: the members the server set are not those of the put: %+v / %+v", who, v, stored)
	}
	if v.Size != int64(len(data)) {
		t.Fatalf("%s: size %d is not the %d sealed bytes", who, v.Size, len(data))
	}
}

// requireNoAnswerAndClosed requires the server to close the stream without a byte of answer.
func requireNoAnswerAndClosed(t *testing.T, what string, c *testkit.Conn) {
	t.Helper()
	_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
	n, err := c.Conn.Read(make([]byte, 1))
	var ne net.Error
	if err == nil || n != 0 || (errors.As(err, &ne) && ne.Timeout()) {
		t.Fatalf("%s must close the stream without an answer (read %d bytes, err %v)", what, n, err)
	}
}

// --- item DN: the put, the state and the value ---

func TestASealedPutIsStoredAndEveryDeviceOfTheAccountReadsTheBoxVerbatim(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice", "bob"))
	var devices []*testkit.Device
	for _, name := range []string{"Phone", "Tablet", "Watch"} {
		d := env.NewDevice("alice")
		d.Name = name
		if _, err := d.Enroll(); err != nil {
			t.Fatal(err)
		}
		devices = append(devices, d)
	}
	// the server offers the capability a sealed sender checks before it puts one (item DP)
	c, r, err := devices[0].Hello()
	if err != nil || c == nil {
		t.Fatalf("hello: %v %+v", err, r)
	}
	if w, ok := r.(*wire.Welcome); !ok || !wire.HasCapability(w.Features, wire.FeatureSealed) {
		t.Fatalf("welcome does not list the sealed feature: %+v", r)
	}
	_ = c.Close()

	data := sealedClipBytes(t, 1016) // one segment of 1000 clear bytes: 1000 + 16
	stored := sealedClipPut(t, devices[0], sealedClipBox, data)
	if stored.SHA256 != shaHex(data) {
		t.Fatalf("the server's sha256 %q is not over the sealed bytes", stored.SHA256)
	}
	for _, d := range devices {
		ctl, err := d.OnlineQuietly()
		if err != nil {
			t.Fatal(err)
		}
		state := clipStateOf(t, ctl)
		requireSealedBody(t, d.Name+" state", state, stored, sealedClipBox, data)
		if state.SourceDeviceID != devices[0].ID || state.SourceDeviceName != "Phone" {
			t.Fatalf("%s: the source device is not the sender: %+v", d.Name, state)
		}
		v, got := clipValue(t, d)
		requireSealedBody(t, d.Name+" value", &v.ClipboardValueBody, stored, sealedClipBox, data)
		if !bytes.Equal(got, data) {
			t.Fatalf("%s: the sealed bytes are not returned byte for byte", d.Name)
		}
	}
	// another account sees nothing of it
	bob := env.NewDevice("bob")
	if _, err := bob.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := bob.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	if v := clipStateOf(t, ctl); v != nil {
		t.Fatalf("another account's clipboard-info shows a value: %+v", v)
	}
}

// The box is an opaque member (item DH): delivered as it came, whatever it holds. A reader that opens the
// box with the associated data and the keys it was sealed with must never meet a re-marshalled one.
func TestAnyBoxComesBackAsItWasSent(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	boxes := map[string]string{
		"real box":              sealedClipBox,
		"members not sorted":    `{"z":1,"a":2,"m":{"y":1,"b":2}}`,
		"json escapes":          `{"s":"é\/\"\\ 😀 \n"}`,
		"raw utf-8":             `{"s":"é 日本語 😀"}`,
		"html characters":       `{"s":"<b>&amp;</b>"}`,
		"line separators":       "{\"s\":\"a b c\"}",
		"numbers kept as typed": `{"big":18446744073709551616,"f":1.0,"e":1E2,"neg":-0}`,
		"one record of json":    `{"p":"` + strings.Repeat("a", wire.MaxRecord-8) + `"}`,
	}
	for name, box := range boxes {
		data := sealedClipBytes(t, 33)
		stored := sealedClipPut(t, d, box, data)
		requireSealedBody(t, name+" state", clipStateOf(t, ctl), stored, box, data)
		v, got := clipValue(t, d)
		requireSealedBody(t, name+" value", &v.ClipboardValueBody, stored, box, data)
		if !bytes.Equal(got, data) {
			t.Fatalf("%s: the bytes changed", name)
		}
	}

	// a box a sender wrote with whitespace loses the whitespace and nothing else
	pretty := "{\n  \"v\": 1,\n  \"z\": [1, 2],\n  \"a\": \"x  y\",\n  \"n\": 1.50\n}"
	data := sealedClipBytes(t, 20)
	body := fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-put","kind":"sealed","size":%d,"sealed":%s}`, wire.SchemaVersion, len(data), pretty)
	c, r, err := d.Hello()
	if err != nil || c == nil {
		t.Fatalf("hello: %v %+v", err, r)
	}
	defer c.Close()
	if err := c.SendRaw(testkit.RawFrame(body)); err != nil {
		t.Fatal(err)
	}
	if ans, err := c.Recv(5 * time.Second); err != nil {
		t.Fatal(err)
	} else if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := c.Conn.Write(data); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(c); err != nil {
		t.Fatal(err)
	}
	ans, err := c.Recv(5 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	stored, ok := ans.(*wire.ClipboardStored)
	if !ok {
		t.Fatalf("want clipboard-stored, got %+v", ans)
	}
	var compact bytes.Buffer
	if err := json.Compact(&compact, []byte(pretty)); err != nil {
		t.Fatal(err)
	}
	requireSealedBody(t, "pretty box", clipStateOf(t, ctl), stored, compact.String(), data)
}

// --- the refusals ---

// A sealed put carries no mimeType and no name, a sealed put needs a box, and only a sealed put carries
// one: each is an invalid envelope, which closes the stream with no answer (7.1) - the way every `clipboard-put`
// outside the bounds of item BW is treated - and changes nothing.
func TestAPutThatBreaksItemDNClosesTheStreamAndKeepsTheValue(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	kept := sealedClipBytes(t, 64)
	stored := sealedClipPut(t, d, sealedClipBox, kept)

	const box = `{"v":1,"n":"x","ct":"y","keys":[]}`
	for name, members := range map[string]string{
		"a sealed put with a mimeType":           `"kind":"sealed","size":9,"mimeType":"text/plain","sealed":` + box,
		"a sealed put with a name":               `"kind":"sealed","size":9,"name":"a.txt","sealed":` + box,
		"a sealed put with no box":               `"kind":"sealed","size":9`,
		"a sealed put with a null box":           `"kind":"sealed","size":9,"sealed":null`,
		"a sealed put with an array for a box":   `"kind":"sealed","size":9,"sealed":[1]`,
		"a sealed put with a string for a box":   `"kind":"sealed","size":9,"sealed":"x"`,
		"a sealed put with a box above a record": `"kind":"sealed","size":9,"sealed":{"p":"` + strings.Repeat("a", wire.MaxRecord) + `"}`,
		"a sealed put of size 0":                 `"kind":"sealed","size":0,"sealed":` + box,
		"a text put with a box":                  `"kind":"text","size":9,"sealed":` + box,
		"an image put with a box":                `"kind":"image","size":9,"mimeType":"image/png","sealed":` + box,
		"a file put with a box":                  `"kind":"file","size":9,"mimeType":"application/pdf","name":"a.pdf","sealed":` + box,
	} {
		c, r, err := d.Hello()
		if err != nil || c == nil {
			t.Fatalf("hello: %v %+v", err, r)
		}
		body := fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-put",%s}`, wire.SchemaVersion, members)
		if err := c.SendRaw(testkit.RawFrame(body)); err != nil {
			t.Fatal(err)
		}
		requireNoAnswerAndClosed(t, name, c)
		_ = c.Close()
	}

	// nothing was stored, nothing was cut: the sealed value is the one before
	v, got := clipValue(t, d)
	requireSealedBody(t, "after the refusals", &v.ClipboardValueBody, stored, sealedClipBox, kept)
	if !bytes.Equal(got, kept) {
		t.Fatal("the bytes of the earlier sealed value changed")
	}
	if values, held := env.Engine.Clipboard().Counts(); values != 1 || held != int64(len(kept)) {
		t.Fatalf("the slot holds %d values, %d bytes after the refusals", values, held)
	}
}

// A guest announces nothing and reads nothing, sealed or not (item AO, item BW).
func TestAGuestCannotPutOrReadASealedValue(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	alice := env.NewDevice("alice")
	if _, err := alice.Enroll(); err != nil {
		t.Fatal(err)
	}
	data := sealedClipBytes(t, 48)
	sealedClipPut(t, alice, sealedClipBox, data)

	g := env.NewDevice("visitor")
	if _, err := g.Enroll(); err != nil {
		t.Fatal(err)
	}
	c, ans, err := clipHello(t, g, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 48, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonNotPermitted) {
		t.Fatalf("a guest's sealed put must be not-permitted: %+v", ans)
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
	ctl, err := g.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	if v := clipStateOf(t, ctl); v != nil {
		t.Fatalf("a guest's clipboard-info shows a value: %+v", v)
	}
	if _, got := clipValue(t, alice); !bytes.Equal(got, data) {
		t.Fatal("the guest's refusals touched the member's sealed value")
	}
}

// --- item BY, BZ: the sizes and the limit are about the stored sealed bytes ---

func TestTheSizeChecksAndTheLimitAreAboutTheSealedBytes(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardBytes = 1024
	o := looseOpts("alice")
	o.Params = &p
	env := sealedClipEnv(t, o)
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	// the box is no part of the size the limit bounds: 1024 sealed bytes with a 600-byte box are at the limit
	bigBox := `{"v":1,"pad":"` + strings.Repeat("a", 600) + `"}`
	data := sealedClipBytes(t, 1024)
	stored := sealedClipPut(t, d, bigBox, data)
	if stored.SHA256 != shaHex(data) {
		t.Fatalf("sha256 %q is not over the sealed bytes", stored.SHA256)
	}
	if values, held := env.Engine.Clipboard().Counts(); values != 1 || held != 1024 {
		t.Fatalf("the slot holds %d values, %d bytes: the box is counted, or the bytes are not", values, held)
	}
	// the value being replaced does not count against the new one
	data2 := sealedClipBytes(t, 1024)
	second := sealedClipPut(t, d, sealedClipBox, data2)

	// one byte above the limit: refused with nothing read, whatever the box
	c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 1025, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonLimitReached) {
		t.Fatalf("want refused limit-reached, got %+v", ans)
	}
	_ = c.Close()

	// a send that closes early or carries more than it declared is dropped, and the sealed value stays
	for name, sent := range map[string]int{"closed early": 40, "more than declared": 101} {
		c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 100, Sealed: json.RawMessage(bigBox)})
		if err != nil {
			t.Fatal(err)
		}
		if _, ok := ans.(*wire.ClipboardReady); !ok {
			t.Fatalf("%s: want clipboard-ready, got %+v", name, ans)
		}
		if _, err := c.Conn.Write(sealedClipBytes(t, sent)); err != nil {
			t.Fatal(err)
		}
		if err := halfClose(c); err != nil {
			t.Fatal(err)
		}
		requireNoAnswerAndClosed(t, name, c)
		_ = c.Close()
		v, got := clipValue(t, d)
		requireSealedBody(t, name, &v.ClipboardValueBody, second, sealedClipBox, data2)
		if !bytes.Equal(got, data2) {
			t.Fatalf("%s: the sealed value was changed", name)
		}
	}

	// the clipboard turned off for the account: every put is limit-reached, the sealed value stays readable
	zero := int64(0)
	if err := env.Ops.SetAccountLimits("alice", &accounts.AccountLimits{ClipboardMaxBytes: &zero}); err != nil {
		t.Fatal(err)
	}
	c, ans, err = clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 8, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonLimitReached) {
		t.Fatalf("a clipboard of 0 bytes must refuse a sealed put: %+v", ans)
	}
	_ = c.Close()
	v, got := clipValue(t, d)
	requireSealedBody(t, "after the limit of 0", &v.ClipboardValueBody, second, sealedClipBox, data2)
	if !bytes.Equal(got, data2) {
		t.Fatal("a lowered limit trimmed the sealed value")
	}
}

// --- item BX: which value is the last ---

func TestSealedAndPlainValuesReplaceEachOtherInTheOrderOfCompletion(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	type step struct {
		name string
		put  *wire.ClipboardPut
		data []byte
	}
	sealedPut := func(box string) *wire.ClipboardPut {
		return &wire.ClipboardPut{Kind: wire.ClipKindSealed, Sealed: json.RawMessage(box)}
	}
	steps := []step{
		{"a text", &wire.ClipboardPut{Kind: "text"}, []byte("a plain text")},
		{"a sealed value", sealedPut(sealedClipBox), sealedClipBytes(t, 1016)},
		{"an image", &wire.ClipboardPut{Kind: "image", MimeType: "image/png", Name: "shot"}, sealedClipBytes(t, 300)},
		{"a second sealed value", sealedPut(`{"v":1,"n":"second"}`), sealedClipBytes(t, 33)},
		{"a file", &wire.ClipboardPut{Kind: "file", MimeType: "application/pdf", Name: "a.pdf"}, sealedClipBytes(t, 70000)},
		{"a third sealed value", sealedPut(`{"v":1,"n":"third"}`), sealedClipBytes(t, 70000)},
		{"a last text", &wire.ClipboardPut{Kind: "text"}, []byte("the last text")},
	}
	var previous string
	for _, s := range steps {
		stored := clipPutWhole(t, d, s.put, s.data)
		if stored.ClipID == previous {
			t.Fatalf("%s: a stored value has a clipId new for every stored value", s.name)
		}
		previous = stored.ClipID
		state := clipStateOf(t, ctl)
		v, got := clipValue(t, d)
		if !bytes.Equal(got, s.data) {
			t.Fatalf("%s: the value is not what was put last", s.name)
		}
		if s.put.Kind == wire.ClipKindSealed {
			requireSealedBody(t, s.name+" state", state, stored, string(s.put.Sealed), s.data)
			requireSealedBody(t, s.name+" value", &v.ClipboardValueBody, stored, string(s.put.Sealed), s.data)
			continue
		}
		for who, b := range map[string]*wire.ClipboardValueBody{"state": state, "value": &v.ClipboardValueBody} {
			if b == nil || b.ClipID != stored.ClipID || b.Kind != s.put.Kind || b.MimeType != s.put.MimeType || b.Name != s.put.Name {
				t.Fatalf("%s %s: the members are not those of the put: %+v", s.name, who, b)
			}
			if len(b.Sealed) != 0 {
				t.Fatalf("%s %s: carries the box of the value before it: %s", s.name, who, b.Sealed)
			}
		}
	}
}

func TestABrokenOrIdleSealedUploadKeepsTheEarlierValue(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardIdle = 300 * time.Millisecond
	o := looseOpts("alice")
	o.Params = &p
	env := sealedClipEnv(t, o)
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	text := clipPutWhole(t, d, &wire.ClipboardPut{Kind: "text"}, []byte("kept text"))

	// a sealed upload that stops half way, and one that delivers no byte: dropped, the text stays
	c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 100, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	_, _ = c.Conn.Write(sealedClipBytes(t, 40))
	_ = halfClose(c)
	requireNoAnswerAndClosed(t, "a sealed upload that stops half way", c)
	_ = c.Close()
	c, ans, err = clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 100, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	requireNoAnswerAndClosed(t, "an idle sealed upload", c)
	_ = c.Close()
	if v := clipStateOf(t, ctl); v == nil || v.ClipID != text.ClipID || v.Kind != "text" || len(v.Sealed) != 0 {
		t.Fatalf("a broken sealed upload changed the text: %+v", v)
	}

	// a sealed value stays when a text upload breaks
	data := sealedClipBytes(t, 200)
	sealed := sealedClipPut(t, d, sealedClipBox, data)
	c, ans, err = clipHello(t, d, &wire.ClipboardPut{Kind: "text", Size: 100})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	_, _ = c.Conn.Write([]byte("only a few"))
	_ = halfClose(c)
	requireNoAnswerAndClosed(t, "a text upload that stops half way", c)
	_ = c.Close()
	requireSealedBody(t, "after the broken text", clipStateOf(t, ctl), sealed, sealedClipBox, data)
}

// While a sealed upload runs the previous value is readable, whole; a second upload is `capacity`; at the
// last byte the new value replaces it and the old clipId is no longer the value (items BX and BY).
func TestASealedUploadInFlightLeavesThePreviousValueReadable(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	previousData := sealedClipBytes(t, 500)
	previous := sealedClipPut(t, d, `{"v":1,"n":"previous"}`, previousData)

	data := sealedClipBytes(t, 20)
	putConn, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 20, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("want clipboard-ready, got %+v", ans)
	}
	if _, err := putConn.Conn.Write(data[:5]); err != nil {
		t.Fatal(err)
	}
	v, got := clipValue(t, d)
	requireSealedBody(t, "during the upload", &v.ClipboardValueBody, previous, `{"v":1,"n":"previous"}`, previousData)
	if !bytes.Equal(got, previousData) {
		t.Fatal("a get during a sealed upload did not return the previous value whole")
	}
	c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 20, Sealed: json.RawMessage(sealedClipBox)})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonCapacity) {
		t.Fatalf("a second upload in flight must be capacity: %+v", ans)
	}
	_ = c.Close()

	if _, err := putConn.Conn.Write(data[5:]); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(putConn); err != nil {
		t.Fatal(err)
	}
	st, err := putConn.Recv(5 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	stored, ok := st.(*wire.ClipboardStored)
	if !ok {
		t.Fatalf("want clipboard-stored, got %+v", st)
	}
	v, got = clipValue(t, d)
	requireSealedBody(t, "after the upload", &v.ClipboardValueBody, stored, sealedClipBox, data)
	if !bytes.Equal(got, data) {
		t.Fatal("the completed upload is not the value")
	}
	c, ans, err = clipGet(t, d, &wire.ClipboardGet{ClipID: previous.ClipID})
	if err != nil {
		t.Fatal(err)
	}
	if r, ok := ans.(*wire.Refused); !ok || r.Reason != string(wire.ReasonUnavailable) {
		t.Fatalf("a clipId that is no longer the value must be unavailable: %+v", ans)
	}
	_ = c.Close()
}

// A get that began is delivered whole, box and bytes, as it was when it began, whatever replaces the value
// meanwhile (item BY).
func TestASealedGetThatBeganIsDeliveredWholeAcrossAReplacement(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	phone, tablet := env.NewDevice("alice"), env.NewDevice("alice")
	for _, d := range []*testkit.Device{phone, tablet} {
		if _, err := d.Enroll(); err != nil {
			t.Fatal(err)
		}
	}
	const oldBox = `{"v":1,"n":"old"}`
	big := sealedClipBytes(t, 200<<10)
	sealedClipPut(t, phone, oldBox, big)

	c, ans, err := clipGet(t, tablet, &wire.ClipboardGet{})
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	head, ok := ans.(*wire.ClipboardValue)
	if !ok || string(head.Sealed) != oldBox || head.Kind != wire.ClipKindSealed {
		t.Fatalf("want the old sealed value, got %+v", ans)
	}
	// the phone replaces the value while the tablet has read only the head
	fresh := sealedClipBytes(t, 90)
	freshStored := sealedClipPut(t, phone, `{"v":1,"n":"new"}`, fresh)
	rest := make([]byte, head.Size)
	if _, err := io.ReadFull(c.Conn, rest); err != nil {
		t.Fatalf("reading the %d raw bytes: %v", head.Size, err)
	}
	if !bytes.Equal(rest, big) || head.SHA256 != shaHex(big) {
		t.Fatal("a get that began did not deliver the value it began with, whole")
	}
	v, got := clipValue(t, tablet)
	requireSealedBody(t, "after the replacement", &v.ClipboardValueBody, freshStored, `{"v":1,"n":"new"}`, fresh)
	if !bytes.Equal(got, fresh) {
		t.Fatal("the replacement is not the value")
	}
}

// --- item BW, BY: the server never inspects the bytes ---

func TestTheSealedBytesAreCiphertextTheServerNeverInspects(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	frame, err := wire.Frame(&wire.ClipboardEmpty{})
	if err != nil {
		t.Fatal(err)
	}
	refusedFrame, err := wire.Frame(wire.Refuse(wire.ReasonUnavailable))
	if err != nil {
		t.Fatal(err)
	}
	patterns := []struct {
		name string
		data []byte
	}{
		{"random, over several reads of the engine", sealedClipBytes(t, 3*(32<<10)+11)},
		{"random, two segments of the contract", sealedClipBytes(t, 65536+16+100+16)},
		{"one byte", []byte{0x5a}},
		{"zeros", make([]byte, 4096)},
		{"a json object", []byte(`{"kind":"text","size":3,"sealed":{}}`)},
		{"a clipboard-put envelope", []byte(`{"schemaVersion":2,"type":"clipboard-put","kind":"text","size":9}`)},
		{"whole wire frames", append(append([]byte(nil), frame...), refusedFrame...)},
		{"a png signature", append([]byte("\x89PNG\r\n\x1a\n"), sealedClipBytes(t, 64)...)},
		{"invalid utf-8", []byte{0xc3, 0x28, 0xa0, 0xa1, 0xe2, 0x28, 0xa1}},
	}
	for _, tc := range patterns {
		stored := sealedClipPut(t, d, sealedClipBox, tc.data)
		if stored.SHA256 != shaHex(tc.data) {
			t.Errorf("%s: the sha256 is not over the bytes", tc.name)
		}
		v, got := clipValue(t, d)
		if !bytes.Equal(got, tc.data) || v.Size != int64(len(tc.data)) || v.Kind != wire.ClipKindSealed {
			t.Errorf("%s: the sealed bytes did not come back byte for byte (%d of %d)", tc.name, len(got), len(tc.data))
		}
	}
}

// --- item CC and DQ: nothing of a sealed value in a log or on disk ---

// sealedClipLeakForms are the shapes a byte string takes if someone prints it: itself, hex in both
// cases, base64 in both alphabets with and without padding.
func sealedClipLeakForms(raw []byte) []string {
	h := hex.EncodeToString(raw)
	return []string{string(raw), h, strings.ToUpper(h),
		base64.StdEncoding.EncodeToString(raw), base64.RawStdEncoding.EncodeToString(raw),
		base64.URLEncoding.EncodeToString(raw), base64.RawURLEncoding.EncodeToString(raw)}
}

func TestNoBoxAndNoSealedByteOfTheClipboardFlowsReachesALogOrTheDataDirectory(t *testing.T) {
	lo, hi := testkit.FreeRange(2)
	p := testkit.TestParams()
	p.ClipboardBytes = 4096
	o := looseOpts("alice")
	o.PortRange = [2]int{lo, hi}
	o.LogFile = true
	o.Params = &p
	env := sealedClipEnv(t, o)
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	guest := env.NewDevice("visitor")
	if _, err := guest.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}

	// a box whose every member is recognisable, and a name and a media type a sealed put must not carry
	b64 := func(n int) string { return base64.RawURLEncoding.EncodeToString(sealedClipBytes(t, n)) }
	nonce, ct, fromKey, enc, wk := b64(12), b64(300), b64(65), b64(65), b64(48)
	boxOf := func(n string) string {
		return fmt.Sprintf(`{"v":1,"from":"dev-phone","fromKey":"%s","n":"%s","ct":"%s","keys":[{"to":"dev-tablet","enc":"%s","wk":"%s"}]}`, fromKey, n, ct, enc, wk)
	}
	box1, box2 := boxOf(nonce), boxOf(b64(12))
	mime, name := "text/needle-"+wire.NewID(), "needle-name-"+wire.NewID()+".txt"
	data1, data2 := sealedClipBytes(t, 1500), sealedClipBytes(t, 700)

	var answers []wire.Envelope
	refusal := func(c *testkit.Conn, ans wire.Envelope) {
		answers = append(answers, ans)
		_ = c.Close()
	}

	stored1 := sealedClipPut(t, d, box1, data1)
	requireSealedBody(t, "state", clipStateOf(t, ctl), stored1, box1, data1)
	if v, _ := clipValue(t, d); string(v.Sealed) != box1 {
		t.Fatal("the value does not carry the box")
	}
	stored2 := sealedClipPut(t, d, box2, data2) // replaces the first
	c, ans, err := clipGet(t, d, &wire.ClipboardGet{ClipID: stored1.ClipID})
	if err != nil {
		t.Fatal(err)
	}
	refusal(c, ans)
	c, ans, err = clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 4097, Sealed: json.RawMessage(box1)})
	if err != nil {
		t.Fatal(err)
	}
	refusal(c, ans)
	for _, sent := range []int{40, 101} { // closed early, more than declared
		c, ans, err := clipHello(t, d, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 100, Sealed: json.RawMessage(box1)})
		if err != nil {
			t.Fatal(err)
		}
		if _, ok := ans.(*wire.ClipboardReady); !ok {
			t.Fatalf("want clipboard-ready, got %+v", ans)
		}
		if _, err := c.Conn.Write(data1[:sent]); err != nil {
			t.Fatal(err)
		}
		_ = halfClose(c)
		requireNoAnswerAndClosed(t, "a broken sealed put", c)
		_ = c.Close()
	}
	// a sealed put that carries a media type and a name, and so is an invalid envelope
	c, r, err := d.Hello()
	if err != nil || c == nil {
		t.Fatalf("hello: %v %+v", err, r)
	}
	body := fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-put","kind":"sealed","size":9,"mimeType":%q,"name":%q,"sealed":%s}`, wire.SchemaVersion, mime, name, box1)
	if err := c.SendRaw(testkit.RawFrame(body)); err != nil {
		t.Fatal(err)
	}
	requireNoAnswerAndClosed(t, "a sealed put with a mimeType and a name", c)
	_ = c.Close()
	// a guest's sealed put and get
	c, ans, err = clipHello(t, guest, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 9, Sealed: json.RawMessage(box1)})
	if err != nil {
		t.Fatal(err)
	}
	refusal(c, ans)

	// what the server said to a refused sender is as clean as what it logged
	var said []string
	for _, a := range answers {
		b, err := wire.Encode(a)
		if err != nil {
			t.Fatal(err)
		}
		said = append(said, string(b))
	}

	needles := []string{box1, box2, nonce, ct, fromKey, enc, wk, mime, name, stored1.SHA256, stored2.SHA256, stored1.ClipID, stored2.ClipID, string(data1), string(data2)}
	for _, raw := range [][]byte{data1[:24], data2[:24], data1, data2} {
		needles = append(needles, sealedClipLeakForms(raw)...)
	}
	logged := strings.Join(env.Log.Lines(), "\n")
	if !strings.Contains(logged, "clipboard-stored") || !strings.Contains(logged, "sealed") {
		t.Fatalf("the clipboard flows left no line naming the kind, so the scan below proves nothing:\n%s", logged)
	}
	for _, n := range needles {
		if n == "" {
			continue
		}
		if strings.Contains(logged, n) {
			t.Fatalf("a log line carries %q.. of a sealed clipboard flow", n[:min(len(n), 12)])
		}
		for _, s := range said {
			if strings.Contains(s, n) {
				t.Fatalf("an answer to a refused sender carries %q..", n[:min(len(n), 12)])
			}
		}
	}
	if v := dataDirViolations(env.DataDir, manifestAllowed, needles, needles); len(v) != 0 {
		t.Fatalf("a sealed value, its box or its members reached the disk or a log file: %v", v)
	}
}

// --- the plain kinds are as they were ---

func TestTextImageAndFileValuesKeepWorkingWithAndWithoutTheSealedFeature(t *testing.T) {
	for _, sealed := range []bool{false, true} {
		o := looseOpts("alice")
		o.Sealed = sealed
		env := clipEnv(t, o)
		d := env.NewDevice("alice")
		if _, err := d.Enroll(); err != nil {
			t.Fatal(err)
		}
		ctl, err := d.OnlineQuietly()
		if err != nil {
			t.Fatal(err)
		}
		for _, put := range []*wire.ClipboardPut{
			{Kind: "text"},
			{Kind: "image", MimeType: "image/png"},
			{Kind: "image", MimeType: "image/jpeg", Name: "photo.jpg"},
			{Kind: "file", MimeType: "application/pdf", Name: "report.pdf"},
		} {
			data := sealedClipBytes(t, 700)
			if put.Kind == "text" {
				data = []byte("a plain text with a few words")
			}
			label := fmt.Sprintf("sealed feature %v, %s", sealed, put.Kind)
			stored := clipPutWhole(t, d, &wire.ClipboardPut{Kind: put.Kind, MimeType: put.MimeType, Name: put.Name}, data)
			if stored.SHA256 != shaHex(data) {
				t.Errorf("%s: the sha256 is not over the bytes", label)
			}
			state := clipStateOf(t, ctl)
			v, got := clipValue(t, d)
			if !bytes.Equal(got, data) {
				t.Errorf("%s: the bytes changed", label)
			}
			for who, b := range map[string]*wire.ClipboardValueBody{"state": state, "value": &v.ClipboardValueBody} {
				if b == nil || b.Kind != put.Kind || b.MimeType != put.MimeType || b.Name != put.Name || b.ClipID != stored.ClipID || b.Size != int64(len(data)) {
					t.Errorf("%s %s: the members are not those of the put: %+v", label, who, b)
					continue
				}
				if len(b.Sealed) != 0 { // not even a `null`
					t.Errorf("%s %s: a plain value carries a box member: %s", label, who, b.Sealed)
				}
			}
		}
	}
}

// -race over sealed and plain puts and gets through the wire: four accounts, each device puts and reads
// back at once, and every account ends on its own last whole value with the box that belongs to its bytes.
func TestConcurrentSealedPutsAndGetsOverTheWire(t *testing.T) {
	env := sealedClipEnv(t, looseOpts("alice", "bob", "carol", "dave"))
	var devices []*testkit.Device
	for _, login := range []string{"alice", "bob", "carol", "dave"} {
		d := env.NewDevice(login)
		if _, err := d.Enroll(); err != nil {
			t.Fatal(err)
		}
		devices = append(devices, d)
	}
	var wg sync.WaitGroup
	for n, d := range devices {
		wg.Add(1)
		go func(n int, d *testkit.Device) {
			defer wg.Done()
			for round := 0; round < 6; round++ {
				data := bytes.Repeat([]byte{byte('a' + n)}, 200+round)
				var stored *wire.ClipboardStored
				if round%3 == 2 {
					stored = clipPutWhole(t, d, &wire.ClipboardPut{Kind: "text"}, data)
				} else {
					stored = sealedClipPut(t, d, sealedClipDigestBox(data), data)
				}
				v, got := clipValue(t, d)
				if !bytes.Equal(got, data) || v.ClipID != stored.ClipID {
					t.Errorf("account %d round %d: the value is not the last complete put", n, round)
					return
				}
				if round%3 != 2 && string(v.Sealed) != sealedClipDigestBox(got) {
					t.Errorf("account %d round %d: the box does not belong to the bytes", n, round)
					return
				}
			}
		}(n, d)
	}
	wg.Wait()
}
