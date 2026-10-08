package main

import (
	"errors"
	"io"
	"os"
	"path/filepath"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/tlsid"
)

// The certificate commands (ticket 168 section 3.4). Both change what the NEXT start serves: a running
// server holds its identity in memory, and the caller restarts it.
//
//	tls replace                      a new self-signed identity (the old pair is gone for good, so every
//	                                 device that pinned it refuses the server until its user pins the new
//	                                 fingerprint); any imported pair is dropped from the config.
//	tls import --cert F --key F      serve a certificate the operator got elsewhere (a CA-signed one for a
//	                                 hosted deployment). The pair is validated first - parses, the key
//	                                 matches, inside its validity - copied into the data directory's tls
//	                                 folder with the module's access list, and exchange.json names the copy.
//
// On success the new fingerprint is printed on stdout as `fingerprint SHA256:...`. A refusal exits 2 and
// writes ONE line `tls-refused: <class>` on stderr (unreadable, mismatch, expired, not-yet-valid) so a
// caller can say it in its own words; no key material is ever printed.

const (
	importedCertName = "imported-cert.pem"
	importedKeyName  = "imported-key.pem"
)

func runTLS(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}

	var fp string
	switch cmd.sub {
	case "import":
		dstCert := filepath.Join(paths.TLSDir, importedCertName)
		dstKey := filepath.Join(paths.TLSDir, importedKeyName)
		id, err := tlsid.Import(cmd.cert, cmd.key, dstCert, dstKey, deploy.ProtectDir, time.Now())
		if err != nil {
			return tlsRefusal(err, stderr)
		}
		cfg.TLS.CertFile, cfg.TLS.KeyFile = dstCert, dstKey
		fp = id.Fingerprint
	case "replace":
		id, err := tlsid.Replace(paths.Cert, paths.Key, deploy.ProtectDir)
		if err != nil {
			printf(stderr, "cannot make a new identity: %v\n", err)
			return exitFailure
		}
		// an imported pair is no longer named, and its private-key copy does not stay behind
		if cfg.TLS.KeyFile != "" && filepath.Base(cfg.TLS.KeyFile) == importedKeyName && filepath.Dir(cfg.TLS.KeyFile) == paths.TLSDir {
			_ = os.Remove(cfg.TLS.KeyFile)
			_ = os.Remove(cfg.TLS.CertFile)
		}
		cfg.TLS.CertFile, cfg.TLS.KeyFile = "", ""
		fp = id.Fingerprint
	}
	if err := cfg.Validate(); err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write the config: %v\n", err)
		return exitConfig
	}
	printf(stdout, "fingerprint %s\n", fp)
	return exitOK
}

// tlsRefusal maps a refused pair onto the one stable stderr line and the usage exit code.
func tlsRefusal(err error, stderr io.Writer) int {
	class := ""
	switch {
	case errors.Is(err, tlsid.ErrMismatch):
		class = "mismatch"
	case errors.Is(err, tlsid.ErrExpired):
		class = "expired"
	case errors.Is(err, tlsid.ErrNotYetValid):
		class = "not-yet-valid"
	case errors.Is(err, tlsid.ErrUnreadable):
		class = "unreadable"
	}
	if class == "" {
		printf(stderr, "cannot import the certificate: %v\n", err)
		return exitFailure
	}
	printf(stderr, "tls-refused: %s\n", class)
	return exitUsage
}
