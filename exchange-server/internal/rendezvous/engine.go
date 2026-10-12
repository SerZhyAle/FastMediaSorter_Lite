package rendezvous

import (
	"io"
	"sync"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/cast"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/tunnel"
)

// Features says which optional capabilities of `welcome.features` this build really implements
// (ticket 161 T-4: the list is driven by what is built, never claimed). Directory and pairing are
// always there; tunnel is there when the server knows its public endpoint (the URLs a producer
// builds cannot exist without it); relay is there when a RelayHandler is set, cast when the flag is -
// the last two arrive with tickets 165 and 166.
type Features struct {
	Cast bool
}

// EngineConfig is everything the real Backend needs.
type EngineConfig struct {
	Params   Params
	Accounts *accounts.Store
	Dir      *directory.Dir
	// Pool is the third-party port pool; nil means a port request is `port-unavailable`.
	Pool     *ports.Pool
	Verifier *limits.Verifier
	// Limiter is the per-client-address limiter the Server also holds; LoginLimiter keeps the buckets
	// that are keyed by a login, an account or a device (a second table, so a stranger cycling logins
	// cannot push real addresses out of the first).
	Limiter      *limits.Limiter
	LoginLimiter *limits.Limiter
	Log          *logx.Logger
	Now          func() time.Time
	// PublicEndpoint is the host:port every client reaches the server by, announced in `welcome`.
	PublicEndpoint string
	ServerVersion  string
	Features       Features
	// Relay is ticket 165's handler; nil until it is built.
	Relay RelayHandler
	// Rand is the source of tunnel ids; nil is the system's generator (the vector generator of
	// ticket 205 injects a seeded one).
	Rand io.Reader
}

// Engine is the real Backend: accounts and enrollment (162), the directory and presence (163),
// tunnels and the third-party port (164).
type Engine struct {
	cfg EngineConfig
	p   Params
	now func() time.Time
	ops *admin.Ops

	tunnels *tunnel.Table
	cast    *cast.Table
	book    *portBook
	closing chan struct{}

	mu      sync.Mutex
	ctl     map[string]*ctlEntry            // control streams by deviceId, for their keepalive windows
	streams map[string]map[*Stream]struct{} // every authenticated stream by deviceId, for a revoke
	closed  bool
}

type ctlEntry struct {
	cs       *ControlStream
	interval time.Duration
	deadline time.Time
}

// NewEngine wires the directory's hooks and port granter to the engine and returns it.
func NewEngine(cfg EngineConfig) *Engine {
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	e := &Engine{cfg: cfg, p: cfg.Params, now: cfg.Now, tunnels: tunnel.NewTable(), closing: make(chan struct{}),
		cast: cast.New(cfg.Now, cfg.Params.CastTimeout, cfg.Params.CastPerAccount),
		ctl:  map[string]*ctlEntry{}, streams: map[string]map[*Stream]struct{}{}}
	e.ops = &admin.Ops{Accounts: cfg.Accounts, Dir: cfg.Dir, Log: cfg.Log, Live: true}
	if cfg.Rand != nil {
		e.tunnels.SetRand(cfg.Rand)
	}
	if b, ok := cfg.Relay.(interface{ bind(*Engine) }); ok {
		b.bind(e)
	}
	e.book = newPortBook(e)
	if cfg.Pool != nil {
		cfg.Dir.SetPorts(e.book)
	} else {
		cfg.Dir.SetPorts(nil)
	}
	cfg.Dir.SetHooks(directory.Hooks{
		ResourceGone:  e.resourceGone,
		BroadcastGone: e.broadcastGone,
		CloseDevice:   e.closeDevice,
	})
	return e
}

// Ops are the operator's operations over this engine's stores, for the admin channel.
func (e *Engine) Ops() *admin.Ops { return e.ops }

// Tunnels is the number of tunnels, pending or bridged (a count, never a list).
func (e *Engine) Tunnels() int { return e.tunnels.Total() }

// features is the `welcome.features` list in the order of the contract (7.2).
func (e *Engine) features() []string {
	out := []string{"directory"}
	if e.cfg.PublicEndpoint != "" {
		out = append(out, "tunnel")
		if e.cfg.Relay != nil {
			out = append(out, "relay")
		}
	}
	if e.cfg.Features.Cast {
		out = append(out, "cast")
	}
	return append(out, "pairing")
}

// --- hooks of the directory ---

func (e *Engine) resourceGone(account, resourceID, deviceID string) {
	e.tunnels.AbortKey(tunnel.ResourceKey(account, resourceID))
	e.cfg.Log.Info("resource-gone", logx.Resource(resourceID), logx.Device(deviceID))
}

