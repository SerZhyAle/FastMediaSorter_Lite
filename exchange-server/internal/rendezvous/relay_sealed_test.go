package rendezvous_test

import (
	"bytes"
	"crypto/ecdh"
	"crypto/rand"
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"io"
	"net"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 234, item DL of DEVICE-EXCHANGE 27: the relay and the tunnel carry ciphertext. A sealed upload
// declares `application/x-fmsx-sealed`; every chunk payload is a sequence of records (len, type, body) that
// the server relays byte for byte. The flag byte of a chunk (0x01 joinable, 0x02 header) is the only part
// of a chunk the ring reads, so the ring keeps working on flags alone. These tests feed the real server
// payloads shaped like the records of the contract - and payloads that are no records at all.

const (
	recKeyRing byte = 0x4B // the key ring of item DL
	recMedia   byte = 0x4D // a sealed media record
	recUnknown byte = 0x7F // a type a reader skips
)

// opaque returns n random bytes: what a sealed payload looks like to the server.
func opaque(t testing.TB, n int) []byte {
	t.Helper()
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		t.Fatal(err)
	}
	return b
}

func joinBytes(parts ...[]byte) []byte { return bytes.Join(parts, nil) }

// sealedRecord is one record of item DL: len (4 bytes, big-endian, counting the type byte and the body),
// the type byte, the body.
func sealedRecord(typ byte, body []byte) []byte {
	b := make([]byte, 5+len(body))
	binary.BigEndian.PutUint32(b, uint32(1+len(body)))
	b[4] = typ
	copy(b[5:], body)
	return b
}

// sealedKeyRing builds a record of type 0x4B with the layout of item DL: epoch, the media type, count and
// count entries of keyId (8), nonce (12) and a wrapped epoch key (48) - all random here.
func sealedKeyRing(t testing.TB, epoch uint32, mediaType string, entries int) []byte {
	t.Helper()
	var body bytes.Buffer
	_ = binary.Write(&body, binary.BigEndian, epoch)
	body.WriteByte(byte(len(mediaType)))
	body.WriteString(mediaType)
	body.WriteByte(byte(entries))
	for i := 0; i < entries; i++ {
		body.Write(opaque(t, 8+12+48))
	}
	return sealedRecord(recKeyRing, body.Bytes())
}

// sealedMedia builds a record of type 0x4D: epoch, nonce (12 random bytes) and n sealed media bytes.
func sealedMedia(t testing.TB, epoch uint32, n int) []byte {
	t.Helper()
	var body bytes.Buffer
	_ = binary.Write(&body, binary.BigEndian, epoch)
	body.Write(opaque(t, 12))
	body.Write(opaque(t, n))
	return sealedRecord(recMedia, body.Bytes())
}

