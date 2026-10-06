package deploy

import (
	"context"
	"errors"
	"testing"
	"time"

	"golang.org/x/sys/windows/svc"
)

func fastTimings(t *testing.T, grace, stopWait time.Duration) {
	t.Helper()
	g, s := startGrace, stopWaitTimeout
	startGrace, stopWaitTimeout = grace, stopWait
	t.Cleanup(func() { startGrace, stopWaitTimeout = g, s })
}

// drive runs Execute like the SCM would and returns its verdict and every state it reported.
func drive(t *testing.T, run func(context.Context) int, send ...svc.ChangeRequest) (specific bool, code uint32, states []svc.State, logged []string) {
	t.Helper()
	requests := make(chan svc.ChangeRequest, len(send))
	for _, r := range send {
		requests <- r
	}
	status := make(chan svc.Status, 16)
	type verdict struct {
		specific bool
		code     uint32
	}
	out := make(chan verdict, 1)
	h := &scmHandler{run: run, logf: func(f string, a ...any) { logged = append(logged, f) }}
	go func() {
		sp, c := h.Execute(nil, requests, status)
		out <- verdict{sp, c}
	}()
	select {
	case v := <-out:
		close(status)
		for s := range status {
			states = append(states, s.State)
		}
		return v.specific, v.code, states, logged
	case <-time.After(10 * time.Second):
		t.Fatal("Execute did not return")
		return
	}
}

func TestServiceStopCancelsAndExitsCleanly(t *testing.T) {
	fastTimings(t, 10*time.Millisecond, 5*time.Second)
	run := func(ctx context.Context) int { <-ctx.Done(); return 0 }
	specific, code, states, _ := drive(t, run, svc.ChangeRequest{Cmd: svc.Stop})
	if specific || code != 0 {
		t.Fatalf("a clean stop is not a failure: specific=%v code=%d", specific, code)
	}
	want := []svc.State{svc.StartPending, svc.Running, svc.StopPending}
	if len(states) != len(want) {
		t.Fatalf("states %v, want %v", states, want)
	}
	for i := range want {
		if states[i] != want[i] {
			t.Fatalf("states %v, want %v", states, want)
		}
	}
}

func TestServiceShutdownIsTreatedLikeStop(t *testing.T) {
	fastTimings(t, 10*time.Millisecond, 5*time.Second)
	run := func(ctx context.Context) int { <-ctx.Done(); return 0 }
	if specific, code, _, _ := drive(t, run, svc.ChangeRequest{Cmd: svc.Shutdown}); specific || code != 0 {
		t.Fatalf("shutdown: specific=%v code=%d", specific, code)
	}
}

func TestServiceThatDiesAtStartFailsTheStartWithItsOwnCode(t *testing.T) {
	fastTimings(t, 5*time.Second, 5*time.Second)
	specific, code, states, logged := drive(t, func(context.Context) int { return 3 })
	if !specific || code != 3 {
		t.Fatalf("a start failure must be reported verbatim: specific=%v code=%d", specific, code)
	}
	for _, s := range states {
		if s == svc.Running {
			t.Fatal("reported Running although the server died at once")
		}
	}
	if len(logged) == 0 {
		t.Fatal("nothing logged")
	}
}

func TestServiceThatEndsOnItsOwnExitsNonZeroSoRecoveryRestartsIt(t *testing.T) {
	fastTimings(t, 10*time.Millisecond, 5*time.Second)
	release := make(chan struct{})
	run := func(ctx context.Context) int { <-release; return 0 }
	go func() { time.Sleep(100 * time.Millisecond); close(release) }()
	specific, code, _, _ := drive(t, run)
	if !specific || code != ExitEndedUnexpectedly {
		t.Fatalf("a self-exit with 0 must still be a failure: specific=%v code=%d", specific, code)
	}

	release2 := make(chan struct{})
	run2 := func(ctx context.Context) int { <-release2; return 1 }
	go func() { time.Sleep(100 * time.Millisecond); close(release2) }()
	if specific, code, _, _ := drive(t, run2); !specific || code != 1 {
		t.Fatalf("a self-exit with 1 is reported as 1: specific=%v code=%d", specific, code)
	}
}

func TestServiceStopThatNeverFinishesReportsATimeout(t *testing.T) {
	fastTimings(t, 10*time.Millisecond, 100*time.Millisecond)
	block := make(chan struct{})
	t.Cleanup(func() { close(block) })
	run := func(context.Context) int { <-block; return 0 }
	specific, code, _, _ := drive(t, run, svc.ChangeRequest{Cmd: svc.Stop})
	if !specific || code != ExitStopTimeout {
		t.Fatalf("specific=%v code=%d", specific, code)
	}
}

func TestServiceStopThatEndsWithAnErrorCodeIsAFailure(t *testing.T) {
	fastTimings(t, 10*time.Millisecond, 5*time.Second)
	run := func(ctx context.Context) int { <-ctx.Done(); return 1 }
	if specific, code, _, _ := drive(t, run, svc.ChangeRequest{Cmd: svc.Stop}); !specific || code != 1 {
		t.Fatalf("specific=%v code=%d", specific, code)
	}
}

func TestRunServiceFromAConsoleIsRefused(t *testing.T) {
	err := RunService(func(context.Context) int { return 0 }, nil)
	if !errors.Is(err, ErrNotAService) {
		t.Fatalf("got %v", err)
	}
}
