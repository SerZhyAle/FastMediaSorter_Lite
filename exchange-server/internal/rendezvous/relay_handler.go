package rendezvous

import (
	"bufio"
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/relay"
	"fms/exchange-server/internal/tunnel"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 165: the fan-out relay of DEVICE-EXCHANGE 7.6. One broadcaster uploads chunks on
// a data stream; any number of listeners GET /v2/b/<broadcastId>/stream inside TLS on the same port
// and read the same bytes out of one shared ring (internal/relay). The server never inspects a
// payload; it keeps at most two seconds of them in memory; a delay never grows - a listener that
// cannot keep up drops to a joinable chunk, it does not stall the upload or another listener.

// Defaults of ticket 165 (working numbers until rung 4 freezes them).
const (
	DefaultRelayGrace      = 15 * time.Second // a dropped upload may come back within this
	DefaultRelayUploadIdle = 15 * time.Second // an upload that sends no chunk this long is dropped
	DefaultRelayWrite      = 5 * time.Second  // every write to a listener has this deadline
	DefaultRelayMaxUpload  = 16_000_000       // bits per second, sustained over DefaultRelayRateWindow
	DefaultRelayRateWindow = 5 * time.Second
	DefaultRelayPerBcast   = broadcastTunnelCeiling // listeners (relay and tunnels) of one broadcast
	DefaultRelayTotal      = 1000                   // relay listeners of the whole deployment
)

// RelayConfig is everything the relay needs; a zero number keeps its default.
type RelayConfig struct {
	Dir     *directory.Dir
	Log     *logx.Logger
	Limiter *limits.Limiter // the per-address buckets (unknown-id attempts on the listen URL)
	Now     func() time.Time

	Window       time.Duration // ring window, default 2 s
	RingBytes    int           // ring byte cap, default 8 MiB
	Grace        time.Duration
	UploadIdle   time.Duration
	WriteTimeout time.Duration

	// MaxUploadBitsPerSec is the sustained ceiling of rule 10; a negative value disables it.
	MaxUploadBitsPerSec int64
	RateWindow          time.Duration
	ListenersPerBcast   int
	ListenersTotal      int

	// CheckConcurrency bounds how many listener credential checks (one PBKDF2 each, item BF) run at once;
	// the default is 2, the engine's own bound for a password hash. The relay keeps its own pool so a flood of
	// listen requests cannot starve a login, nor a login flood a listener.
	CheckConcurrency int
	// CheckIterations is the PBKDF2 round count a check recomputes. Zero is wire.VerifierIterations - the
	// only value the contract allows. It is a field only so that a test need not pay 600000 rounds for every
	// hash it makes; no deployment sets it.
	CheckIterations int
}

// Relay is the RelayHandler of ticket 165.
type Relay struct {
	cfg RelayConfig
	eng *Engine

	// the credential check of a protected broadcast's listen URL (item BF): at most one check per second per
	// broadcast and source, a bounded number at once, and a dummy salt and hash so that a user nobody holds
	// costs the same PBKDF2 as a wrong password
	checks     *limits.Limiter
	pool       *limits.Verifier
	iterations int
	dummySalt  []byte
	dummyHash  []byte

	mu     sync.Mutex
	bc     map[string]*bcast
	total  int
	closed bool
}

// bcast is the relay's state of one broadcast.
type bcast struct {
	id          string
	ring        *relay.Ring
	upload      *Stream // the stream of the upload that is attached now; nil while detached
	gen         int     // grows with every upload that is attached
	timer       *time.Timer
	listeners   int
	contentType string
	out         atomic.Int64 // bytes written to listeners, over the broadcast's life
	ended       bool
}

// NewRelay builds the relay. Pass it as EngineConfig.Relay: the engine binds itself to it.
func NewRelay(cfg RelayConfig) *Relay {
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	if cfg.Grace <= 0 {
		cfg.Grace = DefaultRelayGrace
	}
	if cfg.UploadIdle <= 0 {
		cfg.UploadIdle = DefaultRelayUploadIdle
	}
	if cfg.WriteTimeout <= 0 {
		cfg.WriteTimeout = DefaultRelayWrite
	}
	if cfg.MaxUploadBitsPerSec == 0 {
		cfg.MaxUploadBitsPerSec = DefaultRelayMaxUpload
	}
	if cfg.RateWindow <= 0 {
		cfg.RateWindow = DefaultRelayRateWindow
	}
	if cfg.ListenersPerBcast <= 0 {
		cfg.ListenersPerBcast = DefaultRelayPerBcast
	}
	if cfg.ListenersTotal <= 0 {
		cfg.ListenersTotal = DefaultRelayTotal
	}
	if cfg.CheckConcurrency <= 0 {
		cfg.CheckConcurrency = 2
	}
	if cfg.CheckIterations <= 0 {
		cfg.CheckIterations = wire.VerifierIterations
	}
	lc := limits.Config{MaxAddrs: 4096}
	lc.Rates[limits.ListenCheck] = limits.ListenCheckRate()
	r := &Relay{cfg: cfg, bc: map[string]*bcast{},
		checks:     limits.NewLimiter(lc, cfg.Now),
		pool:       limits.NewVerifier(nil, cfg.CheckConcurrency, 2*time.Second),
		iterations: cfg.CheckIterations,
		dummySalt:  make([]byte, wire.VerifierSaltLen),
		dummyHash:  make([]byte, wire.VerifierHashLen),
	}
	// random per process: a guess for an unknown user is compared with bytes nobody can predict
	_, _ = rand.Read(r.dummySalt)
	_, _ = rand.Read(r.dummyHash)
	return r
}

// bind is called by NewEngine: the relay answers `unavailable` and 404 through the engine's one delay
// and counts the tunnels of a broadcast next to its own listeners.
func (r *Relay) bind(e *Engine) { r.eng = e }

// Listeners is how many relay listeners a broadcast has now.
func (r *Relay) Listeners(broadcastID string) int {
	r.mu.Lock()
	defer r.mu.Unlock()
	if b := r.bc[broadcastID]; b != nil {
		return b.listeners
	}
	return 0
}

// Checks is how many listener credential checks (one PBKDF2 each, item BF) have run over the relay's life: a
// count for the tests that prove a refused or rate-limited request hashes nothing.
func (r *Relay) Checks() int { return r.pool.Calls() }

// Broadcasts is how many broadcasts hold relay state, and ListenersTotal how many relay listeners
// there are (counts for a status and for the tests that prove nothing is kept).
func (r *Relay) Broadcasts() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return len(r.bc)
}

