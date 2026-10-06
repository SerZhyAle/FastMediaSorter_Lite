//go:build !windows

package config

import (
	"os"
	"path/filepath"
)

// defaultDataDir on Linux: $XDG_CONFIG_HOME/fms-exchange, else ~/.config/fms-exchange. A service
// deployment (/etc/fms-exchange) arrives with the Linux microservice build (133 phase 5).
func defaultDataDir(getenv func(string) string) string {
	if x := getenv("XDG_CONFIG_HOME"); x != "" {
		return filepath.Join(x, "fms-exchange")
	}
	if h, err := os.UserHomeDir(); err == nil {
		return filepath.Join(h, ".config", "fms-exchange")
	}
	return filepath.Join(".", "fms-exchange")
}
