package rendezvous

import (
	"bytes"
	"errors"
	"io"
	"net"
	"testing"
	"time"

	"fms/exchange-server/internal/relay"
	"fms/exchange-server/internal/wire"
)

// White-box tests of wsConn over plain loopback TCP: the parts of WEB-BINDING websocket-binding that
// the server's openings cannot reach yet - the raw phase (after `connected` and its kin), the message
// cap as a parameter, the half-close in both directions and through a splice, and the wait for the
// peer's Close. A WebSocket carrying more than the administration stream is a later slice; these tests
// hold the layer it will stand on.

var ibMask = [4]byte{0x11, 0x22, 0x33, 0x44}

// ibFrame is a masked client frame with the shortest length form.
func ibFrame(b0 byte, payload []byte) []byte {
	n := len(payload)
	var out []byte
	switch {
	case n < 126:
		out = []byte{b0, 0x80 | byte(n)}
	case n <= 65535:
		out = []byte{b0, 0x80 | 126, byte(n >> 8), byte(n)}
	default:
		out = []byte{b0, 0x80 | 127, 0, 0, 0, 0, byte(n >> 24), byte(n >> 16), byte(n >> 8), byte(n)}
	}
	out = append(out, ibMask[:]...)
	for i, b := range payload {
		out = append(out, b^ibMask[i%4])
	}
	return out
}

func ibBinary(payload string) []byte { return ibFrame(0x82, []byte(payload)) }

// ibPeer is the browser's end of a connection to a wsConn.
type ibPeer struct {
	t    *testing.T
	conn *net.TCPConn
}

func (p *ibPeer) send(b ...[]byte) {
	p.t.Helper()
	if _, err := p.conn.Write(bytes.Join(b, nil)); err != nil {
		p.t.Fatal(err)
	}
}

// read returns exactly n bytes the server wrote, or fails.
func (p *ibPeer) read(n int, within time.Duration) []byte {
	p.t.Helper()
	_ = p.conn.SetReadDeadline(time.Now().Add(within))
	b := make([]byte, n)
	if _, err := io.ReadFull(p.conn, b); err != nil {
		p.t.Fatalf("reading %d bytes: %v", n, err)
	}
	return b
}

// expectNothing requires that the server writes nothing within the time.
func (p *ibPeer) expectNothing(within time.Duration) {
	p.t.Helper()
	_ = p.conn.SetReadDeadline(time.Now().Add(within))
	b := make([]byte, 1)
	n, err := p.conn.Read(b)
	if n > 0 {
		p.t.Fatalf("the server wrote %#x, want nothing", b[0])
	}
	var ne net.Error
	if !errors.As(err, &ne) || !ne.Timeout() {
		p.t.Fatalf("want a quiet wire, got %v", err)
	}
}

func (p *ibPeer) expectEnd(within time.Duration) {
	p.t.Helper()
	_ = p.conn.SetReadDeadline(time.Now().Add(within))
	if n, err := p.conn.Read(make([]byte, 1)); n != 0 || err == nil {
		p.t.Fatalf("want the end of the connection, got %d bytes, %v", n, err)
	} else if ne := (net.Error)(nil); errors.As(err, &ne) && ne.Timeout() {
		p.t.Fatalf("the connection did not end: %v", err)
	}
}

// ibTCP is a connected pair of loopback connections: the server's and the peer's.
func ibTCP(t *testing.T) (srv *net.TCPConn, peer *net.TCPConn) {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	type res struct {
		c   net.Conn
		err error
	}
	acc := make(chan res, 1)
	go func() { c, err := ln.Accept(); acc <- res{c, err} }()
	c, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatal(err)
	}
	r := <-acc
	if r.err != nil {
		t.Fatal(r.err)
	}
	t.Cleanup(func() { _ = c.Close(); _ = r.c.Close() })
	return r.c.(*net.TCPConn), c.(*net.TCPConn)
}

