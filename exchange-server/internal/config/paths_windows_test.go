package config

import (
	"os"
	"path/filepath"
	"testing"
)

func TestWindowsDefaultOrder(t *testing.T) {
	pd, la := t.TempDir(), t.TempDir()
	env := map[string]string{"ProgramData": pd, "LOCALAPPDATA": la}
	get := func(k string) string { return env[k] }

	// No machine store yet: the per-user store.
	if got := defaultDataDir(get); got != filepath.Join(la, dirName) {
		t.Fatalf("without a machine store: %s", got)
	}
	// Once the service's store exists and opens, it wins (the worker's DataDir order).
	if err := os.Mkdir(filepath.Join(pd, dirName), 0o700); err != nil {
		t.Fatal(err)
	}
	if got := defaultDataDir(get); got != filepath.Join(pd, dirName) {
		t.Fatalf("with a machine store: %s", got)
	}
}
