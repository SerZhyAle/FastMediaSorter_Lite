// Package deploy is the ONLY OS-specific layer of the module (133 section 4): everything that touches
// Windows security descriptors, signals, named events or the service control manager lives here, so a
// Linux microservice replaces this package and never the protocol core.
//
// This file holds the parts that are the same on every platform: the names, the errors and the
// refuse-beside-the-service rule (a function over an injectable probe, so it is tested without a
// real service). The OS twins are protect_*.go, stop_*.go, service_*.go and probe_*.go.
package deploy

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"net"
	"path/filepath"
	"strconv"
	"strings"
)

// ServiceName is the frozen SCM identifier of the Server edition's exchange service (ticket 144).
// The helper, the installer and the Share Manager all name this one service.
const ServiceName = "FastMediaSorterExchangeServer"

// StopEventPrefix is the fixed part of the named event a console `serve` waits on (Windows).
const StopEventPrefix = `Local\FastMediaSorterExchange-Stop-`

// ErrNotRunning is what RequestStop returns when no console instance of that config is waiting
// for a stop request.
var ErrNotRunning = errors.New("no console server is running for this config")

// ErrStopUnsupported is what RequestStop returns where the stop request is not built yet (Linux
// uses SIGTERM until the Linux build of phase 5).
var ErrStopUnsupported = errors.New("the stop command is Windows-only for now")

// ErrNotAService is returned by RunService when the process was not started by the service control
// manager: a mistyped command line must not become a console server with the service's flags.
var ErrNotAService = errors.New("--service must be started by the Windows Service Control Manager")

// ErrServiceUnsupported is returned by RunService on a platform that has no service layer.
var ErrServiceUnsupported = errors.New("--service is only available on Windows")

// StopEventName is the name of the stop event of one config file: the prefix plus 8 lower-case hex
// characters, the first 4 bytes of SHA-256 over the lower-cased absolute path of the config. The
// Share Manager computes the same name to ask for a clean stop.
func StopEventName(configPath string) string {
	abs, err := filepath.Abs(configPath)
	if err != nil {
		abs = configPath
	}
	sum := sha256.Sum256([]byte(strings.ToLower(abs)))
	return StopEventPrefix + hex.EncodeToString(sum[:4])
}

// ServiceProbe answers the two questions the refuse-beside-the-service rule asks. The default
// (Probe) is the real SCM query on Windows and "no service" elsewhere; tests install a fake.
type ServiceProbe interface {
	// Running reports whether the service is running or on its way up.
	Running() (bool, error)
	// HoldsPort reports whether the service's own process listens on the TCP port - true while
	// the service is stopping, when its state no longer says Running but the port is still taken.
	HoldsPort(port int) (bool, error)
}

// Probe is the probe CheckBesideService is normally called with. Replace it in a test only.
var Probe ServiceProbe = defaultProbe{}

// CheckBesideService is 133 section 6.1 "the service wins": a console server must not start while
// the Media Exchange Server service runs, or while its process holds the configured listen port. It
// returns the reason to print ("" = free to start). probeErr is a failure to ask (access denied,
// no SCM): it never refuses on its own - a machine that never had the service must stay usable -
// but the caller should mention it, because the listen itself will then be the next judge.
func CheckBesideService(p ServiceProbe, listen string) (reason string, probeErr error) {
	running, err := p.Running()
	if err != nil {
		probeErr = err
	}
	if running {
		return fmt.Sprintf("the service %s is running and already serves this machine - the service wins. "+
			"Stop the service first to run a console server instead", ServiceName), probeErr
	}
	_, portStr, err := net.SplitHostPort(listen)
	if err != nil {
		return "", probeErr
	}
	port, err := strconv.Atoi(portStr)
	if err != nil || port <= 0 {
		return "", probeErr
	}
	held, err := p.HoldsPort(port)
	if err != nil && probeErr == nil {
		probeErr = err
	}
	if held {
		return fmt.Sprintf("port %d is held by the process of the service %s - the service wins. "+
			"Stop the service first to run a console server instead", port, ServiceName), probeErr
	}
	return "", probeErr
}
