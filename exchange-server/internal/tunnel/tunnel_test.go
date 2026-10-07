package tunnel

import (
	"net"
	"testing"
	"time"

	"fms/exchange-server/internal/wire"
)

func pipePair(t *testing.T) (a, b net.Conn) {
	t.Helper()
	a, b = net.Pipe()
	t.Cleanup(func() { a.Close(); b.Close() })
	return
}

func isClosedFor(c net.Conn, d time.Duration) bool {
	_ = c.SetReadDeadline(time.Now().Add(d))
	_, err := c.Read(make([]byte, 1))
	if err == nil {
		return false
	}
	ne, ok := err.(net.Error)
	return !(ok && ne.Timeout())
}

func TestOpenIssuesSingleUseIDsAndEnforcesTheCap(t *testing.T) {
	tb := NewTable()
	key := ResourceKey("alice", "R")
	var tunnels []*Tunnel
	seen := map[string]bool{}
	for i := 0; i < 3; i++ {
		c, _ := pipePair(t)
		tn, err := tb.Open(key, "dev", c, 3)
		if err != nil {
			t.Fatal(err)
		}
		if !wire.ValidID(tn.ID) || seen[tn.ID] {
			t.Fatalf("tunnel id %q", tn.ID)
		}
		seen[tn.ID] = true
		tunnels = append(tunnels, tn)
	}
	c, _ := pipePair(t)
	if _, err := tb.Open(key, "dev", c, 3); err != ErrFull {
		t.Fatalf("the fourth: %v", err)
	}
	// another resource has its own count
	if _, err := tb.Open(ResourceKey("alice", "R2"), "dev", c, 3); err != nil {
		t.Fatalf("another resource: %v", err)
	}
	if _, err := tb.Open(ResourceKey("bob", "R"), "dev", c, 3); err != nil {
		t.Fatalf("the same resourceId of another account: %v", err)
	}
	if tb.Count(key) != 3 {
		t.Fatal("count")
	}
	// a finished tunnel frees its slot
	tunnels[0].Done()
	if tb.Count(key) != 2 {
		t.Fatal("Done must free the slot")
	}
	if _, err := tb.Open(key, "dev", c, 3); err != nil {
		t.Fatalf("a freed slot: %v", err)
	}
	// Done twice is harmless
	tunnels[0].Done()
}

func TestClaimIsSingleUseAndBoundToTheProducerDevice(t *testing.T) {
	tb := NewTable()
	c, _ := pipePair(t)
	tn, _ := tb.Open(ResourceKey("a", "R"), "producer", c, 16)
	if _, ok := tb.Claim("nope", "producer"); ok {
		t.Fatal("an unknown id claimed")
	}
	if _, ok := tb.Claim(tn.ID, "intruder"); ok {
		t.Fatal("a foreign device claimed the tunnel")
	}
	if tb.Pending() != 1 {
		t.Fatal("a foreign attach must leave the real one waiting")
	}
	got, ok := tb.Claim(tn.ID, "producer")
	if !ok || got != tn {
		t.Fatal("the producer could not claim its tunnel")
	}
	if _, ok := tb.Claim(tn.ID, "producer"); ok {
		t.Fatal("a tunnel id is single use")
	}
	if tb.Pending() != 0 || tb.Count(ResourceKey("a", "R")) != 1 {
		t.Fatal("claimed: no longer pending, still counted until Done")
	}
}

func TestWaitReturnsTheDeliveredStreamOrGivesUpAfterTheWindow(t *testing.T) {
	tb := NewTable()
	c, _ := pipePair(t)
	tn, _ := tb.Open(ResourceKey("a", "R"), "p", c, 16)
	data, peer := pipePair(t)
	go func() {
		got, _ := tb.Claim(tn.ID, "p")
		got.Deliver(data)
	}()
	got, ok := tn.Wait(2*time.Second, nil)
	if !ok || got != data {
		t.Fatalf("wait: %v", ok)
	}
	_ = peer

	// the window passes: the tunnel cannot be claimed any more
	c2, _ := pipePair(t)
	tn2, _ := tb.Open(ResourceKey("a", "R"), "p", c2, 16)
	start := time.Now()
	if _, ok := tn2.Wait(150*time.Millisecond, nil); ok || time.Since(start) < 140*time.Millisecond {
		t.Fatalf("a window that passes is a failure, after the window (%v)", time.Since(start))
	}
	if _, ok := tb.Claim(tn2.ID, "p"); ok {
		t.Fatal("an expired tunnel was claimed")
	}
	tn2.Done()
	if tb.Count(ResourceKey("a", "R")) != 1 { // tn is still bridged
		t.Fatal("the expired tunnel did not free its slot")
	}
}

