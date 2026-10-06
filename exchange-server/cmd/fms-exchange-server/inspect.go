package main

import (
	"encoding/json"
	"errors"
	"io"
	"os"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/tlsid"
)

// inspectReport is the read-only state report: no session data exists to report.
type inspectReport struct {
	DataDir           string `json:"dataDir"`
	ConfigPresent     bool   `json:"configPresent"`
	PasswordSet       bool   `json:"passwordSet"`
	PasswordHashValid bool   `json:"passwordHashValid"`
	CertPresent       bool   `json:"certPresent"`
	Fingerprint       string `json:"fingerprint"`
	Listen            string `json:"listen"`
	PortRange         string `json:"portRange"`
	Error             string `json:"error,omitempty"`
}

// runInspect prints the report as JSON, even on failure, and creates nothing - except with --init,
// which writes the commented defaults and the TLS identity if they do not exist yet.
func runInspect(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	rep := inspectReport{DataDir: paths.Dir}
	code := exitOK
	fail := func(c int, err error) {
		rep.Error = err.Error()
		code = c
	}

	cfg := config.Default()
	if cmd.init {
		var err error
		cfg, err = config.LoadOrInit(paths.Config)
		if err != nil {
			fail(exitConfig, err)
		}
	} else if c, err := config.Load(paths.Config); err == nil {
		cfg = c
	} else if !errors.Is(err, os.ErrNotExist) {
		fail(exitConfig, err)
	}
	if _, err := os.Stat(paths.Config); err == nil {
		rep.ConfigPresent = true
	}
	rep.Listen, rep.PortRange = cfg.Listen, cfg.PortRange
	rep.PasswordSet = cfg.PasswordHash != ""
	rep.PasswordHashValid = rep.PasswordSet && password.ValidRecord(cfg.PasswordHash) == nil

	certPath, keyPath := paths.Cert, paths.Key
	if cfg.TLS.CertFile != "" {
		certPath, keyPath = cfg.TLS.CertFile, cfg.TLS.KeyFile
	}
	if cmd.init && cfg.TLS.CertFile == "" {
		if id, err := tlsid.LoadOrCreate(certPath, keyPath, deploy.ProtectDir); err != nil {
			fail(exitFailure, err)
		} else {
			rep.CertPresent, rep.Fingerprint = true, id.Fingerprint
		}
	} else if fp, err := tlsid.FingerprintOfFile(certPath); err == nil {
		rep.CertPresent, rep.Fingerprint = true, fp
	} else if _, statErr := os.Stat(certPath); statErr == nil {
		rep.CertPresent = true
		fail(exitFailure, err)
	}

	enc := json.NewEncoder(stdout)
	enc.SetIndent("", "  ")
	_ = enc.Encode(rep)
	if code != exitOK {
		printf(stderr, "inspect: %s\n", rep.Error)
	}
	return code
}
