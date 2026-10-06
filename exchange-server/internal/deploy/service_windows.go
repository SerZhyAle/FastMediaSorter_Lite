package deploy

import (
	"context"
	"fmt"
	"time"

	"golang.org/x/sys/windows/svc"
)

// ServiceSupported tells the command line whether --service can work on this platform.
const ServiceSupported = true

// Service-specific exit codes the handler reports (svcSpecificEC=true), so SCM recovery restarts
// a service that died while the SCM still thought it running. A run function that returns a
// non-zero code on its own has that code reported verbatim (the command's own 1..5); these two
// cover the cases that have no code of their own.
const (
	ExitEndedUnexpectedly = 10 // the run function returned 0 although nobody asked it to stop
	ExitStopTimeout       = 11 // the clean shutdown did not finish within stopWaitTimeout
)

// stopWaitTimeout bounds the wait for the server's own clean shutdown after a stop request;
// startGrace is how long the run function has to fail outright before the service says Running.
// Both are variables only so the tests can shorten them.
var (
	stopWaitTimeout = 25 * time.Second
	startGrace      = 500 * time.Millisecond
)

// InWindowsService reports whether this process was started by the service control manager.
func InWindowsService() (bool, error) { return svc.IsWindowsService() }

// RunService hands the process to the SCM dispatcher and runs `run` until the service is stopped.
// run gets a context that Stop and Shutdown cancel, and returns the process-style exit code
// (0 = clean). logf is a best-effort diagnostic line sink (session 0 has no console).
// It refuses to run outside the SCM with ErrNotAService.
func RunService(run func(context.Context) int, logf func(format string, args ...any)) error {
	in, err := svc.IsWindowsService()
	if err != nil {
		return fmt.Errorf("detect service context: %w", err)
	}
	if !in {
		return ErrNotAService
	}
	if err := svc.Run(ServiceName, &scmHandler{run: run, logf: logf}); err != nil {
		return fmt.Errorf("run service %s: %w", ServiceName, err)
	}
	return nil
}

// scmHandler adapts a run function to svc.Handler - the Share worker's shape: StartPending, a
// short grace so a run that dies at once fails the start instead of reporting Running and
// vanishing, Running, then Stop/Shutdown cancel the context and the handler waits for the clean
// shutdown. A run that ends on its own while no stop was requested terminates the service with a
// non-zero specific exit code: only a dead process lets SCM recovery restart it.
type scmHandler struct {
	run  func(context.Context) int
	logf func(format string, args ...any)
}

func (h *scmHandler) log(format string, args ...any) {
	if h.logf != nil {
		h.logf(format, args...)
	}
}

func exitCodeOr(code int, fallback uint32) uint32 {
	if code > 0 {
		return uint32(code)
	}
	return fallback
}

// Execute is the SCM state machine.
func (h *scmHandler) Execute(_ []string, r <-chan svc.ChangeRequest, s chan<- svc.Status) (bool, uint32) {
	s <- svc.Status{State: svc.StartPending}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	done := make(chan int, 1)
	go func() { done <- h.run(ctx) }()

	select {
	case code := <-done:
		s <- svc.Status{State: svc.StopPending}
		h.log("start failed: the server ended with code %d", code)
		return true, exitCodeOr(code, ExitEndedUnexpectedly)
	case <-time.After(startGrace):
	}

	s <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}

	for {
		select {
		case c := <-r:
			switch c.Cmd {
			case svc.Interrogate:
				s <- c.CurrentStatus
			case svc.Stop, svc.Shutdown:
				s <- svc.Status{State: svc.StopPending}
				cancel()
				select {
				case code := <-done:
					if code != 0 {
						h.log("stopped with code %d", code)
						return true, exitCodeOr(code, ExitEndedUnexpectedly)
					}
					return false, 0
				case <-time.After(stopWaitTimeout):
					h.log("the server did not shut down within %s", stopWaitTimeout)
					return true, ExitStopTimeout
				}
			default:
				// no other control code is accepted
			}
		case code := <-done:
			s <- svc.Status{State: svc.StopPending}
			h.log("the server ended unexpectedly with code %d", code)
			return true, exitCodeOr(code, ExitEndedUnexpectedly)
		}
	}
}
