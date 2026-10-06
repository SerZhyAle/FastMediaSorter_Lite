package deploy

import (
	"context"
	"errors"
	"path/filepath"
	"testing"
	"time"
)

// TestStopEventNameVector pins the algorithm the Share Manager reimplements: first 4 bytes of
// SHA-256 over the lower-cased absolute path, as 8 lower-case hex characters. The expected value
// was computed independently (PowerShell SHA256 over the lower-cased string).
func TestStopEventNameVector(t *testing.T) {
	const want = `Local\FastMediaSorterExchange-Stop-e3279b51`
	for _, p := range []string{`C:\Data\Exchange.json`, `c:\data\exchange.json`, `C:\Data\.\Exchange.json`} {
		if got := StopEventName(p); got != want {
			t.Errorf("%s: got %s, want %s", p, got, want)
		}
	}
}

func cfgFile(t *testing.T) string { return filepath.Join(t.TempDir(), "exchange.json") }

func TestRequestStopWithNothingRunningIsErrNotRunning(t *testing.T) {
	if err := RequestStop(cfgFile(t)); !errors.Is(err, ErrNotRunning) {
		t.Fatalf("got %v", err)
	}
}

func TestStopEventCancelsTheContextAndGoesAwayWithTheServer(t *testing.T) {
	cfg := cfgFile(t)
	ctx, cancel, err := NotifyStop(context.Background(), cfg)
	if err != nil {
		t.Fatal(err)
	}
	select {
	case <-ctx.Done():
		t.Fatal("cancelled before any request")
	default:
	}
	if err := RequestStop(cfg); err != nil {
		t.Fatalf("RequestStop: %v", err)
	}
	select {
	case <-ctx.Done():
	case <-time.After(5 * time.Second):
		t.Fatal("the stop event did not cancel the context")
	}
	cancel() // waits for the watcher to release the event
	if err := RequestStop(cfg); !errors.Is(err, ErrNotRunning) {
		t.Fatalf("the event must be gone once the server is down: %v", err)
	}
}

func TestStopEventsOfDifferentConfigsAreIndependent(t *testing.T) {
	a, b := cfgFile(t), cfgFile(t)
	ctxA, cancelA, _ := NotifyStop(context.Background(), a)
	ctxB, cancelB, _ := NotifyStop(context.Background(), b)
	defer cancelA()
	defer cancelB()
	if err := RequestStop(a); err != nil {
		t.Fatal(err)
	}
	select {
	case <-ctxA.Done():
	case <-time.After(5 * time.Second):
		t.Fatal("A was not stopped")
	}
	select {
	case <-ctxB.Done():
		t.Fatal("stopping A stopped B")
	case <-time.After(200 * time.Millisecond):
	}
}

func TestNotifyStopCancelFreesTheEventWithoutARequest(t *testing.T) {
	cfg := cfgFile(t)
	_, cancel, err := NotifyStop(context.Background(), cfg)
	if err != nil {
		t.Fatal(err)
	}
	cancel()
	if err := RequestStop(cfg); !errors.Is(err, ErrNotRunning) {
		t.Fatalf("got %v", err)
	}
	// a restart in the same process meets no stale signalled event
	ctx, cancel2, err := NotifyStop(context.Background(), cfg)
	if err != nil {
		t.Fatal(err)
	}
	defer cancel2()
	select {
	case <-ctx.Done():
		t.Fatal("a fresh start was cancelled by a previous run")
	case <-time.After(100 * time.Millisecond):
	}
}

func TestNotifyStopWithNoConfigIsSignalsOnly(t *testing.T) {
	ctx, cancel, err := NotifyStop(context.Background(), "")
	if err != nil {
		t.Fatal(err)
	}
	cancel()
	<-ctx.Done()
}
