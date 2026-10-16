package clipboard

import (
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"strings"
	"sync"
	"testing"

	"fms/exchange-server/internal/wire"
)

// Ticket 234, item DN of DEVICE-EXCHANGE 27: the slot holds a `sealed` value as it holds any other. The
// bytes are ciphertext the table never looks at, the box is one opaque JSON object it keeps as it came,
// and the order of item BX, the counts of item BZ and the erasures of item BU are about the sealed bytes.

// sealedTestBox is a box in the compact form a sender writes; the table reads none of it.
const sealedTestBox = `{"v":1,"from":"dev-phone","fromKey":"BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw","n":"BC2hdMS2v4zmsfK-","ct":"6YkXLfZy1UuSVEcgoZBshhxhf_t4lNg-uCTbfefMg2M6NhJeMmzRRuuZvnmgRpWL2VUHbphySPhXvV-PUNgQTS4Qwq7AaOLXFpMoFGW4EqAOsW4c","keys":[{"to":"dev-tablet","enc":"BGaIjZ4FjWNcq-clSbHiM3Ruan93aXH51o0iq4BeNOvsfcpqPTvbhO0iuCV3jOw8dFHL6vMUzCVukZn5PPfzXZA","wk":"6mTnH4Hg63mQR--UVIfxNX3m29c574IYNK01g1Tx_CHNjcHyd4iuRbRz_TQ2_sfR"}]}`

func opaqueBytes(t testing.TB, n int) []byte {
	t.Helper()
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		t.Fatal(err)
	}
	return b
}

func sumHex(b []byte) string {
	s := sha256.Sum256(b)
	return hex.EncodeToString(s[:])
}

// writeInPieces feeds data to the put the way a stream does: reads of any size, the odd ones included.
func writeInPieces(p *Put, data []byte) {
	sizes := []int{1, 7, 4096, 32768, 3, 65536}
	for i := 0; len(data) > 0; i++ {
		n := sizes[i%len(sizes)]
		if n > len(data) {
			n = len(data)
		}
		p.Write(data[:n])
		data = data[n:]
	}
}

func sealedDecl(box string, size int) Decl {
	return Decl{Kind: wire.ClipKindSealed, Size: int64(size), Sealed: []byte(box)}
}

func putSealed(t *testing.T, tb *Table, account, device, box string, data []byte) *Value {
	t.Helper()
	p, err := tb.BeginPut(account, sealedDecl(box, len(data)), device, "Test device")
	if err != nil {
		t.Fatalf("begin sealed put: %v", err)
	}
	writeInPieces(p, data)
	v := p.Complete()
	if v == nil {
		t.Fatal("complete returned no value")
	}
	return v
}

func TestASealedValueKeepsItsBoxAndItsBytesAsTheyCame(t *testing.T) {
	tb := testTable()
	data := opaqueBytes(t, 3*65536+16+5)
	v := putSealed(t, tb, "alice", "dev-1", sealedTestBox, data)
	if got := tb.Get("alice"); got != v {
		t.Fatal("the sealed value is not the slot's value")
	}
	if v.Kind != wire.ClipKindSealed || string(v.Sealed) != sealedTestBox {
		t.Fatalf("the kind or the box changed: %q %q", v.Kind, v.Sealed)
	}
	if v.MimeType != "" || v.Name != "" {
		t.Fatalf("a sealed value shows a mimeType or a name: %+v", v)
	}
	if v.Size != int64(len(data)) || !bytes.Equal(v.Bytes(), data) {
		t.Fatalf("the sealed bytes are not stored whole: size %d of %d", v.Size, len(data))
	}
	if v.SHA256 != sumHex(data) {
		t.Fatalf("sha256 %q is not over the sealed bytes", v.SHA256)
	}
	if !wire.ValidID(v.ClipID) || v.SourceDeviceID != "dev-1" || v.SourceDeviceName != "Test device" {
		t.Fatalf("the members the server sets are missing: %+v", v)
	}
}

