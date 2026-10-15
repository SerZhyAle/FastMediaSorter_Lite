package clipboard

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"io"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/wire"
)

// Ticket 229: the slot table's own rules - replace on complete, an interrupted put changes
// nothing, one upload in flight per account, memory accounting, the erasures of 224 - and the
// concurrency the -race step of the suite exercises.

func testTable() *Table {
	t0 := time.Date(2026, 10, 15, 12, 0, 0, 0, time.UTC)
	return NewTable(func() time.Time { return t0 }, &deterministic{})
}

// deterministic is a stateful byte source: every draw differs, so the clipIds of a test are
// stable AND distinct.
type deterministic struct{ n uint64 }

func (d *deterministic) Read(p []byte) (int, error) {
	for i := range p {
		p[i] = byte(d.n + uint64(i))
	}
	d.n++
	return len(p), nil
}

func put(t *testing.T, tb *Table, account, device string, data []byte) *Value {
	t.Helper()
	p, err := tb.BeginPut(account, Decl{Kind: "text", Size: int64(len(data))}, device, "Test device")
	if err != nil {
		t.Fatalf("begin put: %v", err)
	}
	p.Write(data)
	v := p.Complete()
	if v == nil {
		t.Fatal("complete returned no value")
	}
	return v
}

func TestACompletePutReplacesTheValue(t *testing.T) {
	tb := testTable()
	if v := tb.Get("alice"); v != nil {
		t.Fatalf("a fresh table is not empty: %+v", v)
	}
	first := put(t, tb, "alice", "dev-1", []byte("first"))
	second := put(t, tb, "alice", "dev-2", []byte("second"))
	if got := tb.Get("alice"); got != second {
		t.Fatal("the later complete put is not the value")
	}
	if second.ClipID == first.ClipID {
		t.Fatal("a stored value has a clipId new for every stored value")
	}
	if !wire.ValidID(second.ClipID) {
		t.Fatalf("clipId %q is not 16 bytes in the share id form", second.ClipID)
	}
	if second.SourceDeviceID != "dev-2" || second.SourceDeviceName != "Test device" {
		t.Fatalf("the source device was not kept: %+v", second)
	}
	if second.StoredAt != wire.FormatTime(time.Date(2026, 10, 15, 12, 0, 0, 0, time.UTC)) {
		t.Fatalf("storedAt is not the server's clock: %q", second.StoredAt)
	}
	sum := sha256.Sum256([]byte("second"))
	if second.SHA256 != hex.EncodeToString(sum[:]) {
		t.Fatalf("sha256 is not over the bytes: %q", second.SHA256)
	}
	if !bytes.Equal(second.Bytes(), []byte("second")) {
		t.Fatal("the bytes are not the value's")
	}
}

func TestAnInterruptedPutChangesNothing(t *testing.T) {
	tb := testTable()
	put(t, tb, "alice", "dev-1", []byte("kept"))
	p, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 10}, "dev-2", "Test device")
	if err != nil {
		t.Fatal(err)
	}
	p.Write([]byte("half"))
	p.Drop()
	if got := tb.Get("alice"); got == nil || !bytes.Equal(got.Bytes(), []byte("kept")) {
		t.Fatalf("an interrupted put must leave the previous value: %+v", got)
	}
	// the abandoned upload released its slot: a next put is taken
	if _, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 4}, "dev-2", "T"); err != nil {
		t.Fatalf("a put after a dropped one was refused: %v", err)
	}
}

func TestASecondOverlappingPutIsCapacityAndTheLaterCompletionWins(t *testing.T) {
	tb := testTable()
	put(t, tb, "alice", "dev-1", []byte("kept"))
	first, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 5}, "dev-2", "T")
	if err != nil {
		t.Fatal(err)
	}
	// the account's one upload in flight (ticket 229 T-1): an overlapping send is refused, and the
	// old value stays (item BX; item BZ's word for it is capacity)
	if _, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 5}, "dev-3", "T"); !errors.Is(err, ErrPutInFlight) {
		t.Fatalf("a second overlapping put must be ErrPutInFlight, got %v", err)
	}
	first.Write([]byte("latr!"))
	if v := first.Complete(); v == nil {
		t.Fatal("complete returned no value")
	}
	if got := tb.Get("alice"); got == nil || !bytes.Equal(got.Bytes(), []byte("latr!")) {
		t.Fatalf("the completion that landed later is not the value: %+v", got)
	}
	// a double complete or a drop after a complete changes nothing
	if first.Complete() != nil {
		t.Fatal("a second completion stored again")
	}
	first.Drop()
	if got := tb.Get("alice"); got == nil || !bytes.Equal(got.Bytes(), []byte("latr!")) {
		t.Fatalf("a second completion changed the value: %+v", got)
	}
}

func TestAGetDuringAPutReturnsThePreviousValueWhole(t *testing.T) {
	tb := testTable()
	old := put(t, tb, "alice", "dev-1", []byte("previous"))
	p, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 6}, "dev-2", "T")
	if err != nil {
		t.Fatal(err)
	}
	p.Write([]byte("new-"))
	if got := tb.Get("alice"); got != old {
		t.Fatal("a get during a put must return the previous value")
	}
	p.Write([]byte("value"))
	p.Complete()
	if got := tb.Get("alice"); !bytes.Equal(got.Bytes(), []byte("new-value")) {
		t.Fatalf("the completed put is not the value: %q", got.Bytes())
	}
	// the reader that began before the replacement still holds its own bytes whole
	if !bytes.Equal(old.Bytes(), []byte("previous")) {
		t.Fatalf("a value replaced mid-read was not delivered whole: %q", old.Bytes())
	}
}

