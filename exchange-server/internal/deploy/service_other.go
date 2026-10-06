//go:build !windows

package deploy

import "context"

// ServiceSupported tells the command line whether --service can ever work on this platform.
const ServiceSupported = false

// InWindowsService is always false off Windows.
func InWindowsService() (bool, error) { return false, nil }

// RunService is refused off Windows.
func RunService(run func(context.Context) int, logf func(format string, args ...any)) error {
	return ErrServiceUnsupported
}

// ServiceRunning is always (false, nil) off Windows: there is no service to be running.
func ServiceRunning() (bool, error) { return false, nil }

// defaultProbe is the "no service here" probe of non-Windows builds.
type defaultProbe struct{}

func (defaultProbe) Running() (bool, error)      { return false, nil }
func (defaultProbe) HoldsPort(int) (bool, error) { return false, nil }