// The caller's frame buffer is reused by the stream after BeginPut returns: the table keeps its own copy.
func TestTheBoxIsCopiedWhenTheUploadBegins(t *testing.T) {
	tb := testTable()
	frame := []byte(sealedTestBox)
	p, err := tb.BeginPut("alice", Decl{Kind: wire.ClipKindSealed, Size: 4, Sealed: frame}, "dev-1", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	for i := range frame {
		frame[i] = 'X' // the next frame lands in the same buffer
	}
	p.Write([]byte{1, 2, 3, 4})
	v := p.Complete()
	if v == nil || string(v.Sealed) != sealedTestBox {
		t.Fatalf("the box followed the caller's buffer: %q", v.Sealed)
	}
}

// The server never inspects a value (item BW): whatever the sealed bytes look like, they are stored and
// delivered byte for byte, and the hash is over exactly them.
func TestWhatTheSealedBytesLookLikeChangesNothing(t *testing.T) {
	frame, err := wire.Frame(&wire.ClipboardEmpty{})
	if err != nil {
		t.Fatal(err)
	}
	patterns := map[string][]byte{
		"zeros":                   make([]byte, 5000),
		"ones":                    bytes.Repeat([]byte{0xff}, 5000),
		"random":                  opaqueBytes(t, 70000),
		"one byte":                {0x00},
		"a json object":           []byte(`{"kind":"text","size":3,"sealed":{}}`),
		"the box itself":          []byte(sealedTestBox),
		"a png signature":         append([]byte("\x89PNG\r\n\x1a\n"), opaqueBytes(t, 100)...),
		"a zip header":            append([]byte("PK\x03\x04"), opaqueBytes(t, 100)...),
		"a gzip header":           append([]byte{0x1f, 0x8b, 0x08}, opaqueBytes(t, 100)...),
		"a utf-16 text":           []byte("\xff\xfeh\x00i\x00"),
		"invalid utf-8":           {0xc3, 0x28, 0xa0, 0xa1, 0xe2, 0x28, 0xa1},
		"a whole wire frame":      frame,
		"a clipboard-put as text": []byte(`{"schemaVersion":2,"type":"clipboard-put","kind":"text","size":9}`),
	}
	tb := testTable()
	for name, data := range patterns {
		v := putSealed(t, tb, "alice", "dev-1", sealedTestBox, data)
		if !bytes.Equal(v.Bytes(), data) || v.Size != int64(len(data)) || v.SHA256 != sumHex(data) {
			t.Errorf("%s: the sealed bytes were not stored as they came", name)
		}
		if v.Kind != wire.ClipKindSealed {
			t.Errorf("%s: the kind was decided by the bytes: %q", name, v.Kind)
		}
	}
}

func TestASealedValueTakesPartInTheOrderOfItemBX(t *testing.T) {
	tb := testTable()
	first := put(t, tb, "alice", "dev-1", []byte("a text"))
	sealed := putSealed(t, tb, "alice", "dev-2", sealedTestBox, opaqueBytes(t, 1016))
	if tb.Get("alice") != sealed || sealed.ClipID == first.ClipID {
		t.Fatal("a complete sealed put does not replace a text")
	}

	// a plain put replaces the sealed one and shows nothing of its box
	p, err := tb.BeginPut("alice", Decl{Kind: "image", Size: 3, MimeType: "image/png", Name: "shot"}, "dev-3", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	p.Write([]byte{9, 8, 7})
	img := p.Complete()
	if tb.Get("alice") != img || img.Kind != "image" || len(img.Sealed) != 0 || img.MimeType != "image/png" {
		t.Fatalf("an image after a sealed value carries the box of the value before it: %+v", img)
	}

	// a sealed put replaces a plain one and shows none of its members
	again := putSealed(t, tb, "alice", "dev-2", `{"v":1,"n":"second"}`, opaqueBytes(t, 33))
	if tb.Get("alice") != again || again.MimeType != "" || again.Name != "" || string(again.Sealed) != `{"v":1,"n":"second"}` {
		t.Fatalf("a sealed value after an image carries the members of the image: %+v", again)
	}

	// the values replaced are whole for the readers that hold them
	if string(sealed.Sealed) != sealedTestBox || len(sealed.Bytes()) != 1016 || sealed.Kind != wire.ClipKindSealed {
		t.Fatalf("a replaced sealed value was changed: %+v", sealed)
	}
	if string(first.Bytes()) != "a text" || len(first.Sealed) != 0 {
		t.Fatalf("a replaced text was changed: %+v", first)
	}
	if tb.GetByID("alice", sealed.ClipID) != nil || tb.GetByID("alice", again.ClipID) != again {
		t.Fatal("only the latest value answers to its clipId")
	}
}

func TestABrokenSealedUploadChangesNothing(t *testing.T) {
	tb := testTable()
	// on an empty clipboard
	p, err := tb.BeginPut("alice", sealedDecl(sealedTestBox, 100), "dev-1", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	p.Write(opaqueBytes(t, 40))
	if values, held := tb.Counts(); values != 0 || held != 40 {
		t.Fatalf("an upload in flight: %d values, %d bytes held", values, held)
	}
	p.Drop()
	if tb.Get("alice") != nil {
		t.Fatal("a broken sealed upload filled an empty clipboard")
	}
	if values, held := tb.Counts(); values != 0 || held != 0 {
		t.Fatalf("a dropped upload still holds %d values, %d bytes", values, held)
	}

	// over a text, and over a sealed value
	text := put(t, tb, "alice", "dev-1", []byte("kept text"))
	p, _ = tb.BeginPut("alice", sealedDecl(sealedTestBox, 100), "dev-2", "Test device")
	p.Write(opaqueBytes(t, 99))
	p.Drop()
	if tb.Get("alice") != text {
		t.Fatal("a broken sealed upload replaced a text")
	}
	data := opaqueBytes(t, 64)
	sealed := putSealed(t, tb, "alice", "dev-2", sealedTestBox, data)
	p, _ = tb.BeginPut("alice", sealedDecl(`{"v":1,"n":"never-stored"}`, 100), "dev-3", "Test device")
	p.Write(opaqueBytes(t, 10))
	p.Drop()
	got := tb.Get("alice")
	if got != sealed || string(got.Sealed) != sealedTestBox || !bytes.Equal(got.Bytes(), data) {
		t.Fatalf("a broken sealed upload changed the sealed value: %+v", got)
	}
	// and a plain one that breaks over a sealed value
	p, _ = tb.BeginPut("alice", Decl{Kind: "text", Size: 50}, "dev-3", "Test device")
	p.Write([]byte("half"))
	p.Drop()
	if tb.Get("alice") != sealed {
		t.Fatal("a broken text upload replaced a sealed value")
	}
}

func TestASecondSealedUploadInFlightIsRefusedAndTheFirstCompletes(t *testing.T) {
	tb := testTable()
	first, err := tb.BeginPut("alice", sealedDecl(sealedTestBox, 8), "dev-1", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	if _, err := tb.BeginPut("alice", sealedDecl(sealedTestBox, 8), "dev-2", "Test device"); !errors.Is(err, ErrPutInFlight) {
		t.Fatalf("a second sealed upload: %v", err)
	}
	if _, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 4}, "dev-2", "Test device"); !errors.Is(err, ErrPutInFlight) {
		t.Fatalf("a text upload beside a sealed one: %v", err)
	}
	first.Write(opaqueBytes(t, 8))
	if v := first.Complete(); v == nil || tb.Get("alice") != v {
		t.Fatal("the first sealed upload did not complete")
	}
}

// Counts (the bytes behind item BZ and the operator's totals) are about the sealed bytes: the box is
// a member of the record, not part of the value.
func TestTheCountsAreTheSealedBytesNotTheBox(t *testing.T) {
	tb := testTable()
	bigBox := `{"v":1,"pad":"` + strings.Repeat("a", 12000) + `"}`
	v := putSealed(t, tb, "alice", "dev-1", bigBox, opaqueBytes(t, 1016))
	if v.Size != 1016 {
		t.Fatalf("size %d is not the sealed byte count", v.Size)
	}
	if values, held := tb.Counts(); values != 1 || held != 1016 {
		t.Fatalf("%d values, %d bytes held: the box is counted, or the bytes are not", values, held)
	}
	p, _ := tb.BeginPut("bob", sealedDecl(bigBox, 500), "dev-2", "Test device")
	p.Write(opaqueBytes(t, 200))
	if values, held := tb.Counts(); values != 1 || held != 1016+200 {
		t.Fatalf("with an upload in flight: %d values, %d bytes held", values, held)
	}
	p.Drop()
}

func TestAnErasureTakesASealedValueAndItsBoxWithIt(t *testing.T) {
	tb := testTable()
	held := putSealed(t, tb, "alice", "dev-1", sealedTestBox, opaqueBytes(t, 64))
	running, err := tb.BeginPut("alice", sealedDecl(sealedTestBox, 10), "dev-1", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	running.Write(opaqueBytes(t, 5))
	tb.Erase("alice")
	if tb.Get("alice") != nil || tb.GetByID("alice", held.ClipID) != nil {
		t.Fatal("an erased account still holds its sealed value")
	}
	if values, bytesHeld := tb.Counts(); values != 0 || bytesHeld != 0 {
		t.Fatalf("an erased account holds %d values, %d bytes", values, bytesHeld)
	}
	running.Write(opaqueBytes(t, 5))
	if running.Complete() != nil || tb.Get("alice") != nil {
		t.Fatal("a sealed upload that ran during the erasure was stored over it")
	}
	// the reader that began before keeps its own reference (item BY)
	if string(held.Sealed) != sealedTestBox || len(held.Bytes()) != 64 {
		t.Fatalf("a reader's value was changed by the erasure: %+v", held)
	}
}

// -race: sealed and plain puts and gets over accounts and over one shared account. The box of every
// sealed value names the digest of its bytes, so a reader that sees a box beside someone else's bytes
// (a torn replacement) fails here.
func TestConcurrentSealedPutsAndGetsAreWhole(t *testing.T) {
	tb := testTable()
	var wg sync.WaitGroup
	check := func(v *Value) error {
		if v == nil || v.Kind != wire.ClipKindSealed {
			return nil
		}
		if sumHex(v.Bytes()) != v.SHA256 || v.Size != int64(len(v.Bytes())) {
			return fmt.Errorf("the bytes of %s are not its digest", v.ClipID)
		}
		if want := `{"v":1,"d":"` + v.SHA256 + `"}`; string(v.Sealed) != want {
			return fmt.Errorf("the box %q is not the box of the bytes %s", v.Sealed, v.SHA256)
		}
		if v.MimeType != "" || v.Name != "" {
			return errors.New("a sealed value shows a mimeType or a name")
		}
		return nil
	}
	for w := 0; w < 6; w++ {
		w := w
		account := fmt.Sprintf("acct-%d", w%2) // three writers per account
		wg.Add(2)
		go func() {
			defer wg.Done()
			for round := 0; round < 30; round++ {
				data := bytes.Repeat([]byte{byte(w)}, 100+round*w)
				var d Decl
				if round%3 == 0 {
					d = Decl{Kind: "text", Size: int64(len(data))}
				} else {
					d = Decl{Kind: wire.ClipKindSealed, Size: int64(len(data)), Sealed: []byte(`{"v":1,"d":"` + sumHex(data) + `"}`)}
				}
				p, err := tb.BeginPut(account, d, "dev", "Test device")
				if err != nil { // the account's one upload in flight is somebody else's
					continue
				}
				writeInPieces(p, data)
				if round%7 == 6 {
					p.Drop()
				} else {
					p.Complete()
				}
			}
		}()
		go func() {
			defer wg.Done()
			for round := 0; round < 200; round++ {
				if err := check(tb.Get(account)); err != nil {
					t.Error(err)
					return
				}
			}
		}()
	}
	wg.Wait()
}
