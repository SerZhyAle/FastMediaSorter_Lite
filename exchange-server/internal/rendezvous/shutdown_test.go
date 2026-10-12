package rendezvous_test

import (
	"testing"
	"time"

	"fms/exchange-server/internal/rendezvous"
)

type heldSweepBackend struct {
	rendezvous.Backend
	entered chan struct{}
	release chan struct{}
}

func (b *heldSweepBackend) Sweep(time.Time) {
	select {
	case b.entered <- struct{}{}:
	default:
	}
	<-b.release
}

func TestCloseWaitsForAnInFlightSweepBeforeReturning(t *testing.T) {
	b := &heldSweepBackend{entered: make(chan struct{}, 1), release: make(chan struct{})}
	released := false
	defer func() {
		if !released {
			close(b.release)
		}
	}()
	env := startLayer(t, func(c *rendezvous.Config) {
		b.Backend = c.Backend
		c.Backend = b
		c.Params.SweepInterval = time.Millisecond
	})
	select {
	case <-b.entered:
	case <-time.After(2 * time.Second):
		t.Fatal("the sweep did not start")
	}
	done := make(chan struct{})
	go func() { env.srv.Close(); close(done) }()
	select {
	case <-done:
		t.Fatal("Close returned while a sweep could still write to the store")
	case <-time.After(30 * time.Millisecond):
	}
	close(b.release)
	released = true
	select {
	case <-done:
	case <-time.After(2 * time.Second):
		t.Fatal("Close did not finish after the sweep was released")
	}
}