// newE2EKey makes a valid device key (item DF): a P-256 public key as the uncompressed SEC1 point.
func newE2EKey(t testing.TB) *wire.E2EKey {
	t.Helper()
	k, err := ecdh.P256().GenerateKey(rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	return &wire.E2EKey{V: wire.E2EKeyVersion, KEM: base64.RawURLEncoding.EncodeToString(k.PublicKey().Bytes())}
}

// e2eDevice gives a device the key and the capability of a client that seals.
func e2eDevice(t testing.TB, d *testkit.Device) *wire.E2EKey {
	t.Helper()
	d.E2EKey = newE2EKey(t)
	d.Capabilities = []string{wire.E2ECapability}
	return d.E2EKey
}

// testBox is a sealed box (item DH) as a test builds it: the server never opens it, so the members are
// random, and every member value is kept as a needle for the tests that look for leaks.
type testBox struct {
	JSON    string
	Needles []string // n, ct, fromKey and the enc and wk of every recipient
}

func b64(b []byte) string { return base64.RawURLEncoding.EncodeToString(b) }

// newTestBox builds a box from the device `from` (key fromKEM) to the recipients `to`.
func newTestBox(t testing.TB, from, fromKEM string, ctLen int, to ...string) testBox {
	t.Helper()
	n, ct := b64(opaque(t, 12)), b64(opaque(t, ctLen))
	needles := []string{n, ct, fromKEM}
	keys := ""
	for i, id := range to {
		enc, wk := b64(opaque(t, 65)), b64(opaque(t, 48))
		needles = append(needles, enc, wk)
		if i > 0 {
			keys += ","
		}
		keys += fmt.Sprintf(`{"to":%q,"enc":%q,"wk":%q}`, id, enc, wk)
	}
	return testBox{
		JSON:    fmt.Sprintf(`{"v":1,"from":%q,"fromKey":%q,"n":%q,"ct":%q,"keys":[%s]}`, from, fromKEM, n, ct, keys),
		Needles: needles,
	}
}

// sealedBroadcastJSON is a broadcast record of section 6.3 as a sealed one: the RELAY and TUNNEL endpoints
// carry `seal: {v}` and the record carries the box (item DK).
func sealedBroadcastJSON(id, title string, box testBox) []byte {
	return []byte(fmt.Sprintf(`{"broadcastId":%q,"mode":"VIDEO_AUDIO","title":%q,"descriptor":{"endpoints":[`+
		`{"transport":"HTTP_MPEGTS","kind":"RELAY","seal":{"v":1}},{"transport":"HTTP_MPEGTS","kind":"TUNNEL","seal":{"v":1}}]},"sealed":%s}`,
		id, title, box.JSON))
}

// sealedRelayEnv is a server with the real relay and the `sealed` feature on.
func sealedRelayEnv(t *testing.T) *testkit.Env {
	t.Helper()
	o := looseOpts("alice")
	o.WithRelay = true
	o.Sealed = true
	o.PublicEndpoint = "203.0.113.5:22022"
	return testkit.StartServer(t, o)
}

// startSealedBroadcast enrolls a producer that seals, starts a sealed broadcast and attaches a sealed
// upload (contentType application/x-fmsx-sealed).
func startSealedBroadcast(t *testing.T, env *testkit.Env, login string) (*testkit.Producer, string, *testkit.Conn) {
	t.Helper()
	p := env.NewProducer(login, sshPW)
	kem := e2eDevice(t, p.Device)
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(p.Close)
	id, up := attachSealedBroadcast(t, env, p, newTestBox(t, p.ID, kem.KEM, 120, p.ID))
	return p, id, up
}

// attachSealedBroadcast starts a sealed broadcast of an online producer with the given box and attaches a
// sealed upload to it.
func attachSealedBroadcast(t *testing.T, env *testkit.Env, p *testkit.Producer, box testBox) (string, *testkit.Conn) {
	t.Helper()
	id := env.NewID()
	r, err := p.Control().Request(&wire.BroadcastStart{Broadcast: sealedBroadcastJSON(id, "Sealed", box)},
		3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil || r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("broadcast-start: %v %+v", err, r)
	}
	up, ans, err := p.Upload(id, wire.SealedContentType)
	if err != nil || up == nil {
		t.Fatalf("a sealed upload must be accepted: %v %+v", err, ans)
	}
	t.Cleanup(func() { _ = up.Close() })
	return id, up
}

func writeChunk(t *testing.T, up *testkit.Conn, flags byte, payload []byte) {
	t.Helper()
	if err := up.WriteChunk(flags, payload); err != nil {
		t.Fatal(err)
	}
}

// expectBody reads len(want) bytes of a listener's body and fails unless they are exactly want.
func expectBody(t *testing.T, l *testkit.Listening, what string, want []byte) {
	t.Helper()
	got, err := l.ReadFull(len(want), 5*time.Second)
	if err != nil {
		t.Fatalf("%s: %v", what, err)
	}
	if !bytes.Equal(got, want) {
		t.Fatalf("%s: the listener did not get the uploaded bytes byte for byte (%d bytes)", what, len(want))
	}
}

func TestASealedUploadIsAcceptedAndTheListenAnswerCarriesItsContentType(t *testing.T) {
	env := sealedRelayEnv(t)
	_, id, up := startSealedBroadcast(t, env, "alice")
	writeChunk(t, up, wire.ChunkJoinable, sealedMedia(t, 1, 64))
	waitBytesIn(t, env, id, 5+4+12+64)

	l := listen(t, env, id)
	r := l.Resp
	if r.StatusCode != 200 || r.Header.Get("Content-Type") != "application/x-fmsx-sealed" {
		t.Fatalf("the listen answer carries the declared sealed type: %d %v", r.StatusCode, r.Header)
	}
	if r.Header.Get("Cache-Control") != "no-cache" || r.Header.Get("Accept-Ranges") != "none" || r.Header.Get("Icy-Metaint") != "" {
		t.Fatalf("and the other headers of 7.6 stay: %v", r.Header)
	}
	if len(r.TransferEncoding) != 1 || r.TransferEncoding[0] != "chunked" {
		t.Fatalf("%v", r.TransferEncoding)
	}
}

func TestSealedChunksGoThroughTheRingAndOutToAListenerByteForByte(t *testing.T) {
	env := sealedRelayEnv(t)
	_, id, up := startSealedBroadcast(t, env, "alice")
	l := listen(t, env, id) // connected before the first chunk

	const epoch = 7
	header := joinBytes(sealedKeyRing(t, epoch, "video/mp2t", 3), sealedMedia(t, epoch, 2*188)) // ring first, then PAT and PMT
	j1 := sealedMedia(t, epoch, 4096)
	n1 := sealedMedia(t, epoch, 1500)
	unknown := sealedRecord(recUnknown, opaque(t, 700)) // a record type the server has never heard of
	j2 := joinBytes(sealedMedia(t, epoch, 3000), sealedRecord(recUnknown, opaque(t, 30)))
	n2 := sealedMedia(t, epoch, 9)

	// the header chunk (flag 0x02) is held back until the first joinable chunk (0x01) arrives
	writeChunk(t, up, wire.ChunkHeader, header)
	writeChunk(t, up, wire.ChunkJoinable, j1)
	expectBody(t, l, "header chunk and first joinable chunk", joinBytes(header, j1))
	// a chunk with no flag is delivered in order, whatever its record type is
	writeChunk(t, up, 0, n1)
	expectBody(t, l, "a plain media chunk", n1)
	writeChunk(t, up, 0, unknown)
	expectBody(t, l, "a chunk of an unknown record type", unknown)
	writeChunk(t, up, wire.ChunkJoinable, j2)
	expectBody(t, l, "a joinable chunk of two records", j2)
	writeChunk(t, up, 0, n2)
	expectBody(t, l, "the last chunk", n2)

	if uploading, chunks, _, in, ok := env.Relay.Held(id); !ok || !uploading || chunks == 0 || in == 0 {
		t.Fatalf("the upload stays attached: %v %d %d %v", uploading, chunks, in, ok)
	}
}

func TestALateListenerOfASealedBroadcastGetsTheHeaderChunkAndJoinsAtAJoinableChunk(t *testing.T) {
	env := sealedRelayEnv(t)
	_, id, up := startSealedBroadcast(t, env, "alice")
	const epoch = 3
	header := joinBytes(sealedKeyRing(t, epoch, "audio/aac", 2), sealedMedia(t, epoch, 20))
	j1 := sealedMedia(t, epoch, 800)
	n1 := sealedMedia(t, epoch, 800)
	j2 := sealedMedia(t, epoch, 900)
	n2 := sealedMedia(t, epoch, 700)
	var total int64
	for _, c := range []struct {
		flags byte
		p     []byte
	}{{wire.ChunkHeader, header}, {wire.ChunkJoinable, j1}, {0, n1}, {wire.ChunkJoinable, j2}, {0, n2}} {
		writeChunk(t, up, c.flags, c.p)
		total += int64(len(c.p))
	}
	waitBytesIn(t, env, id, total)

	// a listener that comes late gets the header chunk, then starts at the NEWEST joinable chunk - not at a
	// plain one, and not at the first one it missed
	l := listen(t, env, id)
	if l.Resp.Header.Get("Content-Type") != wire.SealedContentType {
		t.Fatalf("%v", l.Resp.Header)
	}
	expectBody(t, l, "header chunk, newest joinable chunk, what follows it", joinBytes(header, j2, n2))
	// and then it follows the live edge
	j3 := sealedMedia(t, epoch, 600)
	writeChunk(t, up, wire.ChunkJoinable, j3)
	expectBody(t, l, "the next joinable chunk", j3)
}

func TestAReplacementUploadDeliversTheNewHeaderChunkToAListenerAlreadyConnected(t *testing.T) {
	env := sealedRelayEnv(t)
	p, id, up1 := startSealedBroadcast(t, env, "alice")

	// epoch 1
	header1 := joinBytes(sealedKeyRing(t, 1, "video/mp2t", 2), sealedMedia(t, 1, 376))
	j1 := sealedMedia(t, 1, 1000)
	writeChunk(t, up1, wire.ChunkHeader, header1)
	writeChunk(t, up1, wire.ChunkJoinable, j1)
	waitBytesIn(t, env, id, int64(len(header1)+len(j1)))
	l := listen(t, env, id)
	expectBody(t, l, "epoch 1: the header chunk and the joinable chunk", joinBytes(header1, j1))

	// the same device uploads again (item N): a new epoch opens with a new ring in a new header chunk
	up2, ans, err := p.Upload(id, wire.SealedContentType)
	if err != nil || up2 == nil {
		t.Fatalf("%v %+v", err, ans)
	}
	defer up2.Close()
	_ = up1.SetReadDeadline(time.Now().Add(3 * time.Second))
	if _, err := up1.Read(make([]byte, 8)); err == nil {
		t.Fatal("the replaced upload's stream must be closed")
	}
	header2 := joinBytes(sealedKeyRing(t, 2, "video/mp2t", 3), sealedMedia(t, 2, 376))
	plain := sealedMedia(t, 2, 500) // not joinable: skipped at the start
	j2 := sealedMedia(t, 2, 1200)
	writeChunk(t, up2, wire.ChunkHeader, header2)
	writeChunk(t, up2, 0, plain)
	writeChunk(t, up2, wire.ChunkJoinable, j2)
	expectBody(t, l, "the listener already connected: the NEW header chunk, then the new joinable chunk", joinBytes(header2, j2))
	if env.Relay.Listeners(id) != 1 {
		t.Fatal("the listener stayed connected")
	}

	// a listener that comes after the replacement gets the new header chunk and never the old one
	l2 := listen(t, env, id)
	expectBody(t, l2, "a new listener after the replacement", joinBytes(header2, j2))
}

// The server reads a chunk's length and flag byte and nothing of what follows. Payloads that are no
// records - a length that lies, a ring that claims 64 keys and holds none, a lone byte, noise, a payload
// at the bound - go through unchanged, and the upload goes on.
func TestTheRelayNeverParsesTheRecordBodies(t *testing.T) {
	env := sealedRelayEnv(t)
	_, id, up := startSealedBroadcast(t, env, "alice")
	l := listen(t, env, id)

	lying := append([]byte{0xFF, 0xFF, 0xFF, 0xFF, recMedia}, opaque(t, 40)...)                          // len far beyond the payload
	zeroLen := append([]byte{0, 0, 0, 0, recMedia}, opaque(t, 16)...)                                    // len 0, then bytes
	hollowRing := sealedRecord(recKeyRing, []byte{0, 0, 0, 9, 9, 'v', 'i', 'd', 'e', 'o', '/', 'x', 64}) // count 64, no entries, a short media type
	oneByte := []byte{0x4D}
	noise := opaque(t, 100_000)
	bound := opaque(t, wire.MaxChunkLen-1) // the length field counts the flag byte: 1048575 payload bytes at most

	writeChunk(t, up, wire.ChunkHeader, lying) // a header chunk that is no record
	writeChunk(t, up, wire.ChunkJoinable, hollowRing)
	expectBody(t, l, "a header chunk and a ring that are no records", joinBytes(lying, hollowRing))
	for _, c := range []struct {
		what  string
		flags byte
		p     []byte
	}{
		{"a record with a zero length", 0, zeroLen},
		{"a one byte payload", wire.ChunkJoinable, oneByte},
		{"noise", 0, noise},
		{"a payload at the 1 MiB bound", wire.ChunkJoinable, bound},
		{"a record after the bound", 0, sealedMedia(t, 1, 10)},
	} {
		writeChunk(t, up, c.flags, c.p)
		expectBody(t, l, c.what, c.p)
	}
	if uploading, _, _, _, ok := env.Relay.Held(id); !ok || !uploading {
		t.Fatal("a payload the server cannot read is no reason to drop the upload")
	}
}

// A broadcast tunnel carries the TLS the producer runs inside the server's TLS (item DM): to the server
// it is bytes. Random bytes that open like TLS records, in both directions, arrive unchanged.
func TestASealedBroadcastTunnelCarriesOpaqueBytesUnchanged(t *testing.T) {
	env := testkit.StartServer(t, func() testkit.Options { o := looseOpts("alice"); o.Sealed = true; return o }())
	prod := env.NewProducer("alice", sshPW)
	kem := e2eDevice(t, prod.Device)

	tlsLike := func(n int) []byte { // a handshake record header, then noise
		return append([]byte{0x16, 0x03, 0x03, byte(n >> 8), byte(n)}, opaque(t, n)...)
	}
	fromProducer, fromConsumer := tlsLike(300_000), tlsLike(200_000)
	atProducer := make(chan []byte, 1)
	release := make(chan struct{})
	defer close(release)
	prod.Handler = func(c net.Conn) {
		defer c.Close()
		go func() { _, _ = c.Write(fromProducer) }()
		got := make([]byte, len(fromConsumer))
		if _, err := io.ReadFull(c, got); err != nil {
			atProducer <- nil
			return
		}
		atProducer <- got
		<-release
	}
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	defer prod.Close()
	bid := env.NewID()
	box := newTestBox(t, prod.ID, kem.KEM, 160, prod.ID)
	r, err := prod.Control().Request(&wire.BroadcastStart{Broadcast: sealedBroadcastJSON(bid, "Tunnel", box)},
		3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil || r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%v %+v", err, r)
	}

	c, ans, err := env.OutsiderConnect(&wire.Connect{BroadcastID: bid, Scheme: "http"})
	if err != nil || c == nil {
		t.Fatalf("%v %+v", err, ans)
	}
	defer c.Close()
	go func() { _, _ = c.Conn.Write(fromConsumer) }()
	_ = c.SetReadDeadline(time.Now().Add(20 * time.Second))
	got := make([]byte, len(fromProducer))
	if _, err := io.ReadFull(c.Conn, got); err != nil {
		t.Fatalf("the consumer did not get the producer's bytes: %v", err)
	}
	if !bytes.Equal(got, fromProducer) {
		t.Fatal("the splice changed the bytes on their way to the consumer")
	}
	select {
	case back := <-atProducer:
		if !bytes.Equal(back, fromConsumer) {
			t.Fatal("the splice changed the bytes on their way to the producer")
		}
	case <-time.After(20 * time.Second):
		t.Fatal("the producer got nothing")
	}
}
