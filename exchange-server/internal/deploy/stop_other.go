//go:build !windows

package deploy

import (
	"context"
	"os"
	"os/signal"
	"syscall"
)

// NotifyStop returns a context cancelled by Ctrl+C or SIGTERM. configPath is accepted for
// symmetry with the Windows build, where it names the stop event; here SIGTERM is the stop request,
// so the returned error is always nil.
func NotifyStop(parent context.Context, configPath string) (context.Context, context.CancelFunc, error) {
	ctx, cancel := signal.NotifyContext(parent, os.Interrupt, syscall.SIGTERM)
	return ctx, cancel, nil
}

// RequestStop is Windows-only for now (the Linux build is phase 5 of 133): use SIGTERM.
func RequestStop(configPath string) error { return ErrStopUnsupported }
