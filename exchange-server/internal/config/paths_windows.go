package config

import (
	"os"
	"path/filepath"
)

const dirName = "FastMediaSorterExchange"

// defaultDataDir follows the worker's DataDir order: the service's machine store under
// %ProgramData% when it exists and opens, then the interactive per-user store, then the user
// config directory.
func defaultDataDir(getenv func(string) string) string {
	if pd := getenv("ProgramData"); pd != "" {
		d := filepath.Join(pd, dirName)
		if st, err := os.Stat(d); err == nil && st.IsDir() {
			if f, err := os.Open(d); err == nil {
				_ = f.Close()
				return d
			}
		}
	}
	if la := getenv("LOCALAPPDATA"); la != "" {
		return filepath.Join(la, dirName)
	}
	if uc, err := os.UserConfigDir(); err == nil {
		return filepath.Join(uc, dirName)
	}
	return filepath.Join(".", dirName)
}