// ListenersTotal is the number of relay listeners of the whole deployment.
func (r *Relay) ListenersTotal() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.total
}

// --- upload (7.6, 165 rules 1, 2, 7, 10) ---

// Upload serves a broadcaster's data stream. It owns the stream.
func (r *Relay) Upload(st *Stream, sess *Session, m *wire.Upload) {
	defer st.Close()
	e := r.eng
	t0 := st.T0()
	// The broadcast must exist and be this device's: started by `broadcast-start` on its control
	// stream. Every other case is the same refusal after the same minimum delay.
	t, ok := r.cfg.Dir.ResolveBroadcast(m.BroadcastID)
	if !ok || t.DeviceID != sess.DeviceID || t.Account != sess.Account {
		r.cfg.Log.Info("upload-refused", logx.Device(sess.DeviceID), logx.Addr(st.Addr()))
		e.unavailable(st, t0)
		return
	}
	b, gen, replaced := r.attach(m.BroadcastID, st, m.ContentType)
	if b == nil {
		return // the server is closing
	}
	if replaced != nil {
		replaced.Abort() // a second upload replaces the first; listeners stay
	}
	if err := st.Send(&wire.UploadAccepted{}); err != nil {
		r.detach(b, gen)
		return
	}
	r.cfg.Log.Info("upload-accepted", logx.Broadcast(m.BroadcastID), logx.Device(sess.DeviceID))

	conn := st.Conn()
	guard := relay.RateGuard{MaxBitsPerSec: r.cfg.MaxUploadBitsPerSec, Window: r.cfg.RateWindow}
	if r.cfg.MaxUploadBitsPerSec < 0 {
		guard.MaxBitsPerSec = 0
	}
	for {
		_ = conn.SetReadDeadline(time.Now().Add(r.cfg.UploadIdle))
		c, err := wire.ReadChunk(conn)
		switch {
		case err == wire.ErrChunkEnd:
			// a zero length ends the broadcast cleanly: the record leaves the directory, the hook of
			// the directory tears the relay and the tunnels down
			r.cfg.Dir.BroadcastEnd(sess.Account, sess.DeviceID, m.BroadcastID)
			r.End(m.BroadcastID)
			return
		case err != nil:
			// a dropped stream, an idle one, a chunk over the bound or with a bad flag: the upload is
			// gone, the broadcast is not (rule 7) - the producer has the grace period to come back
			r.cfg.Log.Info("upload-dropped", logx.Broadcast(m.BroadcastID), logx.Text("why", chunkWhy(err)))
			r.detach(b, gen)
			return
		}
		if guard.Add(len(c.Payload)+5, r.cfg.Now()) {
			_ = st.Send(wire.Refuse(wire.ReasonCapacity))
			r.cfg.Log.Warn("upload-too-fast", logx.Broadcast(m.BroadcastID), logx.Device(sess.DeviceID))
			r.detach(b, gen)
			return
		}
		b.ring.Append(c)
	}
}

