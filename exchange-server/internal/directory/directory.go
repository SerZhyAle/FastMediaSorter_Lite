// Package directory is the account's live directory of devices, resources and broadcasts
// (DEVICE-EXCHANGE 6, 7.3, 7.4 and the disk and memory halves of 7.9, ticket 163).
//
// One Dir holds one hub per account and answers every question inside the hub of the authenticated
// account: two accounts never see each other, on any call. The only cross-account indexes are the
// two capability ids (shareId, broadcastId); they map to an (account, id) pair internally and never
// reveal an account's existence.
//
// It knows nothing of sockets. A device's control stream is a Sink; the Dir enqueues pre-encoded
// frames on it and never waits. What leaves the directory - a port lease to release, a tunnel to
// abort, a stream to close - is reported through Hooks AFTER the lock is released, in order.
//
// What reaches the disk (directory.json): the resource records WITHOUT access, shareId and
// publicPort, and the revision of each account. Never access, never shareId, never a broadcast,
// never a tunnel: a restart comes back with every resource offline and no access until its producer
// returns and publishes again, so no consumer is ever handed a stale access.
package directory

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"

	"fms/exchange-server/internal/wire"
)

// File is the name of the store in the data directory; StoreVersion its storeVersion member.
const (
	File         = "directory.json"
	StoreVersion = 1
)

// Errors. Reason maps one to the wire's refusal where the contract has one.
var (
	ErrMalformed             = errors.New("directory: malformed record")
	ErrProtectionUnsupported = errors.New("directory: unsupported protection")
	ErrIDCollision           = errors.New("directory: the id is held by another")
	ErrCapacity              = errors.New("directory: capacity")
	// ErrLimit is a count limit of the account (resources, live broadcasts): DEVICE-EXCHANGE 19 item AS.
	// ErrCapacity stays for what does not fit a frame.
	ErrLimit       = errors.New("directory: an account limit")
	ErrPortUnavail = errors.New("directory: no port available")
	ErrNoDevice    = errors.New("directory: no such device")
	ErrNotYours    = errors.New("directory: not this device's")
	ErrNotFound    = errors.New("directory: target not found")
	ErrNewerStore  = errors.New("directory: the store was written by a newer server")
)

// Reason turns a Dir error into the refusal the wire answers with ("" for an error that is not a
// refusal: a malformed record closes the stream).
func Reason(err error) wire.Reason {
	switch {
	case errors.Is(err, ErrProtectionUnsupported):
		return wire.ReasonProtectionUnsupported
	case errors.Is(err, ErrIDCollision):
		return wire.ReasonIDCollision
	case errors.Is(err, ErrLimit):
		return wire.ReasonLimitReached
	case errors.Is(err, ErrCapacity), errors.Is(err, wire.ErrRecordSize):
		return wire.ReasonCapacity
	case errors.Is(err, ErrPortUnavail):
		return wire.ReasonPortUnavailable
	}
	return ""
}

// Sink is a device's control stream as the directory sees it.
type Sink interface {
	Send(wire.Envelope) error
	SendFrame([]byte) error
	Close()
}

// PortGranter binds the third-party port of a resource (ticket 164 rule 8). Both calls run under the
// directory lock, so a lease is bound BEFORE `published` is sent and a release is never reordered
// with the next grant.
type PortGranter interface {
	// Grant is called for every publish. requested is nil when the record asks for no port; the
	// granter keeps an existing lease of the resource when it can, and releases it when the record no
	// longer asks. It returns the port that really listens, or ErrPortUnavail.
	Grant(account, resourceID string, requested *int) (granted int, err error)
	// Release gives the resource's lease back (unpublish, offline, revoke).
	Release(account, resourceID string)
}

