package testkit

import (
	"crypto/tls"
	"io"
	"net"
	"os"
	"path/filepath"
	"sync"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/tlsid"
)

// TestPassword is the password of every account a started server is given.
const TestPassword = "Passw0rdTest"

// cheap Argon2id so a test run does not spend seconds on the hash.
var cheap = password.Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32}

// Cheap is the Argon2id cost tests use.
func Cheap() password.Params { return cheap }

// Options tune StartServer; a zero field keeps the test default.
type Options struct {
	Params *rendezvous.Params
	Limits *limits.Config
	// LoginLimits are the rates of the per-login, per-account and per-device buckets.
	LoginLimits *limits.Config
	Caps        *directory.Caps
	PortRange   [2]int // zero: no pool
	VerifyConc  int
	VerifyWait  time.Duration
	PreAuthSlot int
	Now         func() time.Time
	// Logins are the accounts created, all with TestPassword (default: alice).
	Logins         []string
	PublicEndpoint string
	// DataDir reuses an existing data directory (and so its TLS identity, accounts and directory):
	// a restart.
	DataDir string
	// Relay plugs a custom RelayHandler; WithRelay builds the real one (ticket 165) over the started
	// server, with test-sized timers, and RelayConfig adjusts it.
	Relay       rendezvous.RelayHandler
	WithRelay   bool
	RelayConfig func(*rendezvous.RelayConfig)
	Cast        bool
	// LogFile also writes every log line to <DataDir>/logs/exchange.log (the real layout of ticket 182
	// L-1), so a test can scan the directory the way the manifest test of ticket 167 does.
	LogFile bool
	// Pool replaces the port pool PortRange would build (the vector generator gives it a pool whose
	// binds are not real sockets, so a granted port is the same on every machine).
	Pool *ports.Pool
	// AccountsRand and EngineRand are injected random sources for tokens and pairing codes, and for
	// tunnel ids (ticket 205: stable bytes). ServerVersion is announced in `welcome` (default "test").
	AccountsRand  io.Reader
	EngineRand    io.Reader
	ServerVersion string
	// PaceInterval is the time between two password checks of one login or address (19 item AW). Zero is
	// pacing OFF - the many tests that sign in again and again from one address are not about the pace; a
	// test that is sets it, and Sleep replaces time.Sleep in the pacer (with Now, a fully injected clock).
	PaceInterval time.Duration
	Sleep        func(time.Duration)
}

// Env is a running server with everything a test may look at.
type Env struct {
	Server       *rendezvous.Server
	Engine       *rendezvous.Engine
	Accounts     *accounts.Store
	Dir          *directory.Dir
	Ops          *admin.Ops
	Addr         string
	Host         string
	Fingerprint  string
	DataDir      string
	Log          *logx.Capture
	Limiter      *limits.Limiter
	LoginLimiter *limits.Limiter
	Verifier     *limits.Verifier
	Pool         *ports.Pool
	Params       rendezvous.Params
	// Relay is the real relay when Options.WithRelay was set.
	Relay *rendezvous.Relay

	secretMu sync.Mutex
	secrets  []string
	t        TB
}

// TestParams are defaults tuned for tests: short delays and windows.
func TestParams() rendezvous.Params {
	p := rendezvous.DefaultParams()
	p.PendingWindow = 2 * time.Second
	p.UnavailableDelay = 60 * time.Millisecond
	p.IdleTimeout = 10 * time.Second
	p.PeekTimeout = time.Second
	p.FirstFrameTimeout = 2 * time.Second
	p.SweepInterval = 50 * time.Millisecond
	return p
}

