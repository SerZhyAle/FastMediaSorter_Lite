//go:build windows

package deploy

import (
	"fmt"
	"golang.org/x/sys/windows"
	"runtime"
)

var executionState = windows.NewLazySystemDLL("kernel32.dll").NewProc("SetThreadExecutionState")

func KeepAwake(enabled bool) (func(), bool, error) {
	return keepAwake(enabled, func(on bool) error {
		flags := uintptr(0x80000000) // ES_CONTINUOUS; never ES_DISPLAY_REQUIRED
		if on {
			flags |= 1
		} // ES_SYSTEM_REQUIRED
		result, _, err := executionState.Call(flags)
		if result == 0 {
			return fmt.Errorf("execution-state request failed: %w", err)
		}
		return nil
	}, runtime.LockOSThread, runtime.UnlockOSThread)
}
