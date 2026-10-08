package relay

import (
	"sync"
	"time"

	"fms/exchange-server/internal/wire"
)

// This file is the fan-out half of the relay (DEVICE-EXCHANGE 7.6, ticket 165 rules 3 and 4): ONE
// ring per broadcast, shared by every listener, each listener with its own cursor, so memory does not
// grow with the number of listeners. It interprets no byte of a payload - only the flags of a chunk
// - and it never blocks: appending is a lock, a slice append and a wake-up, so a slow or stalled
// listener cannot delay the upload or another listener. A listener that cannot keep up DROPS: its
// cursor falls out of the window and it resumes at a joinable chunk that is still in the ring.
//
// Time is injected (Config.Now), so the window rules are tested without sleeping.

// Defaults of ticket 165 rule 3.
const (
	// DefaultWindow is how long a chunk is kept: what arrived in the last 2 s.
	DefaultWindow = 2 * time.Second
	// DefaultRingBytes is the hard cap on the chunks held, whichever of window and cap is reached first.
	DefaultRingBytes = 8 << 20
)

// Config tunes one ring.
type Config struct {
	Window   time.Duration // zero: DefaultWindow
	MaxBytes int           // zero: DefaultRingBytes
	Now      func() time.Time
}

type entry struct {
	seq      uint64
	at       time.Time
	joinable bool
	payload  []byte
}

// Ring holds the chunks of one broadcast that arrived inside the window, and its header chunk apart.
type Ring struct {
	cfg Config

	mu        sync.Mutex
	notify    chan struct{} // closed and replaced whenever something a listener may be waiting for changes
	chunks    []entry
	head      int // index of the oldest live entry
	bytes     int
	nextSeq   uint64
	header    []byte
	hasHeader bool
	epoch     uint64 // grows with every upload that replaces the previous one
	ended     bool
	in        int64 // bytes appended over the ring's life (a counter for the end-of-broadcast log line)
}

// NewRing returns an empty ring.
func NewRing(cfg Config) *Ring {
	if cfg.Window <= 0 {
		cfg.Window = DefaultWindow
	}
	if cfg.MaxBytes <= 0 {
		cfg.MaxBytes = DefaultRingBytes
	}
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	return &Ring{cfg: cfg, notify: make(chan struct{})}
}

func (r *Ring) wakeLocked() {
	close(r.notify)
	r.notify = make(chan struct{})
}

func (r *Ring) firstSeqLocked() uint64 {
	if r.head < len(r.chunks) {
		return r.chunks[r.head].seq
	}
	return r.nextSeq
}

// evictLocked drops what is older than the window and then the oldest while the byte cap is exceeded.
func (r *Ring) evictLocked() {
	now := r.cfg.Now()
	for r.head < len(r.chunks) {
		e := &r.chunks[r.head]
		if now.Sub(e.at) <= r.cfg.Window && r.bytes <= r.cfg.MaxBytes {
			break
		}
		r.bytes -= len(e.payload)
		e.payload = nil
		r.head++
	}
	if r.head > 256 && r.head*2 > len(r.chunks) {
		n := copy(r.chunks, r.chunks[r.head:])
		for i := n; i < len(r.chunks); i++ {
			r.chunks[i] = entry{}
		}
		r.chunks = r.chunks[:n]
		r.head = 0
	}
}

// Append adds one chunk of the current upload: a header chunk replaces the kept header (and is never
// part of the ring), any other becomes the newest entry. It never blocks and never fails.
func (r *Ring) Append(c wire.Chunk) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.ended {
		return
	}
	r.in += int64(len(c.Payload))
	if c.IsHeader() {
		r.header, r.hasHeader = c.Payload, true
		r.wakeLocked()
		return
	}
	r.evictLocked()
	r.chunks = append(r.chunks, entry{seq: r.nextSeq, at: r.cfg.Now(), joinable: c.Joinable(), payload: c.Payload})
	r.nextSeq++
	r.bytes += len(c.Payload)
	r.evictLocked()
	r.wakeLocked()
}

// Restart is a second upload replacing the first (ticket 165 rule 7): the old window and header are
// dropped, and every listener will continue from the next joinable chunk of the new upload, after the
// new upload's header chunk. Listeners stay connected.
func (r *Ring) Restart() {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.epoch++
	for i := r.head; i < len(r.chunks); i++ {
		r.chunks[i] = entry{}
	}
	r.chunks, r.head, r.bytes = r.chunks[:0], 0, 0
	r.header, r.hasHeader = nil, false
	r.wakeLocked()
}