func TestCounts(t *testing.T) {
	tb := testTable()
	values, bytes := tb.Counts()
	if values != 0 || bytes != 0 {
		t.Fatalf("a fresh table holds %d values, %d bytes", values, bytes)
	}
	put(t, tb, "alice", "d1", []byte("12345"))
	put(t, tb, "bob", "d2", []byte("1234567"))
	p, _ := tb.BeginPut("carol", Decl{Kind: "text", Size: 100}, "d3", "T")
	p.Write([]byte("12"))
	values, bytes = tb.Counts()
	if values != 2 {
		t.Fatalf("%d values, want 2", values)
	}
	if bytes != 5+7+2 {
		t.Fatalf("%d bytes, want 14 (values and the upload in flight)", bytes)
	}
	p.Drop()
	values, bytes = tb.Counts()
	if values != 2 || bytes != 12 {
		t.Fatalf("after the drop: %d values, %d bytes, want 2 and 12", values, bytes)
	}
	tb.Erase("alice")
	values, bytes = tb.Counts()
	if values != 1 || bytes != 7 {
		t.Fatalf("after the erase: %d values, %d bytes, want 1 and 7", values, bytes)
	}
}

func TestEraseWithAnUploadInFlightAndTheUnknownAccount(t *testing.T) {
	tb := testTable()
	put(t, tb, "alice", "d1", []byte("kept"))
	p, err := tb.BeginPut("bob", Decl{Kind: "text", Size: 5}, "d2", "T")
	if err != nil {
		t.Fatal(err)
	}
	p.Write([]byte("part"))
	tb.Erase("bob")
	if v := p.Complete(); v != nil {
		t.Fatal("a put that completes after its account was erased stored a value")
	}
	if tb.Get("bob") != nil {
		t.Fatal("bob's slot survived the erasure")
	}
	if got := tb.Get("alice"); got == nil || !bytes.Equal(got.Bytes(), []byte("kept")) {
		t.Fatal("the erasure of bob touched alice")
	}
	tb.Erase("nobody") // must not panic
}

func TestGetByIDAnswersOnlyTheCurrentValue(t *testing.T) {
	tb := testTable()
	first := put(t, tb, "alice", "d1", []byte("one"))
	if tb.GetByID("alice", first.ClipID) == nil {
		t.Fatal("the current clipId was refused")
	}
	if tb.GetByID("alice", "NOTANIDNOTANIDNOTAN") != nil {
		t.Fatal("an unknown clipId was answered")
	}
	second := put(t, tb, "alice", "d1", []byte("two"))
	if tb.GetByID("alice", first.ClipID) != nil {
		t.Fatal("a clipId that is no longer the value was answered")
	}
	if tb.GetByID("bob", second.ClipID) != nil {
		t.Fatal("another account's clipId was answered")
	}
}

// -race over concurrent puts and gets of several accounts (spec 229 section 4): the counters, the
// reads and the completions run together, and at the end every slot holds a whole value.
func TestConcurrentPutsAndGets(t *testing.T) {
	tb := NewTable(nil, &deterministic{})
	payload := bytes.Repeat([]byte("x"), 1<<10)
	var wg sync.WaitGroup
	for account := 0; account < 4; account++ {
		wg.Add(1)
		go func(n int) {
			defer wg.Done()
			login := string(rune('a' + n))
			for i := 0; i < 50; i++ {
				p, err := tb.BeginPut(login, Decl{Kind: "text", Size: int64(len(payload))}, "d", "T")
				if errors.Is(err, ErrPutInFlight) {
					// the table takes overlapping puts only across accounts; this loop is alone on
					// its own account, so the refusal would mean another test's leak
					t.Errorf("the account's own loop was refused capacity")
					return
				} else if err != nil {
					t.Errorf("begin put: %v", err)
					return
				}
				p.Write(payload)
				if i%3 == 0 {
					_ = tb.Get(login)
				}
				if v := p.Complete(); v == nil {
					t.Errorf("complete returned no value")
					return
				}
				if got := tb.GetByID(login, tb.Get(login).ClipID); got == nil || !bytes.Equal(got.Bytes(), payload) {
					t.Errorf("the slot does not hold the last whole value")
					return
				}
				tb.Counts()
			}
		}(account)
	}
	wg.Wait()
	for a := 0; a < 4; a++ {
		if v := tb.Get(string(rune('a' + a))); v == nil || !bytes.Equal(v.Bytes(), payload) {
			t.Fatalf("account %d does not hold a whole value", a)
		}
	}
}

// a put's buffer is released when it is dropped or replaced: the values and uploads of a table
// that was emptied hold no bytes (the memory accounting the statement's counts stand on).
func TestAnEmptiedTableHoldsNoBytes(t *testing.T) {
	tb := testTable()
	put(t, tb, "alice", "d1", bytes.Repeat([]byte("y"), 4096))
	p, err := tb.BeginPut("alice", Decl{Kind: "text", Size: 4096}, "d1", "T")
	if err != nil {
		t.Fatal(err)
	}
	p.Write(bytes.Repeat([]byte("z"), 4096))
	values, held := tb.Counts()
	if values != 1 || held != 8192 {
		t.Fatalf("mid-put: %d values, %d bytes", values, held)
	}
	tb.Erase("alice")
	if _, held := tb.Counts(); held != 0 {
		t.Fatalf("an emptied table holds %d bytes", held)
	}
	// the put that was erased mid-flight cannot store afterwards
	if v := p.Complete(); v != nil {
		t.Fatal("a put after its erasure stored")
	}
}

var _ io.Reader = &deterministic{}
