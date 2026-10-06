package relay

import (
	"bytes"
	"io"
	"net"
	"testing"
	"time"
)

// pair returns the two ends of one loopback TCP connection.
func pair(t *testing.T) (client, server *net.TCPConn) {
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
	ch := make(chan res, 1)
	go func() { c, err := ln.Accept(); ch <- res{c, err} }()
	c, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatal(err)
	}
	r := <-ch
	if r.err != nil {
		t.Fatal(r.err)
	}
	t.Cleanup(func() { c.Close(); r.c.Close() })
	return c.(*net.TCPConn), r.c.(*net.TCPConn)
}

func spliceAsync(a, b net.Conn, idle time.Duration) <-chan Stats {
	out := make(chan Stats, 1)
	go func() { out <- Splice(a, b, idle) }()
	return out
}

func TestSpliceCopiesBothWaysAndCountsBytes(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	done := spliceAsync(a2, b2, 0)

	if _, err := a1.Write([]byte("to-b")); err != nil {
		t.Fatal(err)
	}
	buf := make([]byte, 4)
	if _, err := io.ReadFull(b1, buf); err != nil || string(buf) != "to-b" {
		t.Fatalf("a->b: %q %v", buf, err)
	}
	if _, err := b1.Write([]byte("to-a!")); err != nil {
		t.Fatal(err)
	}
	buf = make([]byte, 5)
	if _, err := io.ReadFull(a1, buf); err != nil || string(buf) != "to-a!" {
		t.Fatalf("b->a: %q %v", buf, err)
	}
	a1.Close()
	b1.Close()
	select {
	case st := <-done:
		if st.AToB != 4 || st.BToA != 5 {
			t.Fatalf("stats: %+v", st)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("Splice did not return after both ends closed")
	}
}

func TestSpliceHalfCloseKeepsTheOtherDirectionOpen(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	done := spliceAsync(a2, b2, 0)

	_, _ = a1.Write([]byte("request"))
	if err := a1.CloseWrite(); err != nil { // a is done sending
		t.Fatal(err)
	}
	got, err := io.ReadAll(b1) // b sees the request and then EOF, not a reset
	if err != nil || string(got) != "request" {
		t.Fatalf("b read %q, %v", got, err)
	}
	// the reply direction still works after a's half-close
	if _, err := b1.Write([]byte("reply")); err != nil {
		t.Fatal(err)
	}
	_ = b1.CloseWrite()
	reply, err := io.ReadAll(a1)
	if err != nil || string(reply) != "reply" {
		t.Fatalf("a read %q, %v", reply, err)
	}
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("Splice did not finish after both directions ended")
	}
}

func TestSpliceIdleTimeoutClosesBoth(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	start := time.Now()
	done := spliceAsync(a2, b2, 200*time.Millisecond)
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("an idle bridge was never closed")
	}
	if d := time.Since(start); d < 150*time.Millisecond {
		t.Fatalf("closed after %v, before the idle bound", d)
	}
	_ = a1.SetReadDeadline(time.Now().Add(time.Second))
	if _, err := a1.Read(make([]byte, 1)); err == nil {
		t.Fatal("the far end must see the close")
	}
	_ = b1.SetReadDeadline(time.Now().Add(time.Second))
	if _, err := b1.Read(make([]byte, 1)); err == nil {
		t.Fatal("the far end must see the close")
	}
}

func TestSpliceActivityKeepsABridgeOpen(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	done := spliceAsync(a2, b2, 300*time.Millisecond)
	// traffic every 100 ms for a second: far more than the idle bound in total, never idle
	go io.Copy(io.Discard, b1)
	for i := 0; i < 10; i++ {
		_, _ = a1.Write([]byte("x"))
		time.Sleep(100 * time.Millisecond)
	}
	select {
	case <-done:
		t.Fatal("a bridge with traffic was closed as idle")
	default:
	}
	a1.Close()
	b1.Close()
	<-done
}

func TestSpliceAHardErrorClosesBothAtOnce(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	done := spliceAsync(a2, b2, 0)
	// an abortive close (RST) on one side is not a clean end: the other side must be closed too
	_ = a1.SetLinger(0)
	a1.Close()
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("a reset on one side left the bridge open")
	}
	_ = b1.SetReadDeadline(time.Now().Add(time.Second))
	if _, err := b1.Read(make([]byte, 1)); err == nil {
		t.Fatal("the far side of a reset bridge must be closed")
	}
}

func TestSpliceMovesLargeStreamsIntact(t *testing.T) {
	a1, a2 := pair(t)
	b1, b2 := pair(t)
	done := spliceAsync(a2, b2, 0)
	payload := bytes.Repeat([]byte("0123456789abcdef"), 1<<16) // 1 MiB
	go func() {
		_, _ = a1.Write(payload)
		_ = a1.CloseWrite()
	}()
	got, err := io.ReadAll(b1)
	if err != nil || !bytes.Equal(got, payload) {
		t.Fatalf("payload changed or short: %d bytes, %v", len(got), err)
	}
	b1.Close()
	a1.Close()
	<-done
}