func chunkWhy(err error) string {
	switch err {
	case wire.ErrChunkSize:
		return "chunk-size"
	case wire.ErrChunkFlags:
		return "chunk-flags"
	}
	return "stream"
}

// attach registers an upload of a broadcast. It returns the broadcast's state, the generation of this
// upload and the stream of the upload it replaced (to be aborted by the caller, outside the lock).
func (r *Relay) attach(id string, st *Stream, contentType string) (*bcast, int, *Stream) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return nil, 0, nil
	}
	b := r.bc[id]
	var replaced *Stream
	if b == nil {
		b = &bcast{id: id, ring: relay.NewRing(relay.Config{Window: r.cfg.Window, MaxBytes: r.cfg.RingBytes, Now: r.cfg.Now})}
		r.bc[id] = b
	} else {
		// an upload after another (a replacement, or the producer coming back inside the grace period):
		// the window and the header are the new upload's, the listeners continue from its next joinable chunk
		replaced = b.upload
		b.ring.Restart()
	}
	if b.timer != nil {
		b.timer.Stop()
		b.timer = nil
	}
	b.gen++
	b.upload = st
	b.contentType = contentType
	return b, b.gen, replaced
}

// detach marks an upload gone and starts the grace period; an upload that was replaced meanwhile
// (another generation) does nothing.
func (r *Relay) detach(b *bcast, gen int) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed || b.ended || b.gen != gen || b.upload == nil {
		return
	}
	b.upload = nil
	b.timer = time.AfterFunc(r.cfg.Grace, func() { r.graceExpired(b, gen) })
}

// graceExpired tears the relay of a broadcast down: its listeners' bodies end cleanly. The directory
// record is not touched - it stays while the producer's control stream lives, and its LAN endpoints
// still work (rule 7).
func (r *Relay) graceExpired(b *bcast, gen int) {
	r.mu.Lock()
	if r.closed || b.ended || b.gen != gen || b.upload != nil {
		r.mu.Unlock()
		return
	}
	b.ended = true
	delete(r.bc, b.id)
	r.mu.Unlock()
	b.ring.End()
	r.logEnd(b, "grace-expired")
}

// End ends a broadcast's relay: every listener's body ends cleanly, the ring is dropped, the upload
// stream is closed. The engine calls it from the directory's hook when a broadcast ends for any
// reason (broadcast-end, the loss of the control stream, a revoke, a zero-length chunk).
func (r *Relay) End(broadcastID string) {
	r.mu.Lock()
	b := r.bc[broadcastID]
	if b == nil || b.ended {
		r.mu.Unlock()
		return
	}
	b.ended = true
	delete(r.bc, broadcastID)
	up := b.upload
	b.upload = nil
	if b.timer != nil {
		b.timer.Stop()
		b.timer = nil
	}
	r.mu.Unlock()
	b.ring.End()
	if up != nil {
		up.Abort()
	}
	r.logEnd(b, "ended")
}

func (r *Relay) logEnd(b *bcast, why string) {
	r.mu.Lock()
	n := b.listeners
	r.mu.Unlock()
	// counters only, never an id in clear (rule 10, ticket 167)
	r.cfg.Log.State("relay-ended", logx.Broadcast(b.id), logx.Text("why", why),
		logx.Int("bytes-in", clampInt(b.ring.BytesIn())), logx.Int("bytes-out", clampInt(b.out.Load())), logx.Int("listeners", n))
}

// Close ends every broadcast (the server is closing).
func (r *Relay) Close() {
	r.mu.Lock()
	r.closed = true
	all := make([]*bcast, 0, len(r.bc))
	for id, b := range r.bc {
		b.ended = true
		if b.timer != nil {
			b.timer.Stop()
		}
		delete(r.bc, id)
		all = append(all, b)
	}
	r.mu.Unlock()
	for _, b := range all {
		b.ring.End()
		r.logEnd(b, "server-stop")
	}
}