// Hooks are what the directory asks its host to do after a transaction. Every field may be nil.
type Hooks struct {
	// ResourceGone: a resource left the directory or lost its producer. Abort its tunnels.
	ResourceGone func(account, resourceID, deviceID string)
	// BroadcastGone: a broadcast ended. Tear down its relay and its tunnels.
	BroadcastGone func(account, broadcastID, deviceID string)
	// CloseDevice: close every stream of a device (revoke, a rotated token, account removal).
	CloseDevice func(deviceID string)
	// GrantTargetGone: a resource was withdrawn or a broadcast ended. The grants of that target end with
	// it (20 item BD); the host takes them back. Not called when a producer merely goes offline.
	GrantTargetGone func(account, target string, broadcast bool)
}

// Caps are the directory's working numbers until rung 2 freezes them (ticket 163 question 2).
type Caps struct {
	ResourcesPerAccount  int
	BroadcastsPerAccount int
	BroadcastsPerDevice  int
}

// DefaultCaps are 256 resources and 16 live broadcasts per account, 4 per device. A zero is no cap.
func DefaultCaps() Caps { return Caps{256, 16, 4} }

// Options configure a Dir.
type Options struct {
	Dir  string // the data directory ("" keeps nothing on disk)
	Now  func() time.Time
	Caps Caps
	// CapsFor resolves the caps of ONE account (the operator's per-account limits, 19 item AS); nil
	// means Caps for everyone. It is called under the directory's lock, so it must not call back.
	CapsFor func(account string) Caps
	// ListenersOf says how many listeners a broadcast has right now; the relay and the tunnels are the host's,
	// not the directory's. nil means none are known (0). It is called WITHOUT the directory's lock held.
	ListenersOf func(broadcastID string) int
	Ports       PortGranter
	Hooks       Hooks
	// WriteTemp creates the temporary file of a write (a test replaces it).
	WriteTemp func(name string, data []byte, perm os.FileMode) error
}

// DeviceInfo is what the directory shows of an enrolled device.
type DeviceInfo struct {
	ID, Account, Name, Product, ProductVersion, Platform string
	Roles                                                []string
	Capabilities                                         []string
	Receiver                                             json.RawMessage
	LastSeen                                             time.Time
	// E2EKey is the device's public sealing key (27 item DF), shown in its record. nil: the device has none.
	E2EKey *wire.E2EKey
}

type devState struct {
	info    DeviceInfo
	sink    Sink // the control stream, nil while offline
	online  bool
	updated time.Time
	raw     json.RawMessage
}

type resState struct {
	id, device string
	rec        *wire.Record // live: with access, shareId, publicPort while the producer is online
	shareID    string
	updated    time.Time
	raw        json.RawMessage
}

type broState struct {
	id, device   string
	rec          *wire.Record
	maxListeners int
	// verifiers are the credential pairs the relay checks a listener against (item BF): in memory only, with
	// the broadcast, never part of rec or raw (so never in a directory, a changed push or an admin view).
	// nil or empty: the relay checks nobody. Replaced wholesale, never edited in place, so a Target may share it.
	verifiers []wire.Verifier
	updated   time.Time
	raw       json.RawMessage
}

type hub struct {
	login      string
	revision   int64
	devices    map[string]*devState
	resources  map[string]*resState
	broadcasts map[string]*broState
	subs       map[string]Sink // by deviceId: the control streams that subscribed
}

type ref struct{ account, id string }

// Dir is the whole deployment's directory.
type Dir struct {
	opt Options
	now func() time.Time

	mu        sync.Mutex
	hubs      map[string]*hub
	shareIdx  map[string]ref // shareId -> (account, resourceId)
	broIdx    map[string]ref // broadcastId -> (account, broadcastId)
	devIdx    map[string]string
	grants    GrantSource               // what members gave to guests (grants.go); nil: nothing
	payloads  map[grantRef]GrantPayload // the secret member of each grant, in memory only (27 item DQ)
	resDirty  bool                      // resource records changed since the last write
	revDirty  bool                      // a revision changed since the last write
	lastWrite time.Time                 // of the last write
	persistMu sync.Mutex
}

