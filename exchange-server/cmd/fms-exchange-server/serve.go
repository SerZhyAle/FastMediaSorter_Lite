package main

import (
	"context"
	"crypto/tls"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/tlsid"
)

// runServe runs the rendezvous and relay until ctx ends. With --service it hands the process to the
// Windows service control manager instead and runs the same server under it.
func runServe(ctx context.Context, cmd command, stdout, stderr io.Writer) int {
	if cmd.service {
		return runServeAsService(cmd, stderr)
	}
	return serveCore(ctx, cmd, true, stdout, stderr)
}

// runServeAsService is `serve --service`: refused from a console (and off Windows), otherwise the
// server runs under svc.Run. Session 0 has no console, so what a console run would print goes to
// <data dir>\service.log, written only when there is something to say.
func runServeAsService(cmd command, stderr io.Writer) int {
	in, err := deploy.InWindowsService()
	if err != nil {
		printf(stderr, "cannot tell whether this is a service: %v\n", err)
		return exitFailure
	}
	if !in {
		if !deploy.ServiceSupported {
			printf(stderr, "--service is only available on Windows; run `fms-exchange-server serve` for a console server\n")
		} else {
			printf(stderr, "--service must be started by the Windows Service Control Manager, not from a console; "+
				"run `fms-exchange-server serve` for a console server\n")
		}
		return exitUsage
	}
	paths := config.Resolve(cmd.config, os.Getenv)
	sl := &serviceLog{path: filepath.Join(paths.Dir, "service.log")}
	err = deploy.RunService(func(ctx context.Context) int {
		return serveCore(ctx, cmd, false, io.Discard, sl)
	}, sl.logf)
	if err != nil {
		printf(stderr, "%v\n", err)
		sl.logf("%v", err)
		return exitFailure
	}
	return exitOK
}

// serviceLog appends lines to service.log, best effort: a failure to log never changes an outcome.
// The file is created on the first line, never before.
type serviceLog struct{ path string }

func (l *serviceLog) Write(p []byte) (int, error) {
	f, err := os.OpenFile(l.path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600)
	if err != nil {
		return len(p), nil
	}
	defer f.Close()
	_, _ = f.WriteString(time.Now().UTC().Format(time.RFC3339) + " ")
	_, _ = f.Write(p)
	return len(p), nil
}

func (l *serviceLog) logf(format string, a ...any) {
	_, _ = l.Write([]byte(fmt.Sprintf(format, a...) + "\n"))
}