// --- listen (7.6, 165 rules 5 and 6) ---

type admission int

const (
	admitted admission = iota
	noUpload
	full
)

// Listen serves GET /v2/b/<broadcastId>/stream. It owns the stream.
func (r *Relay) Listen(st *Stream, id string) {
	defer st.Close()
	e := r.eng
	t0, addr := st.T0(), st.Addr()

	// an address that has used up its unknown-id attempts is answered 404 without a lookup, so
	// guessing ids is bounded by the bucket; an unknown, ended or never-started id is 404 after the
	// same minimum delay as 164's `unavailable`
	if !r.cfg.Limiter.Allow(addr, limits.UnknownID) {
		e.notFound(st, t0)
		return
	}
	t, ok := r.cfg.Dir.ResolveBroadcast(id)
	if !ok {
		r.cfg.Log.Info("listen-unavailable", logx.Addr(addr))
		e.notFound(st, t0)
		return
	}
	r.cfg.Limiter.Refund(addr, limits.UnknownID) // a known id costs nothing

	// A protected broadcast asks for its credentials BEFORE it tells anything else about itself (an upload
	// attached or not, a full house), so a listener without a pair learns nothing but the challenge.
	switch r.checkListener(id, addr, st.Authorization(), t.Verifiers) {
	case checkChallenge:
		_ = st.WriteRaw(wire.HTTPResponse(401, 0))
		return
	case checkDenied:
		r.cfg.Log.Info("listen-denied", logx.Addr(addr))
		_ = st.WriteRaw(wire.HTTPResponse(401, 0))
		return
	case checkBusy:
		_ = st.WriteRaw(wire.HTTPResponse(503, 1))
		return
	}

	b, ct, adm := r.admit(id, t.MaxListeners)
	switch adm {
	case noUpload: // a live broadcast whose upload is not attached (yet): the producer sends broadcast-start before it uploads
		_ = st.WriteRaw(wire.HTTPResponse(503, 1))
		return
	case full:
		_ = st.WriteRaw(wire.HTTPResponse(503, 2))
		return
	}
	defer r.release(b)

	conn := st.Conn()
	gone := make(chan struct{})
	go func() { // the client closing its side is how an idle listener is noticed
		buf := make([]byte, 256)
		for {
			if _, err := conn.Read(buf); err != nil {
				close(gone)
				return
			}
		}
	}()

	w := bufio.NewWriterSize(conn, 16<<10)
	write := func(p []byte) bool {
		_ = conn.SetWriteDeadline(time.Now().Add(r.cfg.WriteTimeout))
		_, err := w.Write(p)
		return err == nil
	}
	flush := func() bool {
		_ = conn.SetWriteDeadline(time.Now().Add(r.cfg.WriteTimeout))
		return w.Flush() == nil
	}
	if !write(wire.ListenResponseHead(ct)) || !flush() {
		return
	}
	var head []byte
	ln := b.ring.NewListener()
	for {
		out, wait, ended := ln.Take()
		if ended {
			if write(wire.HTTPFinalChunk) {
				flush()
			}
			return
		}
		if len(out) == 0 {
			select {
			case <-wait:
			case <-gone:
				return
			}
			continue
		}
		for _, p := range out {
			head = wire.AppendHTTPChunkHead(head[:0], len(p))
			if !write(head) || !write(p) || !write(wire.HTTPChunkTail) {
				return // a peer that stops reading hits its write deadline here and is disconnected
			}
			b.out.Add(int64(len(p)))
		}
		if !flush() {
			return
		}
	}
}

// listenCheck is the outcome of the credential check of a listen request (item BF).
type listenCheck int

const (
	checkAdmit     listenCheck = iota // no list to check, or a pair matched
	checkChallenge                    // no usable Basic credentials: the challenge, and nothing was hashed
	checkDenied                       // credentials were given and refused: the rate limit, or no pair matched
	checkBusy                         // every check slot is taken: neither admitted nor refused, retry in a second
)

// maxBasicPassword bounds the password of a Basic credential the relay will hash. The request head is at
// most 8 KiB anyway; a real pair is a generated password of a few dozen characters.
const maxBasicPassword = 1024

