package relay

import (
	"fmt"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/wire"
)

// These are the window rules of ticket 165 (rules 3, 4 and 7), proved with an injected clock and no
// sleeps: nothing here depends on how fast the machine is.

type clock struct{ t time.Time }

func newClock() *clock { return &clock{t: time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)} }

func (c *clock) Now() time.Time          { return c.t }
func (c *clock) Advance(d time.Duration) { c.t = c.t.Add(d) }

func chunk(flags byte, s string) wire.Chunk { return wire.Chunk{Flags: flags, Payload: []byte(s)} }

func join(out [][]byte) string {
	var parts []string
	for _, p := range out {
		parts = append(parts, string(p))
	}
	return strings.Join(parts, ",")
}

func take(t *testing.T, l *Listener) string {
	t.Helper()
	out, _, ended := l.Take()
	if ended {
		t.Fatal("unexpected end")
	}
	return join(out)
}

func TestANewListenerReceivesTheHeaderThenStartsAtTheNewestJoinableChunk(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now})
	r.Append(chunk(wire.ChunkHeader, "HDR"))
	r.Append(chunk(wire.ChunkJoinable, "j1"))
	r.Append(chunk(0, "n2"))
	c.Advance(100 * time.Millisecond)
	r.Append(chunk(wire.ChunkJoinable, "j3"))
	r.Append(chunk(0, "n4"))

	l := r.NewListener()
	if got := take(t, l); got != "HDR,j3,n4" {
		t.Fatalf("a joining listener gets the header and then starts only at a joinable chunk: %q", got)
	}
	r.Append(chunk(0, "n5"))
	if got := take(t, l); got != "n5" {
		t.Fatalf("then it follows by cursor: %q", got)
	}
}

func TestAListenerWithNoJoinableChunkYetWaitsForTheNextOne(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now})
	r.Append(chunk(wire.ChunkHeader, "HDR"))
	r.Append(chunk(0, "n1"))
	r.Append(chunk(0, "n2"))
	l := r.NewListener()
	out, wait, ended := l.Take()
	if len(out) != 0 || ended || wait == nil {
		t.Fatalf("nothing joinable is in the ring: %v %v", out, ended)
	}
	r.Append(chunk(0, "n3"))
	if out, _, _ := l.Take(); len(out) != 0 {
		t.Fatalf("a non-joinable chunk is not a start: %q", join(out))
	}
	select {
	case <-wait:
	default:
		t.Fatal("an append must wake a waiting listener")
	}
	r.Append(chunk(wire.ChunkJoinable, "j4"))
	r.Append(chunk(0, "n5"))
	if got := take(t, l); got != "HDR,j4,n5" {
		t.Fatalf("%q", got)
	}
}

func TestAHeaderIsReplacedNotReplayedMidStream(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now})
	r.Append(chunk(wire.ChunkHeader, "H1"))
	r.Append(chunk(wire.ChunkJoinable, "a"))
	l1 := r.NewListener()
	if got := take(t, l1); got != "H1,a" {
		t.Fatalf("%q", got)
	}
	r.Append(chunk(wire.ChunkHeader, "H2")) // a new header replaces the old one
	r.Append(chunk(wire.ChunkJoinable, "b"))
	if got := take(t, l1); got != "b" {
		t.Fatalf("a listener already past the start must not get the header again: %q", got)
	}
	l2 := r.NewListener()
	if got := take(t, l2); got != "H2,b" {
		t.Fatalf("a new listener gets the CURRENT header only: %q", got)
	}
}

