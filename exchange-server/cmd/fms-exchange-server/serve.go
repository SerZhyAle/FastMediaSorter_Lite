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

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/tlsid"
)

// runServe runs the exchange server until ctx ends. With --service it hands the process to the
// Windows service control manager instead and runs the same server under it.
func runServe(ctx context.Context, cmd command, stdout, stderr io.Writer) int {
	if cmd.service {
		return runServeAsService(cmd, stderr)
	}
	return serveCore(ctx, cmd, true, stdout, stderr)
}

// runServeAsService is `serve --service`: refused from a console (and off Windows), otherwise the
// server runs under svc.Run. Session 0 has no console, so what a console run would print goes to
// <data dir>\logs\service.log, written only when there is something to say.
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
	sl := &serviceLog{path: paths.ServiceLog}
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
	// The installer helper creates logs\ with its ACL (ticket 182 L-1); a console run or a hand-made
	// directory gets one here, best effort. An existing directory is never touched.
	_ = os.MkdirAll(filepath.Dir(l.path), 0o700)
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

// noAccountMessage is what a serve that has no account says: the old "never password-less" is now
// "never accountless" (ticket 161 Q161-1), and the operator is told how to add one.
const noAccountMessage = "no account exists, so there is nobody the server could serve. Add one first: " +
	"fms-exchange-server account add <login> --generate\n"