func ibPair(t *testing.T, max int) (*wsConn, *ibPeer) {
	t.Helper()
	srv, peer := ibTCP(t)
	return newWSConn(srv, nil, max), &ibPeer{t: t, conn: peer}
}

func wantClose(t *testing.T, p *ibPeer, code uint16) {
	t.Helper()
	got := p.read(4, 3*time.Second)
	if !bytes.Equal(got, []byte{0x88, 2, byte(code >> 8), byte(code)}) {
		t.Fatalf("want Close(%d), got % x", code, got)
	}
}

// Raw phase: "a message is any run of at least one byte"; the messages read as one byte stream, and
// the empty message is the end of the peer's direction, for good.
func TestWSConnRawPhaseIsAByteStream(t *testing.T) {
	ws, p := ibPair(t, 0)
	p.send(ibBinary("ab"), ibBinary("cde"), ibBinary("x"), ibBinary(""))
	var got []byte
	buf := make([]byte, 2) // smaller than a message
	for {
		n, err := ws.Read(buf)
		got = append(got, buf[:n]...)
		if err != nil {
			if err != io.EOF {
				t.Fatal(err)
			}
			break
		}
	}
	if string(got) != "abcdex" {
		t.Fatalf("read %q", got)
	}
	for i := 0; i < 2; i++ {
		if n, err := ws.Read(buf); n != 0 || err != io.EOF {
			t.Fatalf("after the half-close: %d bytes, %v; want EOF, for good", n, err)
		}
	}
	// the other direction is untouched by it
	if _, err := ws.Write([]byte("still here")); err != nil {
		t.Fatal(err)
	}
	if got := p.read(2+10, time.Second); string(got[2:]) != "still here" {
		t.Fatalf("% x", got)
	}
}

// An empty slice is never put on the wire as a message - that is the half-close and only CloseWrite
// says it, once.
func TestWSConnZeroLengthWriteSendsNothingAndCloseWriteSendsTheEmptyMessage(t *testing.T) {
	ws, p := ibPair(t, 0)
	for _, b := range [][]byte{nil, {}} {
		if n, err := ws.Write(b); n != 0 || err != nil {
			t.Fatalf("Write(%v) = %d, %v", b, n, err)
		}
	}
	p.expectNothing(150 * time.Millisecond)
	if _, err := ws.Write([]byte("x")); err != nil {
		t.Fatal(err)
	}
	if got := p.read(3, time.Second); !bytes.Equal(got, []byte{0x82, 1, 'x'}) {
		t.Fatalf("the first frame is % x, want the message \"x\"", got)
	}
	if err := ws.CloseWrite(); err != nil {
		t.Fatal(err)
	}
	if got := p.read(2, time.Second); !bytes.Equal(got, []byte{0x82, 0}) {
		t.Fatalf("CloseWrite sent % x, want the empty binary message", got)
	}
	if err := ws.CloseWrite(); err != nil {
		t.Fatal(err)
	}
	p.expectNothing(150 * time.Millisecond)
	if _, err := ws.Write([]byte("late")); err == nil {
		t.Fatal("a write after the half-close was accepted")
	}
}

