package rendezvous_test

import (
	"bytes"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 234, item DQ of DEVICE-EXCHANGE 27: the server never logs a box, a sealed record or a key ring,
// and shows none of them in an administration report. A sealed session is run end to end against the real
// server, with every member of every box (n, ct, enc, wk, fromKey) and the first bytes of every sealed
// payload kept as needles; none of them may be in the captured log, in the log file, in an admin report,
// or - for what is memory-only - anywhere in the data directory.

// leakForms are the shapes a byte string takes if someone prints it: itself, hex in both cases, base64 in
// both alphabets with and without padding.
func leakForms(raw []byte) []string {
	h := hex.EncodeToString(raw)
	return []string{string(raw), h, strings.ToUpper(h),
		base64.StdEncoding.EncodeToString(raw), base64.RawStdEncoding.EncodeToString(raw),
		base64.URLEncoding.EncodeToString(raw), base64.RawURLEncoding.EncodeToString(raw)}
}

// prefixNeedles are the leak forms of the first 24 bytes of each payload: 24 is a multiple of 3, so the
// base64 of the prefix is a prefix of the base64 of the whole.
func prefixNeedles(payloads ...[]byte) []string {
	var out []string
	for _, p := range payloads {
		out = append(out, leakForms(p[:24])...)
	}
	return out
}

// findNeedle returns the first needle the haystack carries, or "".
func findNeedle(haystack string, needles []string) string {
	for _, n := range needles {
		if n != "" && strings.Contains(haystack, n) {
			return n
		}
	}
	return ""
}

func assertNoNeedle(t *testing.T, where, haystack string, needles []string) {
	t.Helper()
	if n := findNeedle(haystack, needles); n != "" {
		t.Fatalf("%s carries %q.. of a sealed value", where, n[:min(len(n), 12)])
	}
}

// The scan is only worth its verdict if it finds a value in every shape a careless line would print it in.
func TestTheLeakScanFindsASealedValueInEveryShapeItCouldBePrintedIn(t *testing.T) {
	raw := opaque(t, 4000)
	for i, form := range leakForms(raw[:24]) {
		for _, line := range []string{"payload=" + form, form, "x " + form + " y"} {
			if findNeedle(line, prefixNeedles(raw)) == "" {
				t.Errorf("shape %d of a sealed payload was not found in %q", i, line[:min(len(line), 30)])
			}
		}
	}
	// the whole payload printed as base64 or hex carries the prefix of it
	whole := base64.StdEncoding.EncodeToString(raw)
	if findNeedle("sealed put "+whole, prefixNeedles(raw)) == "" || findNeedle("sealed put "+hex.EncodeToString(raw), prefixNeedles(raw)) == "" {
		t.Error("a whole payload printed as base64 or hex carries its prefix")
	}
	if findNeedle("clipboard-stored size=4000 kind=sealed", prefixNeedles(raw)) != "" {
		t.Error("a line that carries no sealed byte is no finding")
	}
}

func TestASealedSessionLeavesNoBoxMemberNoKeyAndNoSealedByteInTheLogTheAdminReportsOrTheDisk(t *testing.T) {
	p := testkit.TestParams()
	p.ClipboardProbe, p.ClipboardIdle = 200*time.Millisecond, 300*time.Millisecond
	o := looseOpts("alice")
	o.WithRelay, o.Sealed, o.Cast, o.LogFile = true, true, true, true
	o.PublicEndpoint = "203.0.113.5:22022"
	o.Params = &p
	env := testkit.StartServer(t, o)

	// two devices of the account that seal: a producer and a receiver
	prod := env.NewProducer("alice", sshPW)
	prodKey := e2eDevice(t, prod.Device)
	rcv := env.NewDevice("alice")
	rcvKey := e2eDevice(t, rcv)
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	defer prod.Close()
	mustEnroll(t, rcv)
	rc, err := rcv.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	defer rcv.Close()

	var sealedBytes [][]byte // the sealed payloads that crossed the server: chunks and clipboard bytes
	var needles []string     // everything that must be absent from the log and from the reports
	keep := func(b testBox) testBox {
		needles = append(needles, b.Needles...)
		return b
	}

	// 1. a resource published with sealedAccess instead of access (item DI)
	access := keep(newTestBox(t, prod.ID, prodKey.KEM, 300, prod.ID, rcv.ID))
	resID := env.NewID()
	rec := wire.NewRecord()
	rec.SetStr("resourceId", resID)
	rec.SetStr("deviceId", prod.ID)
	rec.SetStr("kind", "sftp-share")
	rec.SetStr("name", "Photos")
	rec.SetRaw("sealedAccess", json.RawMessage(access.JSON))
	if ans, err := prod.Publish(rec.Marshal()); err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("a sealed publish: %v %+v", err, ans)
	}

	// 2. a sealed broadcast with a box, a sealed relay upload, a new box (broadcast-update) and a cast
	bcast := keep(newTestBox(t, prod.ID, prodKey.KEM, 400, prod.ID, rcv.ID))
	bid, up := attachSealedBroadcast(t, env, prod, bcast)
	header := joinBytes(sealedKeyRing(t, 1, "video/mp2t", 2), sealedMedia(t, 1, 376))
	joinable := sealedMedia(t, 1, 2000)
	writeChunk(t, up, wire.ChunkHeader, header)
	writeChunk(t, up, wire.ChunkJoinable, joinable)
	sealedBytes = append(sealedBytes, header, joinable)
	waitBytesIn(t, env, bid, int64(len(header)+len(joinable)))
	l := listen(t, env, bid)
	expectBody(t, l, "the sealed relay", joinBytes(header, joinable))

	bcast2 := keep(newTestBox(t, prod.ID, prodKey.KEM, 410, prod.ID, rcv.ID))
	if err := prod.Control().Send(&wire.BroadcastUpdate{Broadcast: sealedBroadcastJSON(bid, "Sealed again", bcast2)}); err != nil {
		t.Fatal(err)
	}
	if err := prod.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID}); err != nil {
		t.Fatal(err)
	}
	awaitOffer(t, rc)

	// 3. a sealed clipboard put, read by the other device, and a sealed put that is cut off
	clipBox := keep(newTestBox(t, prod.ID, prodKey.KEM, 250, prod.ID, rcv.ID))
	clipBytes := opaque(t, 4000)
	sealedBytes = append(sealedBytes, clipBytes)
	c, ans, err := clipHello(t, prod.Device, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: int64(len(clipBytes)), Sealed: json.RawMessage(clipBox.JSON)})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.ClipboardReady); !ok {
		t.Fatalf("a sealed put is taken by a server that advertises sealed: %+v", ans)
	}
	if _, err := c.Conn.Write(clipBytes); err != nil {
		t.Fatal(err)
	}
	if err := halfClose(c); err != nil {
		t.Fatal(err)
	}
	if st, err := c.Recv(5 * time.Second); err != nil || st.TypeName() != wire.TypeClipboardStored {
		t.Fatalf("%v %+v", err, st)
	}
	v, data := clipValue(t, rcv)
	if v.Kind != wire.ClipKindSealed || !bytes.Equal(data, clipBytes) || !bytes.Equal(v.Sealed, []byte(clipBox.JSON)) {
		t.Fatalf("the sealed value and its box are delivered as they came: %+v", v)
	}
	cut := opaque(t, 1000)
	sealedBytes = append(sealedBytes, cut)
	c2, ans2, err := clipHello(t, prod.Device, &wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: 4000, Sealed: json.RawMessage(keep(newTestBox(t, prod.ID, prodKey.KEM, 90, prod.ID)).JSON)})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans2.(*wire.ClipboardReady); !ok {
		t.Fatalf("%+v", ans2)
	}
	_, _ = c2.Conn.Write(cut)
	_ = halfClose(c2)
	_ = c2.SetReadDeadline(time.Now().Add(5 * time.Second))
	_, _ = io.ReadAll(c2.Conn) // the server drops it: the log says so

	// the needles really went through the server: the other device lists them
	dir, err := rc.ListDirectory()
	if err != nil {
		t.Fatal(err)
	}
	listed, _ := json.Marshal(dir)
	for _, n := range append(access.Needles, bcast2.Needles...) {
		if !strings.Contains(string(listed), n) {
			t.Fatalf("the directory a device lists lacks %q.. - the test would prove nothing", n[:8])
		}
	}

	// 4. refusals in a sealed context: a box that is no object, and an upload of a device that is not the broadcaster's
	other := env.NewProducer("alice", sshPW)
	e2eDevice(t, other.Device)
	if err := other.Start(); err != nil {
		t.Fatal(err)
	}
	defer other.Close()
	garbage := b64(opaque(t, 40))
	needles = append(needles, garbage)
	bad := wire.NewRecord()
	bad.SetStr("resourceId", env.NewID())
	bad.SetStr("deviceId", other.ID)
	bad.SetStr("kind", "sftp-share")
	bad.SetStr("name", "Broken")
	bad.SetRaw("sealedAccess", json.RawMessage(`"`+garbage+`"`))
	refusal, _ := other.Publish(bad.Marshal())
	if conn, a, _ := other.Upload(bid, wire.SealedContentType); conn != nil {
		t.Fatalf("an upload of another device must be refused: %+v", a)
	} else if a != nil {
		refusal = a
	}
	if refusal != nil {
		b, _ := json.Marshal(refusal)
		assertNoNeedle(t, "an answer to a refused sealed request", string(b), needles)
	}

	_ = prod.EndBroadcast(bid)
	eventually(t, "the relay-ended line", func() bool {
		for _, ln := range env.Log.Lines() {
			if strings.Contains(ln, "relay-ended") {
				return true
			}
		}
		return false
	})
	eventually(t, "the dropped sealed put in the log", func() bool {
		for _, ln := range env.Log.Lines() {
			if strings.Contains(ln, "clipboard-put-dropped") {
				return true
			}
		}
		return false
	})

	// what must be absent from the log: every box member, the device keys, the first bytes of every sealed payload
	logNeedles := append(append([]string{prodKey.KEM, rcvKey.KEM}, needles...), prefixNeedles(sealedBytes...)...)
	for _, n := range logNeedles {
		env.AddSecret(n)
	}
	logText := strings.Join(env.Log.Lines(), "\n")
	if strings.Count(logText, "\n") < 10 {
		t.Fatalf("the session wrote hardly any log (%d lines): the scan would prove nothing", strings.Count(logText, "\n")+1)
	}
	assertNoNeedle(t, "the captured log", logText, logNeedles)

	// what must be absent from every administration report
	var reports bytes.Buffer
	enc := json.NewEncoder(&reports)
	devs, _ := env.Ops.ListDevices("alice")
	_ = enc.Encode(env.Ops.Status())
	_ = enc.Encode(env.Ops.ListAccounts())
	_ = enc.Encode(devs)
	_ = enc.Encode(env.Ops.ListGrants("alice", ""))
	_ = enc.Encode(env.Ops.ListInvitations())
	assertNoNeedle(t, "an administration report", reports.String(), logNeedles)

	// what is memory-only (boxes, sealed payloads) is nowhere in the data directory; the device keys are public
	// and kept on disk with the device record (item DQ), so they are not looked for there
	diskNeedles := append(append([]string{}, access.Needles...), needles...)
	diskNeedles = append(diskNeedles, prefixNeedles(sealedBytes...)...)
	var kems = map[string]bool{prodKey.KEM: true, rcvKey.KEM: true}
	err = filepath.Walk(env.DataDir, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		b, rerr := os.ReadFile(path)
		if rerr != nil {
			return nil
		}
		for _, n := range diskNeedles {
			if n != "" && !kems[n] && bytes.Contains(b, []byte(n)) {
				t.Errorf("%s carries %q.. of a sealed value", filepath.Base(path), n[:min(len(n), 12)])
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
}