// a listener started at second 0 and one stalled for 3 s both end up at most one window behind, and
// the fast one is never delayed by the slow one
func TestAStalledListenerSkipsToTheOldestJoinableChunkInTheWindow(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now, Window: 2 * time.Second})
	fast, slow := r.NewListener(), r.NewListener()
	var fastGot []string
	push := func(i int) {
		r.Append(chunk(wire.ChunkJoinable, fmt.Sprintf("c%02d", i)))
		out, _, _ := fast.Take()
		for _, p := range out {
			fastGot = append(fastGot, string(p))
		}
	}
	// one chunk every 100 ms for 3 s; the slow listener takes its first and then stalls
	push(0)
	if got := take(t, slow); got != "c00" {
		t.Fatalf("%q", got)
	}
	for i := 1; i < 30; i++ {
		c.Advance(100 * time.Millisecond)
		push(i)
	}
	// the fast one saw every chunk, in order, with no gap: the slow one cost it nothing
	if len(fastGot) != 30 {
		t.Fatalf("the fast listener saw %d of 30 chunks", len(fastGot))
	}
	for i, g := range fastGot {
		if g != fmt.Sprintf("c%02d", i) {
			t.Fatalf("the fast listener's chunk %d is %s", i, g)
		}
	}
	// the slow one wakes up 2.9 s later: it skips to the oldest joinable chunk still held
	out, _, _ := slow.Take()
	if len(out) == 0 {
		t.Fatal("a stalled listener must resume, not stall")
	}
	first := string(out[0])
	var firstIdx int
	fmt.Sscanf(first, "c%d", &firstIdx)
	behind := time.Duration(29-firstIdx) * 100 * time.Millisecond
	if behind > 2*time.Second {
		t.Fatalf("the resumed listener is %v behind the live edge, the window is 2s (it resumed at %s)", behind, first)
	}
	if firstIdx <= 1 {
		t.Fatalf("the chunks it missed must be dropped, not replayed: it resumed at %s", first)
	}
	for i := 1; i < len(out); i++ {
		if string(out[i]) != fmt.Sprintf("c%02d", firstIdx+i) {
			t.Fatalf("after the skip the listener reads in order: %s", join(out))
		}
	}
}

func TestAResyncedListenerDoesNotGetTheHeaderAgainAndWaitsForAJoinableChunk(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now, Window: time.Second})
	r.Append(chunk(wire.ChunkHeader, "H"))
	r.Append(chunk(wire.ChunkJoinable, "j0"))
	l := r.NewListener()
	if got := take(t, l); got != "H,j0" {
		t.Fatalf("%q", got)
	}
	// it stalls; only non-joinable chunks arrive for longer than the window
	for i := 1; i <= 15; i++ {
		c.Advance(200 * time.Millisecond)
		r.Append(chunk(0, fmt.Sprintf("n%d", i)))
	}
	if out, wait, _ := l.Take(); len(out) != 0 || wait == nil {
		t.Fatalf("it fell behind and nothing joinable is held: it waits for the next joinable chunk, got %q", join(out))
	}
	r.Append(chunk(wire.ChunkJoinable, "j16"))
	r.Append(chunk(0, "n17"))
	if got := take(t, l); got != "j16,n17" {
		t.Fatalf("a listener past the start is not given the header again: %q", got)
	}
}

func TestTheByteCapEvictsTheOldestFirst(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now, Window: time.Hour, MaxBytes: 100})
	for i := 0; i < 10; i++ {
		r.Append(wire.Chunk{Flags: wire.ChunkJoinable, Payload: []byte(fmt.Sprintf("%02d", i) + strings.Repeat("x", 28))}) // 30 bytes
	}
	n, bytes := r.Held()
	if bytes > 100 || n != 3 {
		t.Fatalf("the cap holds at most 100 bytes: %d chunks, %d bytes", n, bytes)
	}
	l := r.NewListener()
	out, _, _ := l.Take()
	if len(out) != 1 || string(out[0][:2]) != "09" {
		t.Fatalf("a new listener starts at the newest joinable chunk: %v", join(out))
	}
	// a listener that was caught up before the burst resumes at the oldest chunk still held
	r2 := NewRing(Config{Now: c.Now, Window: time.Hour, MaxBytes: 100})
	l2 := r2.NewListener()
	r2.Append(wire.Chunk{Flags: wire.ChunkJoinable, Payload: []byte("first")})
	if got := take(t, l2); got != "first" {
		t.Fatal(got)
	}
	for i := 0; i < 10; i++ {
		r2.Append(wire.Chunk{Flags: wire.ChunkJoinable, Payload: []byte(fmt.Sprintf("%02d", i) + strings.Repeat("x", 28))})
	}
	out, _, _ = l2.Take()
	if len(out) != 3 || string(out[0][:2]) != "07" {
		t.Fatalf("it must resume at the oldest held chunk, 07: %d chunks, first %q", len(out), out[0][:2])
	}
}

func TestTheWindowEvictsByTimeEvenWithoutNewChunks(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now, Window: 2 * time.Second})
	r.Append(chunk(wire.ChunkJoinable, "old"))
	c.Advance(3 * time.Second)
	if n, _ := r.Held(); n != 0 {
		t.Fatalf("a chunk older than the window is gone: %d", n)
	}
	l := r.NewListener()
	if out, wait, _ := l.Take(); len(out) != 0 || wait == nil {
		t.Fatalf("a listener must not start at a chunk older than the window: %q", join(out))
	}
}