// "The server caps a message ... and closes 1009 above it": the cap is a parameter, judged on the
// header and on the sum of fragments, and a write above it is refused.
func TestWSConnMessageCap(t *testing.T) {
	t.Run("exactly the cap is taken", func(t *testing.T) {
		ws, p := ibPair(t, 100)
		p.send(ibFrame(0x82, bytes.Repeat([]byte("a"), 100)))
		if n, err := io.ReadFull(ws, make([]byte, 100)); err != nil || n != 100 {
			t.Fatalf("%d, %v", n, err)
		}
	})
	t.Run("one above the cap in one frame", func(t *testing.T) {
		ws, p := ibPair(t, 100)
		p.send(ibFrame(0x82, bytes.Repeat([]byte("a"), 101)))
		var ce *wsCloseError
		if _, err := ws.Read(make([]byte, 1)); !errors.As(err, &ce) || ce.code != 1009 {
			t.Fatalf("Read: %v", err)
		}
		wantClose(t, p, 1009)
	})
	t.Run("fragments that add up above the cap", func(t *testing.T) {
		ws, p := ibPair(t, 100)
		p.send(ibFrame(0x02, bytes.Repeat([]byte("a"), 60)), ibFrame(0x80, bytes.Repeat([]byte("a"), 41)))
		var ce *wsCloseError
		if _, err := ws.Read(make([]byte, 1)); !errors.As(err, &ce) || ce.code != 1009 {
			t.Fatalf("Read: %v", err)
		}
		wantClose(t, p, 1009)
	})
	t.Run("fragments that add up to the cap", func(t *testing.T) {
		ws, p := ibPair(t, 100)
		p.send(ibFrame(0x02, bytes.Repeat([]byte("a"), 60)), ibFrame(0x80, bytes.Repeat([]byte("b"), 40)))
		if n, err := io.ReadFull(ws, make([]byte, 100)); err != nil || n != 100 {
			t.Fatalf("%d, %v", n, err)
		}
	})
	t.Run("a write above the cap", func(t *testing.T) {
		ws, p := ibPair(t, 100)
		if _, err := ws.Write(make([]byte, 101)); !errors.Is(err, errWSMessageTooBig) {
			t.Fatalf("Write: %v", err)
		}
		p.expectNothing(100 * time.Millisecond)
	})
}

// A read deadline that fires in the middle of a header or of a payload loses nothing: the probe
// windows of the engine (a deadline, then the same stream read on) depend on it.
func TestWSConnReadResumesAfterADeadlineInsideAFrame(t *testing.T) {
	ws, p := ibPair(t, 0)
	frame := ibBinary("0123456789")
	for _, cut := range []int{1, 3, 7, 11} { // inside the header, the mask, the payload
		t.Run("", func(t *testing.T) {
			p.send(frame[:cut])
			_ = ws.SetReadDeadline(time.Now().Add(80 * time.Millisecond))
			_, err := ws.Read(make([]byte, 64))
			var ne net.Error
			if !errors.As(err, &ne) || !ne.Timeout() {
				t.Fatalf("want a timeout, got %v", err)
			}
			p.send(frame[cut:])
			_ = ws.SetReadDeadline(time.Now().Add(3 * time.Second))
			got := make([]byte, 10)
			if _, err := io.ReadFull(ws, got); err != nil || string(got) != "0123456789" {
				t.Fatalf("%q, %v", got, err)
			}
		})
	}
}

// The envelope phase and the raw phase share one connection: the frame reader takes exactly one
// message per frame, and the bytes that follow are read raw - the shape of `connected` followed by
// the tunnel.
func TestWSEnvelopeReaderThenRawBytesThenHalfClose(t *testing.T) {
	ws, p := ibPair(t, 0)
	fr := wire.NewFrameReader(&wsEnvelopeReader{w: ws, limit: func() int { return wire.MaxFrame }}, wire.MaxFrame)
	bye, err := wire.EncodeFrame(&wire.Bye{}, wire.MaxFrame)
	if err != nil {
		t.Fatal(err)
	}
	p.send(ibFrame(0x82, bye), ibBinary("RAW"), ibBinary("BYTES"), ibBinary(""))
	_ = ws.SetReadDeadline(time.Now().Add(3 * time.Second))
	if e, err := fr.Read(); err != nil {
		t.Fatal(err)
	} else if _, ok := e.(*wire.Bye); !ok {
		t.Fatalf("%T", e)
	}
	if got, err := io.ReadAll(ws); err != nil || string(got) != "RAWBYTES" {
		t.Fatalf("%q, %v", got, err)
	}
}

