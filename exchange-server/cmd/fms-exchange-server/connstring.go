package main

import (
	"errors"
	"io"
	"net"
	"os"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/tlsid"
)

// runConnectionString prints the exchange connection string (ticket 187 T-2, proposal Ask 6): the
// configured public endpoint and, when the certificate the server presents is not validated by the
// platform trust store, its pin - the one place that decision is made, so a surface that shows the
// string shows what this command prints, byte for byte.
//
// It is built from `publicEndpoint` and the real certificate file, never from a bind address or a guess:
// with no public endpoint it refuses (exit 2) and says to set the address for clients first. It reads
// and writes nothing (the identity is not created here: a server that never started has none, and that is
// exit 4 with the way to make one).
//
// Exit codes: 0 the string on stdout (one line), 2 no public endpoint or a name or login the string cannot
// carry, 4 the configuration or the certificate cannot be read.
func runConnectionString(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}
	if cfg.PublicEndpoint == "" {
		printf(stderr, "the address clients use to reach this server is not set: set it first (set-endpoint host:port)\n")
		return exitUsage
	}
	certPath := paths.Cert
	if cfg.TLS.CertFile != "" {
		certPath = cfg.TLS.CertFile
	}
	fp, err := tlsid.FingerprintOfFile(certPath)
	if err != nil {
		printf(stderr, "the server has no readable certificate yet (start the server once, or run inspect --init): %v\n", err)
		return exitConfig
	}
	host, _, err := net.SplitHostPort(cfg.PublicEndpoint)
	if err != nil {
		printf(stderr, "the configured public endpoint %q is not host:port\n", cfg.PublicEndpoint)
		return exitUsage
	}
	trusted, err := tlsid.PubliclyTrusted(certPath, host, time.Now(), nil)
	if err != nil {
		printf(stderr, "the certificate cannot be read: %v\n", err)
		return exitConfig
	}
	if trusted {
		fp = "" // a CA-signed deployment renews its leaf on the CA's schedule: no pin travels
	}
	c, err := connstr.New(cfg.PublicEndpoint, fp, cmd.label, cmd.login)
	if err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	text, err := c.String()
	if err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	printf(stdout, "%s\n", text)
	return exitOK
}