// End ends the broadcast: every listener finishes what it was handed and then its body ends cleanly.
func (r *Ring) End() {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.ended {
		return
	}
	r.ended = true
	r.wakeLocked()
}

// Held is how many chunks and payload bytes the ring holds now (tests and the byte-cap guard).
func (r *Ring) Held() (chunks, bytes int) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.evictLocked()
	return len(r.chunks) - r.head, r.bytes
}

// BytesIn is how many payload bytes were appended over the ring's life.
func (r *Ring) BytesIn() int64 {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.in
}

type phase int

const (
	// phaseNew: a listener that has not started: it receives the header chunk and starts at the NEWEST
	// joinable chunk in the ring (the live edge, at most one window behind), or at the next joinable
	// chunk to arrive when there is none.
	phaseNew phase = iota
	// phaseEpoch: the upload was replaced: the header chunk of the new upload, then the oldest joinable
	// chunk of the new upload (the next one to arrive when there is none yet).
	phaseEpoch
	// phaseResync: the listener fell out of the window: no header (it is already past the start), the
	// OLDEST joinable chunk still in the ring, or the next joinable chunk to arrive.
	phaseResync
	// phaseStream: caught up, reading by cursor.
	phaseStream
)

// Listener is one cursor over a Ring. It is used by one goroutine.
type Listener struct {
	r     *Ring
	next  uint64
	epoch uint64
	phase phase
}

// NewListener returns a cursor that has not started.
func (r *Ring) NewListener() *Listener { return &Listener{r: r, phase: phaseNew} }

// Take returns the payloads a listener should write now, in order: possibly the header chunk first,
// then chunks from its start or its cursor. When there is nothing to write yet, wait is closed when
// something changes - take again then; when the broadcast has ended and everything was handed out,
// ended is true. Take never blocks.
func (l *Listener) Take() (out [][]byte, wait <-chan struct{}, ended bool) {
	r := l.r
	r.mu.Lock()
	defer r.mu.Unlock()
	r.evictLocked()

	if l.phase == phaseStream {
		switch {
		case l.epoch != r.epoch:
			l.phase = phaseEpoch
		case l.next < r.firstSeqLocked():
			l.phase = phaseResync // chunks it had not been handed were dropped: it skips, it never stalls
		}
	}
	if l.phase == phaseStream {
		if l.next < r.nextSeq {
			base := r.firstSeqLocked()
			for i := r.head + int(l.next-base); i < len(r.chunks); i++ {
				out = append(out, r.chunks[i].payload)
			}
			l.next = r.nextSeq
		}
	} else {
		start := -1
		if l.phase == phaseNew {
			for i := len(r.chunks) - 1; i >= r.head; i-- {
				if r.chunks[i].joinable {
					start = i
					break
				}
			}
		} else {
			for i := r.head; i < len(r.chunks); i++ {
				if r.chunks[i].joinable {
					start = i
					break
				}
			}
		}
		if start >= 0 {
			if (l.phase == phaseNew || l.phase == phaseEpoch) && r.hasHeader {
				out = append(out, r.header)
			}
			for i := start; i < len(r.chunks); i++ {
				out = append(out, r.chunks[i].payload)
			}
			l.next, l.epoch, l.phase = r.nextSeq, r.epoch, phaseStream
		}
	}
	if len(out) == 0 {
		if r.ended {
			return nil, nil, true
		}
		return nil, r.notify, false
	}
	return out, nil, false
}

// RateGuard is the protection of ticket 165 rule 10: an upload that stays above a ceiling for a whole
// window is closed. It measures the average over consecutive windows, so a burst that is followed by
// quiet is not sustained, and a stream that is steady just above the ceiling is.
type RateGuard struct {
	// MaxBitsPerSec is the ceiling; zero or less disables the guard.
	MaxBitsPerSec int64
	// Window is the measuring period (5 s in the working defaults).
	Window time.Duration

	start time.Time
	bytes int64
}

// Add counts n bytes arriving at now and reports whether the window that has just ended was above
// the ceiling.
func (g *RateGuard) Add(n int, now time.Time) (exceeded bool) {
	if g.MaxBitsPerSec <= 0 || g.Window <= 0 {
		return false
	}
	if g.start.IsZero() {
		g.start = now
	}
	if el := now.Sub(g.start); el >= g.Window {
		exceeded = float64(g.bytes)*8/el.Seconds() > float64(g.MaxBitsPerSec)
		g.start, g.bytes = now, 0
	}
	g.bytes += int64(n)
	return exceeded
}