// New builds an empty Dir. Load fills it.
func New(opt Options) *Dir {
	if opt.Now == nil {
		opt.Now = time.Now
	}
	if opt.Caps == (Caps{}) {
		opt.Caps = DefaultCaps()
	}
	if opt.WriteTemp == nil {
		opt.WriteTemp = os.WriteFile
	}
	return &Dir{opt: opt, now: opt.Now, hubs: map[string]*hub{}, shareIdx: map[string]ref{}, broIdx: map[string]ref{}, devIdx: map[string]string{},
		payloads: map[grantRef]GrantPayload{}}
}

func (d *Dir) hubLocked(account string) *hub {
	h := d.hubs[account]
	if h == nil {
		h = &hub{login: account, revision: 1, devices: map[string]*devState{}, resources: map[string]*resState{},
			broadcasts: map[string]*broState{}, subs: map[string]Sink{}}
		d.hubs[account] = h
	}
	return h
}

// --- rendering: the records of section 6 as the server hands them out ---

func (d *Dir) renderDevice(h *hub, s *devState) {
	r := wire.NewRecord()
	r.SetStr("deviceId", s.info.ID)
	r.SetStr("deviceName", s.info.Name)
	r.SetStr("product", s.info.Product)
	if s.info.ProductVersion != "" {
		r.SetStr("productVersion", s.info.ProductVersion)
	}
	if s.info.Platform != "" {
		r.SetStr("platform", s.info.Platform)
	}
	if len(s.info.Roles) > 0 {
		b, _ := json.Marshal(s.info.Roles)
		r.SetRaw("roles", b)
	}
	if len(s.info.Capabilities) > 0 {
		b, _ := json.Marshal(s.info.Capabilities)
		r.SetRaw("capabilities", b)
	}
	if len(s.info.Receiver) > 0 {
		r.SetRaw("receiver", s.info.Receiver)
	}
	if s.info.E2EKey != nil {
		b, _ := json.Marshal(s.info.E2EKey)
		r.SetRaw("e2eKey", b)
	}
	r.SetStr("presence", presence(s.online))
	if !s.info.LastSeen.IsZero() {
		r.SetTime("lastSeen", s.info.LastSeen)
	}
	r.SetTime("updatedAt", s.updated)
	s.raw = r.Marshal()
}

func presence(online bool) string {
	if online {
		return "online"
	}
	return "offline"
}

func (d *Dir) renderResource(h *hub, s *resState) {
	online := h.devices[s.device] != nil && h.devices[s.device].online
	s.raw = resourceForm(s.rec, s.device, presence(online), s.updated)
}

// resourceForm is a resource record as a reader receives it: the members the producer sent plus the
// ones the server owns (`deviceId`, `presence`, `updatedAt`; `publicPort` is already in rec once granted).
func resourceForm(rec *wire.Record, device, presenceValue string, updated time.Time) json.RawMessage {
	r := rec.Clone()
	r.SetStr("deviceId", device)
	r.SetStr("presence", presenceValue)
	r.SetTime("updatedAt", updated)
	return r.Marshal()
}

// widestPort is the widest value a granted `publicPort` can take: five digits.
const widestPort = 65535

// liveMembers are the members of a resource record that live only while its producer is online: the
// producer's `access` (and the box that replaces it), the `shareId` and the granted `publicPort`.
var liveMembers = []string{"access", "sealedAccess", "shareId", "publicPort"}

