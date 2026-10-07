//go:build !windows

package deploy

import (
	"net"
	"os"
	"path/filepath"
)

// ListenAdmin listens on the local admin channel of a data directory: a Unix socket inside it, mode
// 0600 (extraSIDs is a Windows concept and is ignored). A stale socket file of a dead server is
// replaced; the data-directory lock the caller holds proves it is dead.
func ListenAdmin(dataDir string, extraSIDs []string) (net.Listener, error) {
	path := filepath.Join(dataDir, AdminSocketName)
	_ = os.Remove(path)
	l, err := net.Listen("unix", path)
	if err != nil {
		return nil, err
	}
	if err := os.Chmod(path, 0o600); err != nil {
		_ = l.Close()
		return nil, err
	}
	return l, nil
}

// DialAdmin connects to the running server's admin channel, or returns ErrNoAdminChannel.
func DialAdmin(dataDir string) (net.Conn, error) {
	c, err := net.Dial("unix", filepath.Join(dataDir, AdminSocketName))
	if err != nil {
		return nil, ErrNoAdminChannel
	}
	return c, nil
}
