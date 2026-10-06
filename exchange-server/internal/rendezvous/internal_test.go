package rendezvous

import (
	"net"
	"testing"
	"time"
)

// A data stream that arrives after its tunnel was aborted (the registration ended in between) must
// be closed, not left open for nobody to read.
func TestDeliverClosesAStreamForAnAbortedTunnel(t *testing.T) {
	consumerA, consumerB := net.Pipe()
	defer consumerB.Close()
	tun := &tunnel{consumer: consumerA, attached: make(chan net.Conn, 1), done: make(chan struct{})}
	tun.abort()

	data, peer := net.Pipe()
	defer peer.Close()
	tun.deliver(data)

	_ = peer.SetReadDeadline(time.Now().Add(2 * time.Second))
	if _, err := peer.Read(make([]byte, 1)); err == nil {
		t.Fatal("a stream delivered to an aborted tunnel must be closed")
	} else if ne, ok := err.(net.Error); ok && ne.Timeout() {
		t.Fatal("the stream was left open")
	}
}

func TestDeliverToALiveTunnelKeepsTheStream(t *testing.T) {
	consumerA, consumerB := net.Pipe()
	defer consumerA.Close()
	defer consumerB.Close()
	tun := &tunnel{consumer: consumerA, attached: make(chan net.Conn, 1), done: make(chan struct{})}
	data, peer := net.Pipe()
	defer data.Close()
	defer peer.Close()
	tun.deliver(data)
	select {
	case got := <-tun.attached:
		if got != data {
			t.Fatal("wrong stream")
		}
	default:
		t.Fatal("the stream was not delivered")
	}
}