// serveCore is the server itself. console is true for a console run: only then does it refuse to
// start beside the service (133 section 6.1) and listen for the stop event. It refuses to start
// with no account (Q161-1), never regenerates a TLS identity, and holds the data directory's lock for
// its whole life, so a second server (or an offline `account` command) cannot edit the stores under it.
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

	release, err := deploy.LockDir(paths.Dir)
	if err != nil {
		if errors.Is(err, deploy.ErrLocked) {
			printf(stderr, "not starting: another server already holds %s\n", paths.Dir)
		} else {
			printf(stderr, "cannot lock the data directory %s: %v\n", paths.Dir, err)
		}
		return exitFailure
	}
	defer release()

	store, err := accounts.Open(accounts.Options{Dir: paths.Dir, DevicesPerAccount: cfg.Limits.DevicesPerAccount,
		CodesPerAccount: cfg.Limits.CodesPerAccount})
	if err != nil {
		printf(stderr, "cannot open the accounts: %v\n", err)
		return exitConfig
	}
	if !store.HasAccounts() {
		printf(stderr, "%s", noAccountMessage)
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

	_ = os.MkdirAll(paths.LogsDir, 0o700) // see serviceLog.Write: the helper normally made it already
	logger, err := logx.Open(paths.Log, cfg.Log.MaxBytes)
	if err != nil {
		printf(stderr, "cannot open the log %s: %v\n", paths.Log, err)
		return exitConfig
	}
	defer logger.Close()
	logger.Info("start", logx.Text("version", version))
	if cfg.ObsoletePasswordHash {
		logger.Warn("obsolete", logx.Text("member", "the server-wide credential of the superseded design is ignored; it is removed when the config is next saved"))
	}

	// the directory: the enrolled devices (all offline), the resource records of those that remain
	var infos []directory.DeviceInfo
	for _, d := range store.AllDevices() {
		infos = append(infos, directory.DeviceInfo{ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product,
			ProductVersion: d.ProductVersion, Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver})
	}
	l := cfg.Limits
	dir := directory.New(directory.Options{Dir: paths.Dir,
		Caps: directory.Caps{ResourcesPerAccount: l.ResourcesPerAccount, BroadcastsPerAccount: l.BroadcastsPerAccount, BroadcastsPerDevice: l.BroadcastsPerDevice}})
	if err := dir.Load(infos); err != nil {
		printf(stderr, "cannot open the directory: %v\n", err)
		return exitConfig
	}

	host, _, _ := net.SplitHostPort(cfg.Listen)
	lo, hi, _ := config.ParsePortRange(cfg.PortRange)
	pool := ports.NewPool(lo, hi, host, nil, nil)

	rate := func(r config.Rate) limits.Rate { return limits.Rate{PerMinute: r.PerMinute, Burst: r.Burst} }
	lc := limits.Config{MaxAddrs: 4096}
	lc.Rates[limits.Connection] = rate(l.Connection)
	lc.Rates[limits.UnknownID] = rate(l.UnknownID)
	lc.Rates[limits.FailedAuth] = rate(l.FailedAuth)
	lc.Rates[limits.AttachMiss] = rate(l.AttachMiss)
	llc := limits.Config{MaxAddrs: 4096}
	llc.Rates[limits.FailedEnroll] = rate(l.FailedEnroll)
	llc.Rates[limits.FailedHello] = rate(l.FailedHello)
	llc.Rates[limits.PairingRequest] = rate(l.PairingRequest)
	llc.Rates[limits.BroadcastStart] = rate(l.BroadcastStart)
	llc.Rates[limits.Cast] = rate(l.Cast)
	limiter, loginLimiter := limits.NewLimiter(lc, nil), limits.NewLimiter(llc, nil)

	params := rendezvous.DefaultParams()
	params.KeepaliveDefault, params.KeepaliveMin = cfg.Keepalive.DefaultSeconds, cfg.Keepalive.MinSeconds
	params.KeepaliveMax, params.WindowMultiplier = cfg.Keepalive.MaxSeconds, cfg.Keepalive.WindowMultiplier
	params.TunnelCap, params.MaxConns = l.TunnelsPerResource, l.MaxConnections
	params.DevicesPerAccount, params.ResourcesPerAccount = l.DevicesPerAccount, l.ResourcesPerAccount
	params.BroadcastsPerAccount, params.BroadcastsPerDevice, params.CodesPerAccount = l.BroadcastsPerAccount, l.BroadcastsPerDevice, l.CodesPerAccount
	params.CastPerAccount = l.CastPendingPerAccount

	// the relay of ticket 165: it shares the engine's per-address limiter and the directory
	relayHandler := rendezvous.NewRelay(rendezvous.RelayConfig{Dir: dir, Log: logger, Limiter: limiter,
		MaxUploadBitsPerSec: int64(l.RelayUploadMbit) * 1_000_000,
		ListenersPerBcast:   l.RelayListenersPerBcast, ListenersTotal: l.RelayListenersTotal})
	engine := rendezvous.NewEngine(rendezvous.EngineConfig{
		Params: params, Accounts: store, Dir: dir, Pool: pool,
		Verifier: limits.NewVerifier(nil, l.VerifierConcurrency, 2*time.Second),
		Limiter:  limiter, LoginLimiter: loginLimiter, Log: logger,
		PublicEndpoint: cfg.PublicEndpoint, ServerVersion: version,
		Features: rendezvous.Features{Cast: true}, Relay: relayHandler,
	})
	srv := rendezvous.New(rendezvous.Config{
		Params:  params,
		TLS:     &tls.Config{Certificates: []tls.Certificate{id.Cert}, MinVersion: tls.VersionTLS12},
		Limiter: limiter, PreAuth: limits.NewPreAuth(l.PreAuthSlots), Backend: engine, Log: logger,
	})

	ln, err := net.Listen("tcp", cfg.Listen)
	if err != nil {
		printf(stderr, "cannot listen on %s: %v\n", cfg.Listen, err)
		logger.Error("listen-failed", logx.Text("listen", cfg.Listen))
		return exitFailure
	}

	// the local admin channel: how the Share Manager and the account commands reach a running server
	adminLn, err := deploy.ListenAdmin(paths.Dir, cfg.Admin.ManageSIDs)
	if err != nil {
		printf(stderr, "warning: the admin channel is not available (%v); accounts can only be changed with the server stopped\n", err)
		logger.Warn("admin-channel-unavailable", logx.Text("error", err.Error()))
	} else {
		go admin.Serve(adminLn, engine.Ops(), logger)
		defer adminLn.Close()
	}

	printf(stdout, "fms-exchange-server %s\n", version)
	printf(stdout, "listening on %s\n", ln.Addr())
	printf(stdout, "port range %s\n", cfg.PortRange)
	if cfg.PublicEndpoint != "" {
		printf(stdout, "public endpoint %s\n", cfg.PublicEndpoint)
	} else {
		printf(stdout, "public endpoint not set: tunnels and the relay are hidden from clients until it is (fms-exchange-server set-endpoint)\n")
	}
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
