package main

import (
	"encoding/json"
	"errors"
	"io"
	"os"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/tlsid"
)

// inspectReport is the read-only state report: counts of what exists, never a secret, a login or an
// id. The single server password of the superseded design is gone from it (ticket 162): what the
// server needs to serve is at least one account.
type inspectReport struct {
	DataDir          string `json:"dataDir"`
	ConfigPresent    bool   `json:"configPresent"`
	Accounts         int    `json:"accounts"`
	Devices          int    `json:"devices"`
	CertPresent      bool   `json:"certPresent"`
	CertImported     bool   `json:"certImported"` // exchange.json names a certificate and key to serve instead of the generated identity
	Fingerprint      string `json:"fingerprint"`
	Listen           string `json:"listen"`
	PortRange        string `json:"portRange"`
	PublicEndpoint   string `json:"publicEndpoint"`
	Error            string `json:"error,omitempty"`
	KeepAwake        bool   `json:"keepAwake"`
	LogRetentionDays int    `json:"logRetentionDays"`
	MaxAccounts      int    `json:"maxAccounts"`
	AdminOn                     bool     `json:"adminOn"`
	AdminPasswordSet            bool     `json:"adminPasswordSet"`
	AdminAllowedAddresses       []string `json:"adminAllowedAddresses,omitempty"`
	AdminSessionLifetimeMinutes int      `json:"adminSessionLifetimeMinutes,omitempty"`
	WebDir                      string   `json:"webDir,omitempty"`
}

// runInspect prints the report as JSON, even on failure, and creates nothing - except with --init,
// which writes the commented defaults and the TLS identity if they do not exist yet. It reads the
// account files without opening the store, so it works on a directory a server is running from.
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
	rep.Listen, rep.PortRange, rep.PublicEndpoint = cfg.Listen, cfg.PortRange, cfg.PublicEndpoint
	rep.KeepAwake, rep.LogRetentionDays, rep.MaxAccounts = cfg.KeepAwake, cfg.Log.RetentionDays, cfg.Limits.MaxAccounts
	rep.AdminOn, rep.AdminPasswordSet, rep.WebDir = cfg.Admin.On, cfg.Admin.PasswordRecord != "", cfg.WebDir
	rep.AdminAllowedAddresses = cfg.Admin.AllowedAddresses
	rep.AdminSessionLifetimeMinutes = cfg.Admin.SessionLifetimeMinutes
	if a, d, err := accounts.Peek(paths.Dir); err != nil {
		fail(exitConfig, err)
	} else {
		rep.Accounts, rep.Devices = a, d
	}

	certPath, keyPath := paths.Cert, paths.Key
	if cfg.TLS.CertFile != "" {
		certPath, keyPath = cfg.TLS.CertFile, cfg.TLS.KeyFile
		rep.CertImported = true
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