// resourceBoundExceeded is DEVICE-EXCHANGE item BS for a resource: the 16 KiB bound holds for the record
// AS DELIVERED, in every form the server will deliver it. There are two. While the producer is online the
// record carries its live members, presence `online` and, when it asked for a third-party port, a granted
// port of up to five digits; once the device goes offline the live members are gone and presence `offline`
// is a byte longer than `online`. A record admitted here is therefore listed within the bound however long
// it lives: a server never accepts a record it cannot list (item A).
func resourceBoundExceeded(rec *wire.Record, device string, updated time.Time, portRequested bool) bool {
	live := rec.Clone()
	if portRequested {
		live.SetInt("publicPort", widestPort)
	}
	gone := rec.Clone()
	for _, k := range liveMembers {
		gone.Delete(k)
	}
	return len(resourceForm(live, device, presence(true), updated)) > wire.MaxRecord ||
		len(resourceForm(gone, device, presence(false), updated)) > wire.MaxRecord
}

func (d *Dir) renderBroadcast(h *hub, s *broState) {
	r := s.rec.Clone()
	r.SetStr("deviceId", s.device)
	r.SetTime("updatedAt", s.updated)
	s.raw = r.Marshal()
}

// checkBox is the one thing the directory asks of a sealed member of a record - `sealedAccess` of a
// resource (27 item DI), `sealed` of a broadcast (item DK): when it is there it is one JSON object. The
// server does not look at what is inside a box, ever (item DH).
func checkBox(rec *wire.Record, key string) error {
	if rec.Has(key) && wire.CheckBox(rec.Raw(key)) != nil {
		return ErrMalformed
	}
	return nil
}

// stored is the form a resource record takes on disk: no access, no sealedAccess (the box that replaces
// it, 27 item DI), no shareId, no publicPort (all four live only while the producer is online), no
// presence (a restart is offline by definition).
func stored(r *wire.Record) *wire.Record {
	c := r.Clone()
	for _, k := range []string{"access", "sealedAccess", "shareId", "publicPort", "presence"} {
		c.Delete(k)
	}
	return c
}

// --- a transaction: what one handler call changed, pushed as ONE changed with ONE revision ---

type tx struct {
	d       *Dir
	h       *hub
	upDev   map[string]json.RawMessage
	upRes   map[string]json.RawMessage
	upBro   map[string]json.RawMessage
	rmDev   map[string]bool
	rmRes   map[string]bool
	rmBro   map[string]bool
	upGrant map[string]json.RawMessage // a member's grants added or changed (20 item BD, 27 item DJ)
	rmGrant map[string]bool            // a member's grants taken back
	devSet  bool                       // the set of devices changed (enrolled or removed): the givers of a guest are told
	after   []func()                   // run after the lock is released, in order
	hooks   Hooks                      // the hooks as they were when the transaction began
	resDirt bool
}

func (d *Dir) begin(h *hub) *tx {
	return &tx{d: d, h: h, hooks: d.opt.Hooks, upDev: map[string]json.RawMessage{}, upRes: map[string]json.RawMessage{}, upBro: map[string]json.RawMessage{},
		rmDev: map[string]bool{}, rmRes: map[string]bool{}, rmBro: map[string]bool{},
		upGrant: map[string]json.RawMessage{}, rmGrant: map[string]bool{}}
}

func (t *tx) upsertDevice(s *devState) {
	t.d.renderDevice(t.h, s)
	delete(t.rmDev, s.info.ID)
	t.upDev[s.info.ID] = s.raw
}

func (t *tx) upsertResource(s *resState) {
	t.d.renderResource(t.h, s)
	delete(t.rmRes, s.id)
	t.upRes[s.id] = s.raw
}

func (t *tx) upsertBroadcast(s *broState) {
	t.d.renderBroadcast(t.h, s)
	delete(t.rmBro, s.id)
	t.upBro[s.id] = s.raw
}

func (t *tx) removeDevice(id string) {
	delete(t.upDev, id)
	t.rmDev[id] = true
}

func (t *tx) removeResource(id string) {
	delete(t.upRes, id)
	t.rmRes[id] = true
}

func (t *tx) removeBroadcast(id string) {
	delete(t.upBro, id)
	t.rmBro[id] = true
}

