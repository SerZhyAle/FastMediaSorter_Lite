package rendezvous

import (
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/relay"
	"fms/exchange-server/internal/tunnel"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 164: tunnels by resourceId, shareId and broadcastId, the producer's attach, the
// uniform `unavailable`, and the third-party port of ANYWHERE-ACCESS 5.5 re-keyed to resources.

// broadcastTunnelCeiling is the deployment ceiling on the listeners of one broadcast (ticket 167 rule 6).
const broadcastTunnelCeiling = 500

// RelayHandler is what ticket 165 plugs in: the broadcaster's upload, the listeners' GET, and the
// count of relay listeners the tunnels of a broadcast share its `maxListeners` with. End is the
// directory hook's teardown of a broadcast that ended for any reason, Close the server's. Until a
// handler is set an upload closes the stream, a listen is a 404 and the listener count is zero.
// *Relay is the real one.
type RelayHandler interface {
	Upload(st *Stream, sess *Session, m *wire.Upload)
	Listen(st *Stream, broadcastID string)
	Listeners(broadcastID string) int
	End(broadcastID string)
	Close()
}

// unavailable is the ONE refusal of every connect that cannot be served - an unknown id, an ended
// registration, another account's resource, an offline producer, a resource or broadcast at its
// cap, an address that guessed too often, a producer that never attached - and it is ONE code path
// with ONE fixed minimum delay measured from the connect, so neither the bytes nor the timing tell
// the cases apart.
func (e *Engine) unavailable(st *Stream, t0 time.Time) {
	if d := e.p.UnavailableDelay - time.Since(t0); d > 0 {
		select {
		case <-time.After(d):
		case <-e.closing:
		}
	}
	st.Refuse(wire.ReasonUnavailable)
}

// notFound is `unavailable` for the relay's listen URL: a 404 after the same minimum delay, from the
// same moment, so the answer to an unknown id and to an ended one cannot be told apart by timing.
func (e *Engine) notFound(st *Stream, t0 time.Time) {
	if d := e.p.UnavailableDelay - time.Since(t0); d > 0 {
		select {
		case <-time.After(d):
		case <-e.closing:
		}
	}
	_ = st.WriteRaw(wire.HTTPResponse(404, 0))
}

func (e *Engine) relayListeners(broadcastID string) int {
	if e.cfg.Relay == nil {
		return 0
	}
	return e.cfg.Relay.Listeners(broadcastID)
}

// broadcastListeners is what the operator's broadcast list shows as the listeners of a broadcast: the relay's
// listeners plus the tunnels of the broadcast, the two that share its `maxListeners`. A tunnel still waiting
// for the producer's attach is counted as the cap counts it. An unknown or ended broadcast is 0.
func (e *Engine) broadcastListeners(broadcastID string) int {
	return e.relayListeners(broadcastID) + e.tunnels.Count(tunnel.BroadcastKey(broadcastID))
}

// Connect is a consumer's `connect` (7.5). sess is nil for an outsider, who may only come by a
// shareId or a broadcastId (the state machine closes any other).
func (e *Engine) Connect(st *Stream, sess *Session, m *wire.Connect) {
	defer st.Close()
	t0, addr := st.T0(), st.Addr()
	id := m.ResourceID
	if id == "" {
		id = m.ShareID
	}
	if id == "" {
		id = m.BroadcastID
	}
	if !wire.ValidID(id) {
		e.unavailable(st, t0)
		return
	}
	// An address that has used up its unknown-id attempts is answered `unavailable` without a lookup
	// at all, so guessing ids is bounded by the bucket, not by the network.
	if !e.cfg.Limiter.Allow(addr, limits.UnknownID) {
		e.unavailable(st, t0)
		return
	}
	var target directory.Target
	var found bool
	switch {
	case m.ResourceID != "":
		if sess == nil {
			return
		}
		target, found = e.cfg.Dir.ResolveResource(sess.Account, id) // the account is fixed before the lookup
		if !found && e.cfg.Accounts.IsGuest(sess.Account) {
			if g, ok := e.cfg.Accounts.GrantFor(id, sess.Account); ok {
				target, found = e.cfg.Dir.ResolveResource(g.GrantedBy, id)
			}
		}
	case m.ShareID != "":
		target, found = e.cfg.Dir.ResolveShare(id)
	default:
		if sess != nil && e.cfg.Accounts.IsGuest(sess.Account) {
			if !e.cfg.Accounts.HasGrant(id, sess.Account) {
				e.cfg.Log.Info("connect-unavailable", logx.Addr(addr))
				e.unavailable(st, t0)
				return
			}
		}
		target, found = e.cfg.Dir.ResolveBroadcast(id)
	}
	if !found {
		e.cfg.Log.Info("connect-unavailable", logx.Addr(addr))
		e.unavailable(st, t0)
		return
	}
	e.cfg.Limiter.Refund(addr, limits.UnknownID) // a known id costs nothing

	var key tunnel.Key
	max := e.p.TunnelCap
	var build func(tid string) *wire.Open
	switch {
	case m.BroadcastID != "":
		key = tunnel.BroadcastKey(target.BroadcastID)
		max = broadcastTunnelCeiling
		if target.MaxListeners > 0 { // tunnels and the relay's listeners share the cap (item M)
			max = target.MaxListeners - e.relayListeners(target.BroadcastID)
		}
		build = func(tid string) *wire.Open {
			return &wire.Open{TunnelID: tid, BroadcastID: target.BroadcastID, Scheme: m.Scheme}
		}
	case m.ShareID != "":
		key = tunnel.ResourceKey(target.Account, target.ResourceID)
		build = func(tid string) *wire.Open { return &wire.Open{TunnelID: tid, ShareID: m.ShareID} }
	default:
		key = tunnel.ResourceKey(target.Account, target.ResourceID)
		build = func(tid string) *wire.Open { return &wire.Open{TunnelID: tid, ResourceID: target.ResourceID} }
	}
	if max <= 0 || !e.bridge(st.Conn(), target, key, max, build, func() error { return st.Send(&wire.Connected{}) }) {
		e.unavailable(st, t0)
	}
}

// bridge asks the producer for a data stream (`open`), waits for its `attach`, then splices the
// consumer onto it. announce, when set, writes `connected {}` first - the consumer's connect path; a
// raw third-party client on a published port gets no envelope at all. It reports false when no bridge
// came to be (at its cap, no control stream, no attach in time, ended).
func (e *Engine) bridge(conn net.Conn, target directory.Target, key tunnel.Key, max int,
	build func(tunnelID string) *wire.Open, announce func() error) bool {
	tun, err := e.tunnels.Open(key, target.DeviceID, conn, max)
	if err != nil {
		return false
	}
	defer tun.Done()
	if target.Ctrl.Send(build(tun.ID)) != nil {
		return false
	}
	data, ok := tun.Wait(e.p.PendingWindow, e.closing)
	if !ok {
		return false
	}
	_ = conn.SetDeadline(time.Time{})
	if announce != nil {
		if err := announce(); err != nil {
			tun.Abort()
			return true
		}
	}
	st := relay.Splice(conn, data, e.p.IdleTimeout)
	e.cfg.Log.Info("tunnel-closed", logx.Tunnel(tun.ID), logx.Device(target.DeviceID),
		logx.Int("to-producer", clampInt(st.AToB)), logx.Int("to-consumer", clampInt(st.BToA)))
	return true
}

func clampInt(n int64) int {
	if n > int64(^uint(0)>>1) {
		return int(^uint(0) >> 1)
	}
	return int(n)
}

// Attach is a producer's data stream: `hello`, then `attach {tunnelId}`. An unknown, expired, used or
// foreign tunnel id is closed WITHOUT an answer and counts against the per-address attach-miss bucket.
func (e *Engine) Attach(st *Stream, sess *Session, m *wire.Attach) {
	addr := st.Addr()
	if !e.cfg.Limiter.Allow(addr, limits.AttachMiss) {
		st.Close()
		return
	}
	tun, ok := e.tunnels.Claim(m.TunnelID, sess.DeviceID)
	if !ok {
		e.cfg.Log.Info("attach-miss", logx.Device(sess.DeviceID), logx.Addr(addr))
		st.Close()
		return
	}
	e.cfg.Limiter.Refund(addr, limits.AttachMiss) // a hit costs nothing
	_ = st.Conn().SetDeadline(time.Time{})
	tun.Deliver(st.Conn()) // the consumer's bridge owns the stream from here
}

// Upload and Listen are the relay's (ticket 165, relay_handler.go); without a handler an upload
// closes the stream and a listen is a 404.
func (e *Engine) Upload(st *Stream, sess *Session, m *wire.Upload) {
	if sess != nil && e.cfg.Accounts.IsGuest(sess.Account) { // a guest broadcasts nothing (19 item AO)
		e.cfg.Log.Info("upload-refused", logx.Device(sess.DeviceID), logx.Text("why", "guest"))
		st.Refuse(wire.ReasonNotPermitted)
		return
	}
	if e.cfg.Relay == nil {
		st.Close()
		return
	}
	e.cfg.Relay.Upload(st, sess, m)
}

// Listen serves GET /v2/b/<id>/stream.
func (e *Engine) Listen(st *Stream, broadcastID string) {
	if e.cfg.Relay == nil {
		_ = st.WriteRaw(wire.HTTPResponse(404, 0))
		st.Close()
		return
	}
	e.cfg.Relay.Listen(st, broadcastID)
}

// --- the third-party port (ANYWHERE-ACCESS 5.5) ---

// portBook holds the port leases of published resources: one lease per resource, bound before
// `published` is sent, alive while the resource is published and its device online, released on
// unpublish, offline and revoke. It is the directory's PortGranter.
type portBook struct {
	e      *Engine
	mu     sync.Mutex
	leases map[string]*ports.Lease
}

func newPortBook(e *Engine) *portBook { return &portBook{e: e, leases: map[string]*ports.Lease{}} }

func bookKey(account, id string) string { return account + "\x00" + id }

// Grant is called under the directory's lock for every publish. A record that asks for no port
// releases the lease it had; one that asks for port N (0 = any) keeps a lease it already holds when N
// is 0 or that very port, otherwise binds a new one and gives the old one back. The port that is
// granted may differ from N when N is not free (5.5); nothing free is ports.ErrPortUnavailable.
func (b *portBook) Grant(account, id string, requested *int) (int, error) {
	b.mu.Lock()
	defer b.mu.Unlock()
	key := bookKey(account, id)
	old := b.leases[key]
	if requested == nil {
		if old != nil {
			old.Close()
			delete(b.leases, key)
		}
		return 0, nil
	}
	if old != nil && (*requested == 0 || *requested == old.Port()) {
		return old.Port(), nil
	}
	if b.e.cfg.Pool == nil {
		return 0, ports.ErrPortUnavailable
	}
	// never SO_REUSEADDR: the pool binds with the stock listener, which on Windows lets no other
	// process take a live port
	lease, err := b.e.cfg.Pool.Lease(*requested, func(c net.Conn) { b.e.portConn(account, id, c) })
	if err != nil {
		return 0, err
	}
	if old != nil {
		old.Close()
	}
	b.leases[key] = lease
	return lease.Port(), nil
}

// Release gives a resource's lease back.
func (b *portBook) Release(account, id string) {
	b.mu.Lock()
	defer b.mu.Unlock()
	key := bookKey(account, id)
	if l := b.leases[key]; l != nil {
		l.Close()
		delete(b.leases, key)
	}
}

func (b *portBook) releaseAll() {
	b.mu.Lock()
	defer b.mu.Unlock()
	for k, l := range b.leases {
		l.Close()
		delete(b.leases, k)
	}
}

// Leases is how many third-party ports are leased (a count, never a list).
func (e *Engine) Leases() int {
	e.book.mu.Lock()
	defer e.book.mu.Unlock()
	return len(e.book.leases)
}

// portConn is a raw client on a published resource's port: bridged like a connect, with no envelope on
// its side, because WinSCP and FileZilla speak SSH from the first byte. NOTHING is sent on the plain
// port before the bridge.
func (e *Engine) portConn(account, resourceID string, c net.Conn) {
	key := limits.Key(c.RemoteAddr())
	if !e.cfg.Limiter.Allow(key, limits.Connection) {
		_ = c.Close()
		return
	}
	go func() {
		defer c.Close()
		target, ok := e.cfg.Dir.ResolveResource(account, resourceID)
		if !ok {
			return
		}
		e.bridge(c, target, tunnel.ResourceKey(account, resourceID), e.p.TunnelCap,
			func(tid string) *wire.Open { return &wire.Open{TunnelID: tid, ResourceID: resourceID} }, nil)
	}()
}
