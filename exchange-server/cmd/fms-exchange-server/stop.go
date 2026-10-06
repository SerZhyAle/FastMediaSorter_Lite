package main

import (
	"errors"
	"io"
	"os"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
)

// runStop is `stop`: it asks the console server running for that config to stop cleanly (the same
// path as Ctrl+C: registrations closed, the `stop` log line written). It does not wait for the
// server to finish and it does not touch a service - the service is stopped through the SCM. Exit 0
// once the request is delivered, 1 when nothing is running for the config or the request cannot be
// made on this platform.
func runStop(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	switch err := deploy.RequestStop(paths.Config); {
	case err == nil:
		printf(stdout, "stop requested for %s\n", paths.Config)
		return exitOK
	case errors.Is(err, deploy.ErrNotRunning):
		printf(stderr, "no console server is running for %s\n", paths.Config)
		return exitFailure
	case errors.Is(err, deploy.ErrStopUnsupported):
		printf(stderr, "stop is Windows-only for now (use SIGTERM on Linux; the Linux build is a later phase)\n")
		return exitFailure
	default:
		printf(stderr, "cannot request a stop: %v\n", err)
		return exitFailure
	}
}
