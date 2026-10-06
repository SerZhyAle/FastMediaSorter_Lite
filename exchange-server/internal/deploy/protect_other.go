//go:build !windows

package deploy

import "os"

// ProtectDir restricts dir to its owner (0700); files made inside are created 0600.
func ProtectDir(dir string) error { return os.Chmod(dir, 0o700) }