// "While the stream carries envelopes, one message is exactly one frame" - the same rule one level
// down, on a frame reader fed by messages, for the second and later envelopes of a stream.
func TestWSEnvelopeReaderRefusesAMessageThatIsNotOneFrame(t *testing.T) {
	bye, err := wire.EncodeFrame(&wire.Bye{}, wire.MaxFrame)
	if err != nil {
		t.Fatal(err)
	}
	for name, msg := range map[string][]byte{
		"trailing bytes": append(append([]byte(nil), bye...), 0),
		"short message":  bye[:len(bye)-1],
		"two frames":     append(append([]byte(nil), bye...), bye...),
	} {
		t.Run(name, func(t *testing.T) {
			ws, p := ibPair(t, 0)
			fr := wire.NewFrameReader(&wsEnvelopeReader{w: ws, limit: func() int { return wire.MaxFrame }}, wire.MaxFrame)
			p.send(ibFrame(0x82, bye), ibFrame(0x82, msg))
			_ = ws.SetReadDeadline(time.Now().Add(3 * time.Second))
			if _, err := fr.Read(); err != nil {
				t.Fatalf("the good frame: %v", err)
			}
			var ce *wsCloseError
			if _, err := fr.Read(); !errors.As(err, &ce) || ce.code != 1002 {
				t.Fatalf("the bad message: %v", err)
			}
			wantClose(t, p, 1002)
		})
	}
}

// "A server that sends Close waits briefly for the reply before it closes the transport."
func TestWSConnCloseWaitsForThePeersClose(t *testing.T) {
	closeInBackground := func(ws *wsConn) chan time.Time {
		done := make(chan time.Time, 1)
		go func() { _ = ws.Close(); done <- time.Now() }()
		return done
	}
	t.Run("the peer answers", func(t *testing.T) {
		ws, p := ibPair(t, 0)
		done := closeInBackground(ws)
		wantClose(t, p, 1000)
		select {
		case <-done:
			t.Fatal("Close returned before the peer's reply")
		case <-time.After(wsCloseWait / 4):
		}
		p.send(ibFrame(0x88, []byte{0x03, 0xE8}))
		select {
		case <-done:
		case <-time.After(wsCloseWait / 2):
			t.Fatal("Close did not return after the reply")
		}
		p.expectEnd(time.Second)
	})
	t.Run("the peer never answers", func(t *testing.T) {
		ws, p := ibPair(t, 0)
		start := time.Now()
		done := closeInBackground(ws)
		wantClose(t, p, 1000)
		end := <-done
		if d := end.Sub(start); d < wsCloseWait*8/10 || d > wsCloseWait+3*time.Second {
			t.Fatalf("Close waited %v, want about %v", d, wsCloseWait)
		}
		p.expectEnd(time.Second)
	})
	t.Run("the peer goes away", func(t *testing.T) {
		ws, p := ibPair(t, 0)
		start := time.Now()
		done := closeInBackground(ws)
		wantClose(t, p, 1000)
		_ = p.conn.Close()
		if d := (<-done).Sub(start); d > wsCloseWait/2 {
			t.Fatalf("Close waited %v for a peer that was gone", d)
		}
	})
	t.Run("another goroutine is reading", func(t *testing.T) {
		ws, p := ibPair(t, 0)
		readErr := make(chan error, 1)
		go func() { _, err := ws.Read(make([]byte, 8)); readErr <- err }()
		time.Sleep(50 * time.Millisecond) // the reader is parked in its read
		done := closeInBackground(ws)
		wantClose(t, p, 1000)
		select {
		case <-done:
			t.Fatal("Close returned before the peer's reply")
		case <-time.After(wsCloseWait / 4):
		}
		p.send(ibFrame(0x88, nil))
		select {
		case <-done:
		case <-time.After(wsCloseWait / 2):
			t.Fatal("Close did not return after the reply")
		}
		if err := <-readErr; err != io.EOF {
			t.Fatalf("the reader ended with %v, want EOF", err)
		}
	})
	t.Run("after a failure the peer's reply is found behind the bad frame", func(t *testing.T) {
		ws, p := ibPair(t, 0)
		p.send(ibFrame(0xC2, []byte("zz")), ibFrame(0x88, []byte{0x03, 0xEA}))
		if _, err := ws.Read(make([]byte, 1)); err == nil {
			t.Fatal("RSV1 was accepted")
		}
		wantClose(t, p, 1002)
		start := time.Now()
		_ = ws.Close()
		if d := time.Since(start); d > wsCloseWait/2 {
			t.Fatalf("Close waited %v although the peer had answered", d)
		}
	})
}