func TestARestartContinuesEveryListenerAfterTheNewHeader(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now})
	r.Append(chunk(wire.ChunkHeader, "H1"))
	r.Append(chunk(wire.ChunkJoinable, "a1"))
	l := r.NewListener()
	if got := take(t, l); got != "H1,a1" {
		t.Fatal(got)
	}
	r.Restart() // a second upload replaces the first
	if out, wait, _ := l.Take(); len(out) != 0 || wait == nil {
		t.Fatalf("after a replacement the listener waits for the new upload: %q", join(out))
	}
	r.Append(chunk(wire.ChunkHeader, "H2"))
	r.Append(chunk(0, "n0"))
	r.Append(chunk(wire.ChunkJoinable, "b1"))
	r.Append(chunk(0, "b2"))
	if got := take(t, l); got != "H2,b1,b2" {
		t.Fatalf("the listener continues from the next joinable chunk of the new upload, after its header: %q", got)
	}
}

func TestEndGivesEveryListenerWhatItHasThenTheEnd(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now})
	l := r.NewListener()
	r.Append(chunk(wire.ChunkJoinable, "last"))
	r.End()
	out, _, ended := l.Take()
	if ended || join(out) != "last" {
		t.Fatalf("a listener is handed what it had not read before the end: %v %v", join(out), ended)
	}
	if _, _, ended := l.Take(); !ended {
		t.Fatal("then its body ends")
	}
	r.Append(chunk(wire.ChunkJoinable, "late")) // nothing is added after the end
	if n, _ := r.Held(); n != 1 {
		t.Fatalf("%d", n)
	}
}

func TestAppendNeverBlocksWhateverTheListenersDo(t *testing.T) {
	r := NewRing(Config{})
	for i := 0; i < 50; i++ {
		r.NewListener() // 50 listeners that never take
	}
	done := make(chan struct{})
	go func() {
		for i := 0; i < 20000; i++ {
			r.Append(wire.Chunk{Flags: wire.ChunkJoinable, Payload: make([]byte, 512)})
		}
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		t.Fatal("an upload that appends must never wait for a listener")
	}
	if _, b := r.Held(); b > DefaultRingBytes {
		t.Fatalf("the ring holds %d bytes", b)
	}
}

func TestMemoryDoesNotGrowWithTheNumberOfListeners(t *testing.T) {
	c := newClock()
	r := NewRing(Config{Now: c.Now, MaxBytes: 1 << 20})
	for i := 0; i < 200; i++ {
		r.NewListener()
	}
	for i := 0; i < 5000; i++ {
		c.Advance(time.Millisecond)
		r.Append(wire.Chunk{Flags: wire.ChunkJoinable, Payload: make([]byte, 1024)})
	}
	if _, b := r.Held(); b > 1<<20 {
		t.Fatalf("the ring is bounded whatever the listeners do: %d", b)
	}
	if r.BytesIn() != 5000*1024 {
		t.Fatalf("%d", r.BytesIn())
	}
}

func TestRateGuardFlagsOnlyASustainedRate(t *testing.T) {
	c := newClock()
	g := RateGuard{MaxBitsPerSec: 8_000_000, Window: 5 * time.Second} // 1 MB/s
	// 5 s at 0.9 MB/s: fine
	for i := 0; i < 50; i++ {
		if g.Add(90_000, c.Now()) {
			t.Fatalf("flagged under the ceiling at step %d", i)
		}
		c.Advance(100 * time.Millisecond)
	}
	if g.Add(1, c.Now()) {
		t.Fatal("the window ended under the ceiling")
	}
	// a burst followed by quiet is not sustained
	g = RateGuard{MaxBitsPerSec: 8_000_000, Window: 5 * time.Second}
	g.Add(4_000_000, c.Now()) // 4 MB at once: 0.8 MB/s over 5 s
	c.Advance(5 * time.Second)
	if g.Add(1, c.Now()) {
		t.Fatal("a burst that averages under the ceiling over the window is not sustained")
	}
	// 5 s at 1.5 MB/s: flagged when the window ends
	g = RateGuard{MaxBitsPerSec: 8_000_000, Window: 5 * time.Second}
	flagged := false
	for i := 0; i < 52 && !flagged; i++ {
		flagged = g.Add(150_000, c.Now())
		c.Advance(100 * time.Millisecond)
	}
	if !flagged {
		t.Fatal("a stream above the ceiling for a whole window must be flagged")
	}
	// disabled
	if (&RateGuard{}).Add(1<<30, c.Now()) {
		t.Fatal("a zero ceiling disables the guard")
	}
}