func TestAnAttachThatWinsTheRaceAtTheWindowIsNotLost(t *testing.T) {
	for i := 0; i < 50; i++ {
		tb := NewTable()
		c, _ := pipePair(t)
		tn, _ := tb.Open(ResourceKey("a", "R"), "p", c, 16)
		data, _ := pipePair(t)
		go func() {
			time.Sleep(time.Duration(i%5) * time.Millisecond)
			if got, ok := tb.Claim(tn.ID, "p"); ok {
				got.Deliver(data)
			}
		}()
		got, ok := tn.Wait(3*time.Millisecond, nil)
		if ok && got != data {
			t.Fatal("wrong stream")
		}
		if !ok {
			// the window won: then the attach must not have been claimed, or its stream is closed
			if _, claimed := tb.Claim(tn.ID, "p"); claimed {
				t.Fatal("a tunnel that gave up can still be claimed")
			}
		}
	}
}

func TestAbortEndsBothStreamsAndAClosingServerEndsTheWait(t *testing.T) {
	tb := NewTable()
	consumer, consumerPeer := pipePair(t)
	tn, _ := tb.Open(ResourceKey("a", "R"), "p", consumer, 16)
	data, dataPeer := pipePair(t)
	go func() {
		got, _ := tb.Claim(tn.ID, "p")
		got.Deliver(data)
	}()
	if _, ok := tn.Wait(2*time.Second, nil); !ok {
		t.Fatal("wait")
	}
	tn.Abort()
	tn.Abort() // idempotent
	if !isClosedFor(consumerPeer, time.Second) || !isClosedFor(dataPeer, time.Second) {
		t.Fatal("Abort must close the consumer's stream and the producer's")
	}

	c, _ := pipePair(t)
	tn2, _ := tb.Open(ResourceKey("a", "R"), "p", c, 16)
	closing := make(chan struct{})
	go func() { time.Sleep(50 * time.Millisecond); close(closing) }()
	if _, ok := tn2.Wait(5*time.Second, closing); ok {
		t.Fatal("a closing server must end the wait")
	}
}

func TestDeliverClosesAStreamForAnAbortedTunnel(t *testing.T) {
	tb := NewTable()
	consumer, consumerPeer := pipePair(t)
	tn, _ := tb.Open(ResourceKey("a", "R"), "p", consumer, 16)
	_ = consumerPeer
	tn.Abort()
	data, peer := net.Pipe()
	defer peer.Close()
	tn.Deliver(data)
	if !isClosedFor(peer, 2*time.Second) {
		t.Fatal("a stream delivered to an aborted tunnel must be closed, not left open")
	}
}

func TestDeliverToALiveTunnelKeepsTheStream(t *testing.T) {
	tb := NewTable()
	consumer, _ := pipePair(t)
	tn, _ := tb.Open(ResourceKey("a", "R"), "p", consumer, 16)
	data, peer := pipePair(t)
	tn.Deliver(data)
	select {
	case got := <-tn.attached:
		if got != data {
			t.Fatal("wrong stream")
		}
	default:
		t.Fatal("the stream was not delivered")
	}
	_ = peer
}

func TestAbortByKeyDeviceAndAll(t *testing.T) {
	tb := NewTable()
	type tc struct {
		tn   *Tunnel
		peer net.Conn
	}
	mk := func(key Key, dev string) tc {
		a, b := pipePair(t)
		tn, err := tb.Open(key, dev, a, 16)
		if err != nil {
			t.Fatal(err)
		}
		return tc{tn, b}
	}
	r1 := mk(ResourceKey("a", "R1"), "d1")
	r1b := mk(ResourceKey("a", "R1"), "d1")
	r2 := mk(ResourceKey("a", "R2"), "d1")
	b1 := mk(BroadcastKey("B1"), "d2")
	b2 := mk(BroadcastKey("B2"), "d3")

	tb.AbortKey(ResourceKey("a", "R1"))
	if !isClosedFor(r1.peer, time.Second) || !isClosedFor(r1b.peer, time.Second) {
		t.Fatal("AbortKey")
	}
	if isClosedFor(r2.peer, 50*time.Millisecond) || isClosedFor(b1.peer, 50*time.Millisecond) {
		t.Fatal("AbortKey touched another entity")
	}
	tb.AbortDevice("d1")
	if !isClosedFor(r2.peer, time.Second) {
		t.Fatal("AbortDevice")
	}
	if isClosedFor(b1.peer, 50*time.Millisecond) {
		t.Fatal("AbortDevice touched another device")
	}
	tb.AbortAll()
	if !isClosedFor(b1.peer, time.Second) || !isClosedFor(b2.peer, time.Second) {
		t.Fatal("AbortAll")
	}
	select {
	case <-b2.tn.Aborted():
	default:
		t.Fatal("Aborted must be closed")
	}
}
