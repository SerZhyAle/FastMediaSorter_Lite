//go:build !windows

package deploy

// Non-Windows hosts do not change the host's power policy.
func KeepAwake(enabled bool) (func(), bool, error) { return func() {}, false, nil }
