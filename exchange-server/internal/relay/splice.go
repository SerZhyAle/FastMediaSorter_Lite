// Package relay is the bridge (contract section 5.4, duty R-4): two streams spliced together,
// bytes opaque in both directions, bounded buffers, backpressure by TCP flow control, and no
// interpretation of any byte.
package relay

import (
	"errors"
	"io"
	"net"
	"sync"
	"sync/atomic"
	"time"
)

// BufSize is the copy buffer of one direction.
const BufSize = 32 * 1024

var bufPool = sync.Pool{New: func() any { b := make([]byte, BufSize); return &b }}

type closeWriter interface{ CloseWrite() error }

// Stats is what crossed the bridge.
type Stats struct{ AToB, BToA int64 }

// Splice copies a<->b until both directions end or the bridge has been idle for idle (no byte
// either way). A clean end of one direction is passed on as a half-close and the other direction
// keeps running; any other error closes both at once. Both streams are closed on return.
func Splice(a, b net.Conn, idle time.Duration) Stats {
	var last atomic.Int64
	last.Store(time.Now().UnixNano())
	var ab, ba atomic.Int64

	errc := make(chan error, 2)
	go pump(b, a, &last, &ab, errc)
	go pump(a, b, &last, &ba, errc)

	stop := make(chan struct{})
	if idle > 0 {
		tick := idle / 4
		if tick < 5*time.Millisecond {
			tick = 5 * time.Millisecond
		}
		if tick > time.Second {
			tick = time.Second
		}
		go func() {
			t := time.NewTicker(tick)
			defer t.Stop()
			for {
				select {
				case <-stop:
					return
				case now := <-t.C:
					if now.UnixNano()-last.Load() > int64(idle) {
						_ = a.Close()
						_ = b.Close()
						return
					}
				}
			}
		}()
	}

	closeBoth := func() { _ = a.Close(); _ = b.Close() }
	if err := <-errc; err != nil {
		closeBoth()
	}
	<-errc
	close(stop)
	closeBoth()
	return Stats{AToB: ab.Load(), BToA: ba.Load()}
}

// pump copies src to dst. It reports nil on a clean end of src (after half-closing dst) and the
// error otherwise.
func pump(dst, src net.Conn, last, n *atomic.Int64, done chan<- error) {
	bp := bufPool.Get().(*[]byte)
	defer bufPool.Put(bp)
	buf := *bp
	for {
		nr, er := src.Read(buf)
		if nr > 0 {
			nw, ew := dst.Write(buf[:nr])
			n.Add(int64(nw))
			last.Store(time.Now().UnixNano())
			if ew != nil {
				done <- ew
				return
			}
			if nw != nr {
				done <- io.ErrShortWrite
				return
			}
		}
		if er != nil {
			if errors.Is(er, io.EOF) {
				if cw, ok := dst.(closeWriter); ok {
					_ = cw.CloseWrite()
				}
				done <- nil
				return
			}
			done <- er
			return
		}
	}
}