func (t *tx) empty() bool {
	return len(t.upDev)+len(t.upRes)+len(t.upBro)+len(t.rmDev)+len(t.rmRes)+len(t.rmBro)+len(t.upGrant)+len(t.rmGrant) == 0
}

func sortedRaw(m map[string]json.RawMessage) []json.RawMessage {
	keys := make([]string, 0, len(m))
	for k := range m {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	out := make([]json.RawMessage, 0, len(keys))
	for _, k := range keys {
		out = append(out, m[k])
	}
	return out
}

func sortedIDs(m map[string]bool) []string {
	out := make([]string, 0, len(m))
	for k := range m {
		out = append(out, k)
	}
	sort.Strings(out)
	return out
}

// commit ends a transaction under the lock: the revision grows by one, and every subscriber of the
// account is handed ONE changed. Pushes are never coalesced across revisions, so a client that sees a
// gap knows it missed something. A subscriber whose queue is full closes itself (it relists on
// reconnect); a changed too large for a frame closes every subscriber for the same reason.
func (t *tx) commit() {
	if t.empty() {
		return
	}
	h := t.h
	h.revision++
	t.d.revDirty = true
	if t.resDirt {
		t.d.resDirty = true
	}
	ch := &wire.Changed{
		Revision: h.revision,
		Upserts:  wire.Upserts{Devices: sortedRaw(t.upDev), Resources: sortedRaw(t.upRes), Broadcasts: sortedRaw(t.upBro), Grants: sortedRaw(t.upGrant)},
		Removals: wire.Removals{DeviceIDs: sortedIDs(t.rmDev), ResourceIDs: sortedIDs(t.rmRes), BroadcastIDs: sortedIDs(t.rmBro), GrantIDs: sortedIDs(t.rmGrant)},
	}
	t.d.fanOutToGuestsLocked(h.login, t) // what changed in what a guest was given
	if t.devSet {
		t.d.pushGrantDevicesLocked(h.login) // the devices of a guest changed: whoever gave to it is told
	}
	if len(h.subs) == 0 {
		return
	}
	for id, sink := range h.subs {
		view := *ch
		view.Upserts.Resources = t.d.resourcesFor(h, id, ch.Upserts.Resources)
		frame, err := wire.EncodeFrame(&view, wire.MaxFrameAuth)
		if err != nil {
			go sink.Close() // never under the lock: closing a TLS stream may wait on a slow peer
			delete(h.subs, id)
			continue
		}
		if sink.SendFrame(frame) != nil {
			delete(h.subs, id)
		}
	}
}

func (t *tx) afterUnlock(f func()) { t.after = append(t.after, f) }

// finish commits and unlocks, then runs the hooks the transaction queued.
func (t *tx) finish() {
	t.commit()
	t.d.mu.Unlock()
	for _, f := range t.after {
		f()
	}
}

// --- loading ---

type storeFile struct {
	StoreVersion int              `json:"storeVersion"`
	Revisions    map[string]int64 `json:"revisions"`
	Resources    []storedResource `json:"resources"`
}

type storedResource struct {
	Account string          `json:"account"`
	Record  json.RawMessage `json:"record"`
}

// Load installs the enrolled devices (all offline) and reads directory.json: the resource records
// of devices that still exist, all offline and without access, and each account's revision PLUS ONE,
// so subscribers resync after a restart even if the last writes were lost.
func (d *Dir) Load(devs []DeviceInfo) error {
	var f storeFile
	if d.opt.Dir != "" {
		data, err := os.ReadFile(filepath.Join(d.opt.Dir, File))
		switch {
		case err == nil:
			if err := json.Unmarshal(data, &f); err != nil {
				return fmt.Errorf("%s is not valid JSON: %w", File, err)
			}
			if f.StoreVersion > StoreVersion {
				return fmt.Errorf("%s: %w", File, ErrNewerStore)
			}
		case !errors.Is(err, os.ErrNotExist):
			return err
		}
		_ = os.Remove(filepath.Join(d.opt.Dir, File+".tmp"))
	}
	d.mu.Lock()
	defer d.mu.Unlock()
	now := d.now()
	for _, info := range devs {
		h := d.hubLocked(info.Account)
		s := &devState{info: info, updated: now}
		h.devices[info.ID] = s
		d.devIdx[info.ID] = info.Account
		d.renderDevice(h, s)
	}
	for acct, rev := range f.Revisions {
		if h := d.hubs[acct]; h != nil {
			h.revision = rev + 1
		}
	}
	for _, sr := range f.Resources {
		h := d.hubs[sr.Account]
		if h == nil {
			d.resDirty = true
			continue
		}
		rec, err := wire.ParseRecord(sr.Record)
		if err != nil {
			d.resDirty = true
			continue
		}
		id, _ := rec.Str("resourceId")
		dev, _ := rec.Str("deviceId")
		if !wire.ValidID(id) || h.devices[dev] == nil {
			d.resDirty = true // the device is gone: its records are not kept
			continue
		}
		rec = stored(rec)
		s := &resState{id: id, device: dev, rec: rec, updated: now}
		if at, ok := rec.Str("updatedAt"); ok {
			if t, err := wire.ParseTime(at); err == nil {
				s.updated = t
			}
		}
		h.resources[id] = s
		d.renderResource(h, s)
	}
	return nil
}

// --- persistence ---

func (d *Dir) snapshotLocked() storeFile {
	f := storeFile{StoreVersion: StoreVersion, Revisions: map[string]int64{}, Resources: []storedResource{}}
	logins := make([]string, 0, len(d.hubs))
	for l := range d.hubs {
		logins = append(logins, l)
	}
	sort.Strings(logins)
	for _, l := range logins {
		h := d.hubs[l]
		f.Revisions[l] = h.revision
		ids := make([]string, 0, len(h.resources))
		for id := range h.resources {
			ids = append(ids, id)
		}
		sort.Strings(ids)
		for _, id := range ids {
			rec := stored(h.resources[id].rec)
			rec.SetTime("updatedAt", h.resources[id].updated)
			f.Resources = append(f.Resources, storedResource{Account: l, Record: rec.Marshal()})
		}
	}
	return f
}

// Persist writes directory.json when something changed: at once for a resource record, and a
// revision alone at most once a second (the revision is persisted lazily, and always on Flush).
func (d *Dir) Persist(now time.Time) error {
	d.mu.Lock()
	if !d.resDirty && !(d.revDirty && now.Sub(d.lastWrite) >= time.Second) {
		d.mu.Unlock()
		return nil
	}
	return d.writeLocked(now)
}

// Flush writes whatever is dirty (a clean stop).
func (d *Dir) Flush() error {
	d.mu.Lock()
	if !d.resDirty && !d.revDirty {
		d.mu.Unlock()
		return nil
	}
	return d.writeLocked(d.now())
}

// writeLocked snapshots under the lock, releases it and writes. The caller holds d.mu.
func (d *Dir) writeLocked(now time.Time) error {
	if d.opt.Dir == "" {
		d.resDirty, d.revDirty = false, false
		d.mu.Unlock()
		return nil
	}
	f := d.snapshotLocked()
	d.resDirty, d.revDirty = false, false
	d.lastWrite = now
	d.mu.Unlock()

	d.persistMu.Lock()
	defer d.persistMu.Unlock()
	data, err := json.MarshalIndent(f, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	if err := os.MkdirAll(d.opt.Dir, 0o700); err != nil {
		return err
	}
	path := filepath.Join(d.opt.Dir, File)
	tmp := path + ".tmp"
	if err := d.opt.WriteTemp(tmp, data, 0o600); err != nil {
		d.mu.Lock()
		d.resDirty, d.revDirty = true, true // try again on the next tick
		d.mu.Unlock()
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}