// basicCredentials reads an `Authorization: Basic <base64 of user:password>` header (RFC 7617). ok is false for
// anything else - another scheme, bad base64, no colon, an empty user, an over-long password - none of which
// has anything to hash. The scheme name is case-insensitive.
func basicCredentials(header string) (user, password string, ok bool) {
	scheme, rest, found := strings.Cut(strings.TrimSpace(header), " ")
	if !found || !strings.EqualFold(scheme, "Basic") {
		return "", "", false
	}
	raw, err := base64.StdEncoding.DecodeString(strings.TrimSpace(rest))
	if err != nil {
		return "", "", false
	}
	user, password, found = strings.Cut(string(raw), ":")
	if !found || user == "" || len(password) > maxBasicPassword {
		return "", "", false
	}
	return user, password, true
}

// checkListener judges a listen request against the verifiers its broadcast holds (item BF, item DV). A
// broadcast with none - never sent, or sent as an empty list - checks nobody, with or without an Authorization
// header. Otherwise a request with no usable Basic credentials is the challenge and costs nothing, so the
// usual "ask, get 401, ask again with a pair" costs one check; a request with credentials takes one token of
// ListenCheck (one per second per broadcast and source) BEFORE any hash is computed, and the token is not
// refunded, whatever the answer. The hash itself runs inside the relay's own bounded pool.
func (r *Relay) checkListener(id, addr, authorization string, verifiers []wire.Verifier) listenCheck {
	if len(verifiers) == 0 {
		return checkAdmit
	}
	user, password, ok := basicCredentials(authorization)
	if !ok {
		return checkChallenge
	}
	if !r.checks.Allow(id+"\x00"+addr, limits.ListenCheck) {
		return checkDenied
	}
	matched, err := r.pool.Run(func() bool { return r.pairMatches(verifiers, user, password) })
	switch {
	case err != nil:
		return checkBusy
	case matched:
		return checkAdmit
	}
	return checkDenied
}

// pairMatches computes ONE PBKDF2 whatever the user: the salt of the entry that names the user, or the relay's
// dummy salt when none does, so an unknown user and a wrong password cost the same time and say the same
// thing. The hashes are compared in constant time.
func (r *Relay) pairMatches(verifiers []wire.Verifier, user, password string) bool {
	salt, want, found := r.dummySalt, r.dummyHash, 0
	for i := range verifiers {
		if subtle.ConstantTimeCompare([]byte(verifiers[i].User), []byte(user)) == 1 && found == 0 {
			salt, want, found = verifiers[i].Salt, verifiers[i].Hash, 1
		}
	}
	got := wire.DeriveVerifierHash(password, salt, r.iterations)
	return subtle.ConstantTimeCompare(got, want)&found == 1
}

// admit counts a listener in, unless the broadcast has no upload, or its `maxListeners` (counting the
// tunnels brokered for it, item M), the deployment's per-broadcast ceiling or the deployment's total of
// relay listeners is reached. `0` means no cap of the producer's own.
func (r *Relay) admit(id string, maxListeners int) (*bcast, string, admission) {
	r.mu.Lock()
	defer r.mu.Unlock()
	b := r.bc[id]
	if r.closed || b == nil || b.ended || b.upload == nil {
		return nil, "", noUpload
	}
	limit := r.cfg.ListenersPerBcast
	if maxListeners > 0 && maxListeners < limit {
		limit = maxListeners
	}
	tunnels := 0
	if r.eng != nil {
		tunnels = r.eng.tunnels.Count(tunnel.BroadcastKey(id))
	}
	if b.listeners+tunnels >= limit || r.total >= r.cfg.ListenersTotal {
		return nil, "", full
	}
	b.listeners++
	r.total++
	return b, b.contentType, admitted
}

func (r *Relay) release(b *bcast) {
	r.mu.Lock()
	b.listeners--
	r.total--
	r.mu.Unlock()
}

// Held reports what the relay holds of one broadcast: whether an upload is attached, how many chunks
// and bytes its ring holds, and how many bytes have been uploaded over its life. A count for a status
// and for the tests; ok is false when the relay holds nothing of the broadcast.
func (r *Relay) Held(broadcastID string) (uploading bool, chunks, bytes int, bytesIn int64, ok bool) {
	r.mu.Lock()
	b := r.bc[broadcastID]
	if b == nil {
		r.mu.Unlock()
		return false, 0, 0, 0, false
	}
	uploading = b.upload != nil
	ring := b.ring
	r.mu.Unlock()
	chunks, bytes = ring.Held()
	return uploading, chunks, bytes, ring.BytesIn(), true
}