// serveCore is the server itself. console is true for a console run: only then does it refuse to
// start beside the service (133 section 6.1) and listen for the stop event. It refuses to start
// without a usable password (133 section 5: never password-less) and never regenerates a TLS
// identity.
func serveCore(ctx context.Context, cmd command, console bool, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.LoadOrInit(paths.Config)
	if err != nil {
		printf(stderr, "cannot read the config %s: %v\n", paths.Config, err)
		return exitConfig
	}
	if err := cfg.Validate(); err != nil {
		printf(stderr, "the config is not valid: %v\n", err)
		return exitConfig
	}
	if console {
		reason, probeErr := deploy.CheckBesideService(deploy.Probe, cfg.Listen)
		if reason != "" {
			printf(stderr, "not starting: %s\n", reason)
			return exitService
		}
		if probeErr != nil {
			printf(stderr, "warning: cannot tell whether the service is running (%v); continuing\n", probeErr)
		}
	}
	if err := cfg.CheckPassword(); err != nil {
		if errors.Is(err, config.ErrNoPassword) {
			printf(stderr, "no server password is set. Set one first: fms-exchange-server set-password --generate\n")
		} else {
			printf(stderr, "the stored password record is malformed. Set a new one: fms-exchange-server set-password --generate\n")
		}
		return exitPolicy
	}

	if console {
		// Created here so a stop that arrives while the identity is still being loaded is honoured:
		// the context is already cancelled when the select below is reached.
		var stopErr error
		var cancel context.CancelFunc
		ctx, cancel, stopErr = deploy.NotifyStop(ctx, paths.Config)
		defer cancel()
		if stopErr != nil {
			printf(stderr, "warning: `fms-exchange-server stop` will not reach this server (%v); Ctrl+C still works\n", stopErr)
		}
	}

	certPath, keyPath := paths.Cert, paths.Key
	var id *tlsid.Identity
	if cfg.TLS.CertFile != "" {
		id, err = tlsid.Load(cfg.TLS.CertFile, cfg.TLS.KeyFile)
	} else {
		id, err = tlsid.LoadOrCreate(certPath, keyPath, deploy.ProtectDir)
	}
	if err != nil {
		printf(stderr, "TLS identity: %v\n", err)
		return exitFailure
	}

	logger, err := logx.Open(paths.Log, cfg.Log.MaxBytes)
	if err != nil {
		printf(stderr, "cannot open the log %s: %v\n", paths.Log, err)
		return exitConfig
	}
	defer logger.Close()
	logger.Info("start", logx.Text("version", version))

	host, _, _ := net.SplitHostPort(cfg.Listen)
	lo, hi, _ := config.ParsePortRange(cfg.PortRange)
	pool := ports.NewPool(lo, hi, host, nil, nil)

	l := cfg.Limits
	lc := limits.Config{MaxAddrs: 4096}
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: l.Connection.PerMinute, Burst: l.Connection.Burst}
	lc.Rates[limits.UnknownID] = limits.Rate{PerMinute: l.UnknownID.PerMinute, Burst: l.UnknownID.Burst}
	lc.Rates[limits.FailedRegister] = limits.Rate{PerMinute: l.FailedRegister.PerMinute, Burst: l.FailedRegister.Burst}
	lc.Rates[limits.AttachMiss] = limits.Rate{PerMinute: l.AttachMiss.PerMinute, Burst: l.AttachMiss.Burst}

	record := cfg.PasswordHash
	params := rendezvous.DefaultParams()
	params.KeepaliveDefault, params.KeepaliveMin = cfg.Keepalive.DefaultSeconds, cfg.Keepalive.MinSeconds
	params.KeepaliveMax, params.WindowMultiplier = cfg.Keepalive.MaxSeconds, cfg.Keepalive.WindowMultiplier
	params.TunnelCap, params.MaxRegistrations, params.MaxConns = l.TunnelsPerShare, l.MaxRegistrations, l.MaxConnections

	srv := rendezvous.New(rendezvous.Config{
		Params: params,
		TLS:    &tls.Config{Certificates: []tls.Certificate{id.Cert}, MinVersion: tls.VersionTLS12},
		Verifier: limits.NewVerifier(func(p string) bool { return password.Verify(record, p) },
			l.VerifierConcurrency, 2*time.Second),
		Limiter: limits.NewLimiter(lc, nil),
		PreAuth: limits.NewPreAuth(l.PreAuthSlots),
		Pool:    pool,
		Log:     logger,
	})

	ln, err := net.Listen("tcp", cfg.Listen)
	if err != nil {
		printf(stderr, "cannot listen on %s: %v\n", cfg.Listen, err)
		logger.Error("listen-failed", logx.Text("listen", cfg.Listen))
		return exitFailure
	}
	printf(stdout, "fms-exchange-server %s\n", version)
	printf(stdout, "listening on %s\n", ln.Addr())
	printf(stdout, "port range %s\n", cfg.PortRange)
	printf(stdout, "tls fingerprint %s\n", id.Fingerprint)
	logger.Info("listening", logx.Text("addr", ln.Addr().String()), logx.Text("fingerprint", id.Fingerprint))

	done := make(chan error, 1)
	go func() { done <- srv.Serve(ln) }()
	select {
	case <-ctx.Done():
		srv.Close()
		<-done
		logger.Info("stop")
		return exitOK
	case err := <-done:
		if err != nil {
			printf(stderr, "serve: %v\n", err)
			logger.Error("serve-failed", logx.Text("error", err.Error()))
			return exitFailure
		}
		return exitOK
	}
}