// Half-close through a bridge: the empty message the peer sends reaches the other side as the end of
// that direction (a TCP FIN to a TCP peer, the empty message to a WebSocket peer), in both
// directions, while the other direction goes on.
func TestSplicePassesTheHalfCloseBetweenWebSockets(t *testing.T) {
	a, pa := ibPair(t, 0)
	b, pb := ibPair(t, 0)
	type result struct{ stats relay.Stats }
	done := make(chan result, 1)
	go func() { done <- result{relay.Splice(a, b, 10*time.Second)} }()

	pa.send(ibBinary("hello"), ibBinary(""))
	if got := pb.read(2+5, 3*time.Second); string(got[2:]) != "hello" {
		t.Fatalf("% x", got)
	}
	if got := pb.read(2, 3*time.Second); !bytes.Equal(got, []byte{0x82, 0}) {
		t.Fatalf("the half-close did not pass: % x", got)
	}
	// the direction b -> a still works, then ends the same way
	pb.send(ibBinary("world"), ibBinary(""))
	if got := pa.read(2+5, 3*time.Second); string(got[2:]) != "world" {
		t.Fatalf("% x", got)
	}
	if got := pa.read(2, 3*time.Second); !bytes.Equal(got, []byte{0x82, 0}) {
		t.Fatalf("the half-close did not pass back: % x", got)
	}
	select {
	case r := <-done:
		if r.stats.AToB != 5 || r.stats.BToA != 5 {
			t.Fatalf("stats %+v", r.stats)
		}
	case <-time.After(8 * time.Second):
		t.Fatal("the splice did not end after both directions ended")
	}
}

func TestSplicePassesTheHalfCloseBetweenAWebSocketAndATCPStream(t *testing.T) {
	a, pa := ibPair(t, 0)
	srv, peer := ibTCP(t)
	done := make(chan relay.Stats, 1)
	go func() { done <- relay.Splice(a, srv, 10*time.Second) }()

	// TCP FIN -> the empty message
	if err := peer.CloseWrite(); err != nil {
		t.Fatal(err)
	}
	if got := pa.read(2, 3*time.Second); !bytes.Equal(got, []byte{0x82, 0}) {
		t.Fatalf("a FIN did not become the empty message: % x", got)
	}
	// the empty message -> TCP FIN, after the bytes before it
	pa.send(ibBinary("data"), ibBinary(""))
	_ = peer.SetReadDeadline(time.Now().Add(3 * time.Second))
	if got, err := io.ReadAll(peer); err != nil || string(got) != "data" {
		t.Fatalf("%q, %v (want the bytes, then the end)", got, err)
	}
	select {
	case <-done:
	case <-time.After(8 * time.Second):
		t.Fatal("the splice did not end")
	}
}

// After our Close nothing more is written: not a message, not a pong.
func TestWSConnWritesNothingAfterClose(t *testing.T) {
	ws, p := ibPair(t, 0)
	if err := ws.writeClose(1000); err != nil {
		t.Fatal(err)
	}
	wantClose(t, p, 1000)
	if _, err := ws.Write([]byte("x")); err == nil {
		t.Fatal("a message was written after Close")
	}
	if err := ws.writePong([]byte("p")); err != nil {
		t.Fatal(err)
	}
	if err := ws.writeClose(1002); err != nil { // the first code wins, the second is not sent
		t.Fatal(err)
	}
	p.expectNothing(150 * time.Millisecond)
}