func (e *Engine) broadcastGone(account, broadcastID, deviceID string) {
	e.tunnels.AbortKey(tunnel.BroadcastKey(broadcastID))
	if e.cfg.Relay != nil {
		e.cfg.Relay.End(broadcastID) // every listener's body ends cleanly, the ring is dropped
	}
	e.cast.DropBroadcast(broadcastID)
	e.cfg.Log.Info("broadcast-ended", logx.Broadcast(broadcastID), logx.Device(deviceID))
}

// closeDevice ends every stream of a device, its control stream and its data streams, and its
// tunnels: a revoked or re-enrolled device must not keep any connection the old token opened.
func (e *Engine) closeDevice(deviceID string) {
	e.mu.Lock()
	var sts []*Stream
	for st := range e.streams[deviceID] {
		sts = append(sts, st)
	}
	delete(e.ctl, deviceID)
	e.mu.Unlock()
	e.tunnels.AbortDevice(deviceID)
	for _, st := range sts {
		st.Abort()
	}
}

// track remembers an authenticated stream of a device until it closes.
func (e *Engine) track(deviceID string, st *Stream) {
	e.mu.Lock()
	m := e.streams[deviceID]
	if m == nil {
		m = map[*Stream]struct{}{}
		e.streams[deviceID] = m
	}
	m[st] = struct{}{}
	e.mu.Unlock()
	st.OnClose(func() {
		e.mu.Lock()
		if m := e.streams[deviceID]; m != nil {
			delete(m, st)
			if len(m) == 0 {
				delete(e.streams, deviceID)
			}
		}
		e.mu.Unlock()
	})
}

// --- keepalive windows (7.3) ---

func (e *Engine) registerCtl(cs *ControlStream, interval time.Duration) {
	e.mu.Lock()
	e.ctl[cs.sess.DeviceID] = &ctlEntry{cs: cs, interval: interval, deadline: e.now().Add(interval * time.Duration(e.p.WindowMultiplier))}
	e.mu.Unlock()
}

// touchCtl extends the window of a control stream that sent an envelope.
func (e *Engine) touchCtl(cs *ControlStream) {
	e.mu.Lock()
	if en := e.ctl[cs.sess.DeviceID]; en != nil && en.cs == cs {
		en.deadline = e.now().Add(en.interval * time.Duration(e.p.WindowMultiplier))
	}
	e.mu.Unlock()
}

func (e *Engine) dropCtl(cs *ControlStream) {
	e.mu.Lock()
	if en := e.ctl[cs.sess.DeviceID]; en != nil && en.cs == cs {
		delete(e.ctl, cs.sess.DeviceID)
	}
	e.mu.Unlock()
}

// Sweep takes a device offline when its keepalive window has passed (its control stream is closed,
// and the loop that was reading it does the rest), forgets expired tombstones and pairing codes, and
// writes what is due to disk.
func (e *Engine) Sweep(now time.Time) {
	e.expireCasts()
	var dead []*ControlStream
	e.mu.Lock()
	for id, en := range e.ctl {
		if now.After(en.deadline) {
			dead = append(dead, en.cs)
			delete(e.ctl, id)
		}
	}
	e.mu.Unlock()
	for _, cs := range dead {
		e.cfg.Log.Info("keepalive-expired", logx.Device(cs.sess.DeviceID))
		cs.abort()
	}
	if err := e.cfg.Accounts.Sweep(now); err != nil {
		e.cfg.Log.Error("store-write-failed", logx.Text("store", "devices"), logx.Text("error", err.Error()))
	}
	if err := e.cfg.Dir.Persist(now); err != nil {
		e.cfg.Log.Error("store-write-failed", logx.Text("store", "directory"), logx.Text("error", err.Error()))
	}
}

// Close ends every tunnel and releases every port, and writes what is only in memory.
func (e *Engine) Close() {
	e.mu.Lock()
	if e.closed {
		e.mu.Unlock()
		return
	}
	e.closed = true
	close(e.closing)
	e.mu.Unlock()
	if e.cfg.Relay != nil {
		e.cfg.Relay.Close()
	}
	e.tunnels.AbortAll()
	e.book.releaseAll()
	if err := e.cfg.Dir.Flush(); err != nil {
		e.cfg.Log.Error("store-write-failed", logx.Text("store", "directory"), logx.Text("error", err.Error()))
	}
	if err := e.cfg.Accounts.Flush(); err != nil {
		e.cfg.Log.Error("store-write-failed", logx.Text("store", "devices"), logx.Text("error", err.Error()))
	}
}