// StartServer starts a server on 127.0.0.1 with a fresh identity, the accounts of Options.Logins and
// the real engine behind it.
func StartServer(t TB, opt Options) *Env {
	t.Helper()
	dir := opt.DataDir
	if dir == "" {
		dir = t.TempDir()
	}
	id, err := tlsid.LoadOrCreate(filepath.Join(dir, "tls", "cert.pem"), filepath.Join(dir, "tls", "key.pem"), nil)
	if err != nil {
		t.Fatalf("identity: %v", err)
	}
	log := logx.NewCapture()
	if opt.LogFile {
		logsDir := filepath.Join(dir, "logs")
		if err := os.MkdirAll(logsDir, 0o700); err != nil {
			t.Fatalf("logs dir: %v", err)
		}
		f, err := os.OpenFile(filepath.Join(logsDir, "exchange.log"), os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o600)
		if err != nil {
			t.Fatalf("log file: %v", err)
		}
		t.Cleanup(func() { _ = f.Close() }) // registered first: it runs after the server has closed
		log = logx.NewCaptureAlso(f)
	}
	env := &Env{Params: TestParams(), Fingerprint: id.Fingerprint, Log: log, DataDir: dir, t: t}
	if opt.Params != nil {
		env.Params = *opt.Params
	}
	now := opt.Now
	if now == nil {
		now = time.Now
	}
	policy := env.Params.Policy()
	if opt.Caps != nil {
		policy.Defaults.Resources, policy.Defaults.Broadcasts, policy.Defaults.BroadcastsPerDevice =
			opt.Caps.ResourcesPerAccount, opt.Caps.BroadcastsPerAccount, opt.Caps.BroadcastsPerDevice
	}
	pace := opt.PaceInterval
	if pace == 0 {
		pace = -1 // off: see Options.PaceInterval
	}
	env.Accounts, err = accounts.Open(accounts.Options{Dir: dir, Now: now, Params: cheap,
		DevicesPerAccount: env.Params.DevicesPerAccount, CodesPerAccount: env.Params.CodesPerAccount, CodeLifetime: env.Params.CodeLifetime,
		TombstoneLifetime: env.Params.TombstoneLifetime, Rand: opt.AccountsRand, Policy: &policy,
		PaceInterval: pace, Sleep: opt.Sleep})
	if err != nil {
		t.Fatalf("accounts: %v", err)
	}
	logins := opt.Logins
	if logins == nil {
		logins = []string{"alice"}
	}
	for _, l := range logins {
		if env.Accounts.Exists(l) {
			continue
		}
		if _, err := env.Accounts.AddAccount(l, TestPassword); err != nil {
			t.Fatalf("account %s: %v", l, err)
		}
		env.AddSecret(l)
	}
	caps := directory.DefaultCaps()
	caps.ResourcesPerAccount, caps.BroadcastsPerAccount, caps.BroadcastsPerDevice =
		env.Params.ResourcesPerAccount, env.Params.BroadcastsPerAccount, env.Params.BroadcastsPerDevice
	if opt.Caps != nil {
		caps = *opt.Caps
	}
	env.Dir = directory.New(directory.Options{Dir: dir, Now: now, Caps: caps})
	var infos []directory.DeviceInfo
	for _, d := range env.Accounts.AllDevices() {
		infos = append(infos, directory.DeviceInfo{ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product,
			ProductVersion: d.ProductVersion, Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver})
	}
	if err := env.Dir.Load(infos); err != nil {
		t.Fatalf("directory: %v", err)
	}

	lc := limits.DefaultConfig()
	if opt.Limits != nil {
		lc = *opt.Limits
	}
	env.Limiter = limits.NewLimiter(lc, opt.Now)
	llc := limits.DefaultConfig()
	if opt.LoginLimits != nil {
		llc = *opt.LoginLimits
	}
	env.LoginLimiter = limits.NewLimiter(llc, opt.Now)
	conc, wait := opt.VerifyConc, opt.VerifyWait
	if conc == 0 {
		conc = 2
	}
	if wait == 0 {
		wait = 2 * time.Second
	}
	env.Verifier = limits.NewVerifier(nil, conc, wait)
	slots := opt.PreAuthSlot
	if slots == 0 {
		slots = 16
	}
	if opt.Pool != nil {
		env.Pool = opt.Pool
	} else if opt.PortRange[0] != 0 {
		env.Pool = ports.NewPool(opt.PortRange[0], opt.PortRange[1], "127.0.0.1", nil, opt.Now)
	}
	relayHandler := opt.Relay
	if opt.WithRelay && relayHandler == nil {
		rc := rendezvous.RelayConfig{Dir: env.Dir, Log: env.Log.Logger, Limiter: env.Limiter, Now: opt.Now,
			Grace: 400 * time.Millisecond, UploadIdle: 5 * time.Second, WriteTimeout: time.Second, MaxUploadBitsPerSec: -1}
		if opt.RelayConfig != nil {
			opt.RelayConfig(&rc)
		}
		env.Relay = rendezvous.NewRelay(rc)
		relayHandler = env.Relay
	}
	env.Engine = rendezvous.NewEngine(rendezvous.EngineConfig{
		Params: env.Params, Accounts: env.Accounts, Dir: env.Dir, Pool: env.Pool, Verifier: env.Verifier,
		Limiter: env.Limiter, LoginLimiter: env.LoginLimiter, Log: env.Log.Logger, Now: opt.Now,
		PublicEndpoint: opt.PublicEndpoint, ServerVersion: serverVersion(opt), Features: rendezvous.Features{Cast: opt.Cast}, Relay: relayHandler,
		Rand: opt.EngineRand,
	})
	env.Ops = env.Engine.Ops()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	env.Addr = ln.Addr().String()
	env.Host = "127.0.0.1"
	env.Server = rendezvous.New(rendezvous.Config{
		Params: env.Params, TLS: &tls.Config{Certificates: []tls.Certificate{id.Cert}, MinVersion: tls.VersionTLS12},
		Limiter: env.Limiter, PreAuth: limits.NewPreAuth(slots), Backend: env.Engine, Log: env.Log.Logger, Now: opt.Now,
	})
	go func() { _ = env.Server.Serve(ln) }()
	t.Cleanup(env.Server.Close)
	// Every test of every package that starts a server also proves, at its end, that no secret it
	// handled reached the log (tickets 139 and 167). Cleanups run last-in first-out, so this one runs
	// before the server closes.
	t.Cleanup(func() { env.AssertLogsClean(t) })
	return env
}

// Stop closes the server (a test that restarts one on the same data directory).
func (e *Env) Stop() { e.Server.Close() }

// AddSecret records a value that must never appear in a log line.
func (e *Env) AddSecret(s string) {
	if s == "" {
		return
	}
	e.secretMu.Lock()
	e.secrets = append(e.secrets, s)
	e.secretMu.Unlock()
}

// AssertLogsClean fails if any recorded secret, or the test password, is in the captured log.
func (e *Env) AssertLogsClean(t TB) {
	t.Helper()
	e.secretMu.Lock()
	all := append([]string{TestPassword}, e.secrets...)
	e.secretMu.Unlock()
	e.Log.AssertNone(t, all...)
}

// Secrets returns what was recorded (a test scanning the data directory for the same needles).
func (e *Env) Secrets() []string {
	e.secretMu.Lock()
	defer e.secretMu.Unlock()
	return append([]string{TestPassword}, e.secrets...)
}

func serverVersion(opt Options) string {
	if opt.ServerVersion != "" {
		return opt.ServerVersion
	}
	return "test"
}
