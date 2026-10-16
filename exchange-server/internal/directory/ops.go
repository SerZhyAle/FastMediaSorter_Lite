package directory

import (
	"encoding/json"
	"sort"

	"fms/exchange-server/internal/wire"
)

// maxListenersCeiling is the ceiling of a broadcast's `maxListeners` (ticket 163 rule 9).
const maxListenersCeiling = 10000

// --- devices and presence (7.3) ---

// DeviceEnrolled puts an enrolled device into the directory, offline until its control stream
// arrives. A device the account already has - a rotated token - is replaced and goes offline at
// once: its control stream is closed, its resources lose their access, its broadcasts end, and the
// CloseDevice hook ends its other streams (the old token dies at once, 5.2).
func (d *Dir) DeviceEnrolled(info DeviceInfo) {
	d.mu.Lock()
	h := d.hubLocked(info.Account)
	t := d.begin(h)
	if old := h.devices[info.ID]; old != nil {
		oldSink := old.sink
		d.offlineLocked(t, h, old)
		t.afterUnlock(func() {
			if oldSink != nil {
				oldSink.Close()
			}
			if t.hooks.CloseDevice != nil {
				t.hooks.CloseDevice(info.ID)
			}
		})
	}
	s := &devState{info: info, updated: d.now()}
	if old := h.devices[info.ID]; old != nil {
		s.info.LastSeen = old.info.LastSeen
	}
	h.devices[info.ID] = s
	d.devIdx[info.ID] = info.Account
	t.upsertDevice(s)
	t.devSet = true // a guest's givers are told which devices it has now (27 item DJ)
	t.finish()
}

// Online records a device's control stream. A second control stream of the same device replaces the
// first - the old one is closed without a message - and the device's resources and broadcasts carry
// over, access included. It reports false for a device the directory does not know (revoked in the
// meantime): the caller closes the stream.
func (d *Dir) Online(account, deviceID string, sink Sink) bool {
	d.mu.Lock()
	h := d.hubs[account]
	var s *devState
	if h != nil {
		s = h.devices[deviceID]
	}
	if s == nil {
		d.mu.Unlock()
		return false
	}
	t := d.begin(h)
	old := s.sink
	s.sink = sink
	delete(h.subs, deviceID) // the old stream's subscription is not the new stream's
	if old != nil && old != sink {
		t.afterUnlock(old.Close)
	}
	if !s.online {
		s.online = true
		now := d.now()
		s.info.LastSeen, s.updated = now, now
		t.upsertDevice(s)
		for _, r := range h.resources {
			if r.device == deviceID {
				t.upsertResource(r)
			}
		}
	}
	t.finish()
	return true
}

// Offline takes a device offline when its control stream ended, a missed keepalive window or a bye.
// It ignores a stream that is no longer the device's current one (it was replaced). Its resources
// stay in the directory as offline and lose their access, shareId and port lease; its broadcasts
// END; one transaction, one revision, one changed.
func (d *Dir) Offline(account, deviceID string, sink Sink) {
	d.mu.Lock()
	h := d.hubs[account]
	var s *devState
	if h != nil {
		s = h.devices[deviceID]
	}
	if s == nil || s.sink != sink {
		d.mu.Unlock()
		return
	}
	t := d.begin(h)
	d.offlineLocked(t, h, s)
	t.upsertDevice(s)
	t.finish()
}

// offlineLocked is the whole of "a device went offline", shared by Offline, a rotated token, and the
// first half of a revoke.
func (d *Dir) offlineLocked(t *tx, h *hub, s *devState) {
	wasOnline := s.online
	s.sink, s.online = nil, false
	delete(h.subs, s.info.ID)
	if wasOnline {
		now := d.now()
		s.info.LastSeen, s.updated = now, now
	}
	for _, r := range sortedRes(h) {
		if r.device != s.info.ID {
			continue
		}
		d.dropLiveLocked(h, r)
		t.upsertResource(r)
		t.resDirt = true
		id, dev := r.id, r.device
		t.afterUnlock(func() {
			if t.hooks.ResourceGone != nil {
				t.hooks.ResourceGone(h.login, id, dev)
			}
		})
	}
	for _, b := range sortedBro(h) {
		if b.device != s.info.ID {
			continue
		}
		d.endBroadcastLocked(t, h, b)
	}
}

// dropLiveLocked strips what lives only while the producer is online: access (or sealedAccess, the box
// that replaces it - 27 item DI), shareId, publicPort, and the lease behind the port.
func (d *Dir) dropLiveLocked(h *hub, r *resState) {
	if r.shareID != "" {
		delete(d.shareIdx, r.shareID)
		r.shareID = ""
	}
	if d.opt.Ports != nil {
		d.opt.Ports.Release(h.login, r.id)
	}
	for _, k := range liveMembers {
		r.rec.Delete(k)
	}
	d.dropGrantPayloadsLocked(h.login, r.id) // a grant's access is held in memory only while the producer is online (20 item BD)
	r.updated = d.now()
}

func (d *Dir) endBroadcastLocked(t *tx, h *hub, b *broState) {
	d.dropGrantPayloadsLocked(h.login, b.id)
	delete(h.broadcasts, b.id)
	delete(d.broIdx, b.id)
	t.removeBroadcast(b.id)
	id, dev := b.id, b.device
	t.afterUnlock(func() {
		if t.hooks.BroadcastGone != nil {
			t.hooks.BroadcastGone(h.login, id, dev)
		}
		if t.hooks.GrantTargetGone != nil {
			t.hooks.GrantTargetGone(h.login, id, true) // the grants of a broadcast live and end with it (20 item BD)
		}
	})
}

func sortedRes(h *hub) []*resState {
	out := make([]*resState, 0, len(h.resources))
	for _, r := range h.resources {
		out = append(out, r)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].id < out[j].id })
	return out
}

func sortedBro(h *hub) []*broState {
	out := make([]*broState, 0, len(h.broadcasts))
	for _, b := range h.broadcasts {
		out = append(out, b)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].id < out[j].id })
	return out
}

// Subscribe registers the device's control stream for `changed` pushes. Only the device's current
// control stream may subscribe.
func (d *Dir) Subscribe(account, deviceID string, sink Sink) {
	d.mu.Lock()
	defer d.mu.Unlock()
	if h := d.hubs[account]; h != nil {
		if s := h.devices[deviceID]; s != nil && s.sink == sink {
			h.subs[deviceID] = sink
		}
	}
}

// Snapshot is the answer to `list`: the account's revision and every record, sorted by id. The
// requester's own device is included; hiding it is the consumer's rule (9.1).
func (d *Dir) Snapshot(account string) (revision int64, devices, resources, broadcasts []json.RawMessage) {
	return d.SnapshotFor(account, "")
}

// SnapshotFor applies the enrolled device capability to every resource in a full list.
func (d *Dir) SnapshotFor(account, deviceID string) (revision int64, devices, resources, broadcasts []json.RawMessage) {
	revision, devices, resources, broadcasts, _ = d.SnapshotFull(account, deviceID)
	return
}

// SnapshotFull is SnapshotFor plus the grants the account made (20 item BD, 27 item DJ), all read under one
// lock so that they and the revision belong together. grants is nil for an account that gave nothing.
func (d *Dir) SnapshotFull(account, deviceID string) (revision int64, devices, resources, broadcasts, grants []json.RawMessage) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[account]
	if h == nil {
		return 0, nil, nil, nil, nil
	}
	return h.revision, d.rawDevices(h), d.resourcesFor(h, deviceID, d.rawResources(h, "", nil)), d.rawBroadcasts(h, "", nil), d.grantsOfLocked(account)
}

func (d *Dir) rawDevices(h *hub) []json.RawMessage {
	ids := make([]string, 0, len(h.devices))
	for id := range h.devices {
		ids = append(ids, id)
	}
	sort.Strings(ids)
	out := make([]json.RawMessage, 0, len(ids))
	for _, id := range ids {
		out = append(out, h.devices[id].raw)
	}
	return out
}

// rawResources lists the resources' records; replaceID/with swap one record for a candidate.
func (d *Dir) rawResources(h *hub, replaceID string, with json.RawMessage) []json.RawMessage {
	ids := make([]string, 0, len(h.resources)+1)
	for id := range h.resources {
		ids = append(ids, id)
	}
	if replaceID != "" && h.resources[replaceID] == nil {
		ids = append(ids, replaceID)
	}
	sort.Strings(ids)
	out := make([]json.RawMessage, 0, len(ids))
	for _, id := range ids {
		if id == replaceID && with != nil {
			out = append(out, with)
		} else {
			out = append(out, h.resources[id].raw)
		}
	}
	return out
}

func (d *Dir) rawBroadcasts(h *hub, replaceID string, with json.RawMessage) []json.RawMessage {
	ids := make([]string, 0, len(h.broadcasts)+1)
	for id := range h.broadcasts {
		ids = append(ids, id)
	}
	if replaceID != "" && h.broadcasts[replaceID] == nil {
		ids = append(ids, replaceID)
	}
	sort.Strings(ids)
	out := make([]json.RawMessage, 0, len(ids))
	for _, id := range ids {
		if id == replaceID && with != nil {
			out = append(out, with)
		} else {
			out = append(out, h.broadcasts[id].raw)
		}
	}
	return out
}

// --- resources (7.4) ---

// PublishResult is the answer to a publish.
type PublishResult struct {
	ResourceID string
	PublicPort *int
}

// Publish offers a resource record. The record is checked, not interpreted: at most 16 KiB as the
// reader receives it - the members the server adds included, in the widest form (past it, capacity;
// DEVICE-EXCHANGE item BS) - resourceId in the id form, kind and name present - an unknown kind is stored as is -
// deviceId forced to the publishing device, shareId in the id form and unique per deployment.
//
// A resourceId held by another device of the account, or a shareId held anywhere, is ErrIDCollision;
// the SAME device publishing a held resourceId replaces it (item H: how a producer supplies access
// again after a restart). The record's `publicPort` is the third-party port REQUEST; what was granted
// replaces it in the record. A directory that would no longer fit a frame is ErrCapacity.
func (d *Dir) Publish(account, deviceID string, raw json.RawMessage) (PublishResult, error) {
	rec, err := wire.ParseRecord(raw)
	if err != nil {
		if err == wire.ErrRecordSize {
			return PublishResult{}, err
		}
		return PublishResult{}, ErrMalformed
	}
	if rec.Has("protection") && !wire.ValidProtection(rec.Raw("protection")) {
		return PublishResult{}, ErrProtectionUnsupported
	}
	if err := checkBox(rec, "sealedAccess"); err != nil {
		return PublishResult{}, err
	}
	id, ok := rec.Str("resourceId")
	kind, okK := rec.Str("kind")
	name, okN := rec.Str("name")
	if !ok || !wire.ValidID(id) || !okK || kind == "" || !okN || name == "" {
		return PublishResult{}, ErrMalformed
	}
	shareID := ""
	if rec.Has("shareId") {
		if shareID, ok = rec.Str("shareId"); !ok || !wire.ValidID(shareID) {
			return PublishResult{}, ErrMalformed
		}
	}
	var requested *int
	if rec.Has("publicPort") {
		n, ok := rec.Int("publicPort")
		if !ok || n < 0 || n > 65535 {
			return PublishResult{}, ErrMalformed
		}
		requested = &n
	}

	d.mu.Lock()
	h := d.hubs[account]
	var dev *devState
	if h != nil {
		dev = h.devices[deviceID]
	}
	if dev == nil || dev.sink == nil {
		d.mu.Unlock()
		return PublishResult{}, ErrNoDevice
	}
	t := d.begin(h)
	existing := h.resources[id]
	switch {
	case existing != nil && existing.device != deviceID:
		d.mu.Unlock()
		return PublishResult{}, ErrIDCollision
	case shareID != "":
		if r, held := d.shareIdx[shareID]; held && (r.account != account || r.id != id) {
			d.mu.Unlock()
			return PublishResult{}, ErrIDCollision
		}
	}
	if lim := d.capsFor(account).ResourcesPerAccount; existing == nil && lim > 0 && len(h.resources) >= lim {
		d.mu.Unlock()
		return PublishResult{}, ErrLimit
	}

	// the candidate, and whether the account's directory would still fit one frame
	rec.SetStr("deviceId", deviceID)
	candidate := &resState{id: id, device: deviceID, rec: rec, shareID: shareID, updated: d.now()}
	if resourceBoundExceeded(rec, deviceID, candidate.updated, requested != nil) {
		d.mu.Unlock() // item BS: the bound is the record as delivered, not as sent
		return PublishResult{}, ErrCapacity
	}
	d.renderResource(h, candidate)
	if !wire.DirectoryFits(d.rawDevices(h), d.rawResources(h, id, candidate.raw), d.rawBroadcasts(h, "", nil)) {
		d.mu.Unlock()
		return PublishResult{}, ErrCapacity
	}

	// the lease is bound BEFORE `published` is sent, and the record shows what was granted
	var granted *int
	if d.opt.Ports != nil {
		n, err := d.opt.Ports.Grant(account, id, requested)
		if err != nil {
			d.mu.Unlock()
			return PublishResult{}, ErrPortUnavail
		}
		if requested != nil {
			granted = &n
		}
	} else if requested != nil {
		d.mu.Unlock()
		return PublishResult{}, ErrPortUnavail
	}
	if granted != nil {
		rec.SetInt("publicPort", *granted)
	} else {
		rec.Delete("publicPort")
	}

	if existing != nil && existing.shareID != "" && existing.shareID != shareID {
		delete(d.shareIdx, existing.shareID)
	}
	if shareID != "" {
		d.shareIdx[shareID] = ref{account, id}
	}
	candidate.updated = d.now()
	h.resources[id] = candidate
	t.upsertResource(candidate)
	t.resDirt = true
	t.finish()
	return PublishResult{ResourceID: id, PublicPort: granted}, nil
}

// Unpublish removes a resource of the publishing device; an unknown id, or another device's, is
// ignored (no answer, nothing leaks).
func (d *Dir) Unpublish(account, deviceID, resourceID string) {
	d.mu.Lock()
	h := d.hubs[account]
	var r *resState
	if h != nil {
		r = h.resources[resourceID]
	}
	if r == nil || r.device != deviceID {
		d.mu.Unlock()
		return
	}
	t := d.begin(h)
	d.removeResourceLocked(t, h, r)
	t.finish()
}

func (d *Dir) removeResourceLocked(t *tx, h *hub, r *resState) {
	d.dropLiveLocked(h, r)
	delete(h.resources, r.id)
	t.removeResource(r.id)
	t.resDirt = true
	id, dev := r.id, r.device
	t.afterUnlock(func() {
		if t.hooks.ResourceGone != nil {
			t.hooks.ResourceGone(h.login, id, dev)
		}
		if t.hooks.GrantTargetGone != nil {
			t.hooks.GrantTargetGone(h.login, id, false) // the grants of a withdrawn resource end with it (20 item BD)
		}
	})
}

// --- broadcasts (6.3, 7.4) ---

func parseBroadcast(raw json.RawMessage) (*wire.Record, string, error) {
	rec, err := wire.ParseRecord(raw)
	if err != nil {
		if err == wire.ErrRecordSize {
			return nil, "", err
		}
		return nil, "", ErrMalformed
	}
	id, ok := rec.Str("broadcastId")
	mode, okM := rec.Str("mode")
	desc := rec.Raw("descriptor")
	if !ok || !wire.ValidID(id) || !okM || mode == "" || len(desc) == 0 || desc[0] != '{' {
		return nil, "", ErrMalformed
	}
	if err := checkBox(rec, "sealed"); err != nil {
		return nil, "", err
	}
	return rec, id, nil
}

func checkListeners(n *int) error {
	if n != nil && (*n < 0 || *n > maxListenersCeiling) {
		return ErrMalformed
	}
	return nil
}

// BroadcastStart records a live broadcast, in memory only. The record is bounded as delivered (item BS:
// the server's `deviceId`, `updatedAt` and `startedAt` count; past 16 KiB it is capacity). At most 4
// live broadcasts per device and 16 per account (capacity); the broadcastId is unique per deployment (id-collision) except that the
// same device starting a held id replaces it; maxListeners is 0 (no cap) to 10000.
func (d *Dir) BroadcastStart(account, deviceID string, raw json.RawMessage, maxListeners *int) (string, error) {
	rec, id, err := parseBroadcast(raw)
	if err != nil {
		return "", err
	}
	if err := checkListeners(maxListeners); err != nil {
		return "", err
	}
	d.mu.Lock()
	h := d.hubs[account]
	var dev *devState
	if h != nil {
		dev = h.devices[deviceID]
	}
	if dev == nil || dev.sink == nil {
		d.mu.Unlock()
		return "", ErrNoDevice
	}
	existing := h.broadcasts[id]
	if r, held := d.broIdx[id]; held && (r.account != account || existing == nil || existing.device != deviceID) {
		d.mu.Unlock()
		return "", ErrIDCollision
	}
	if existing == nil {
		perDevice := 0
		for _, b := range h.broadcasts {
			if b.device == deviceID {
				perDevice++
			}
		}
		caps := d.capsFor(account)
		if (caps.BroadcastsPerDevice > 0 && perDevice >= caps.BroadcastsPerDevice) ||
			(caps.BroadcastsPerAccount > 0 && len(h.broadcasts) >= caps.BroadcastsPerAccount) {
			d.mu.Unlock()
			return "", ErrLimit
		}
	}
	rec.SetStr("deviceId", deviceID)
	now := d.now()
	if !rec.Has("startedAt") {
		rec.SetTime("startedAt", now)
	}
	cand := &broState{id: id, device: deviceID, rec: rec, updated: now}
	if maxListeners != nil {
		cand.maxListeners = *maxListeners
	}
	d.renderBroadcast(h, cand)
	if len(cand.raw) > wire.MaxRecord { // item BS: the bound is the record as delivered, not as sent
		d.mu.Unlock()
		return "", ErrCapacity
	}
	if !wire.DirectoryFits(d.rawDevices(h), d.rawResources(h, "", nil), d.rawBroadcasts(h, id, cand.raw)) {
		d.mu.Unlock()
		return "", ErrCapacity
	}
	t := d.begin(h)
	h.broadcasts[id] = cand
	d.broIdx[id] = ref{account, id}
	t.upsertBroadcast(cand)
	t.finish()
	return id, nil
}

// BroadcastUpdate replaces a live broadcast's record and, when present, its listener cap (item M). A
// broadcast that is not this device's is ignored; a record that would not fit a frame is ErrCapacity.
func (d *Dir) BroadcastUpdate(account, deviceID string, raw json.RawMessage, maxListeners *int) error {
	rec, id, err := parseBroadcast(raw)
	if err != nil {
		return err
	}
	if err := checkListeners(maxListeners); err != nil {
		return err
	}
	d.mu.Lock()
	h := d.hubs[account]
	var b *broState
	if h != nil {
		b = h.broadcasts[id]
	}
	if b == nil || b.device != deviceID {
		d.mu.Unlock()
		return nil
	}
	rec.SetStr("deviceId", deviceID)
	if !rec.Has("startedAt") {
		if s, ok := b.rec.Str("startedAt"); ok {
			rec.SetStr("startedAt", s)
		}
	}
	cand := &broState{id: id, device: deviceID, rec: rec, maxListeners: b.maxListeners, updated: d.now()}
	if maxListeners != nil {
		cand.maxListeners = *maxListeners
	}
	d.renderBroadcast(h, cand)
	if len(cand.raw) > wire.MaxRecord { // item BS: the bound is the record as delivered, not as sent
		d.mu.Unlock()
		return ErrCapacity
	}
	if !wire.DirectoryFits(d.rawDevices(h), d.rawResources(h, "", nil), d.rawBroadcasts(h, id, cand.raw)) {
		d.mu.Unlock()
		return ErrCapacity
	}
	t := d.begin(h)
	h.broadcasts[id] = cand
	t.upsertBroadcast(cand)
	t.finish()
	return nil
}

// BroadcastEnd ends a live broadcast of the calling device; anything else is ignored.
func (d *Dir) BroadcastEnd(account, deviceID, broadcastID string) {
	d.mu.Lock()
	h := d.hubs[account]
	var b *broState
	if h != nil {
		b = h.broadcasts[broadcastID]
	}
	if b == nil || b.device != deviceID {
		d.mu.Unlock()
		return
	}
	t := d.begin(h)
	d.endBroadcastLocked(t, h, b)
	t.finish()
}

// --- revoke and removal (162 rules 7 and 9) ---

// RevokeDevice removes a device from the directory in one transaction: its control stream closes,
// its resources leave (their ports are released), its broadcasts end, and the CloseDevice hook ends
// its other streams. It reports whether the device was there.
func (d *Dir) RevokeDevice(account, deviceID string) bool {
	d.mu.Lock()
	h := d.hubs[account]
	var s *devState
	if h != nil {
		s = h.devices[deviceID]
	}
	if s == nil {
		d.mu.Unlock()
		return false
	}
	t := d.begin(h)
	d.removeDeviceLocked(t, h, s)
	t.finish()
	return true
}

func (d *Dir) removeDeviceLocked(t *tx, h *hub, s *devState) {
	sink := s.sink
	s.sink, s.online = nil, false
	delete(h.subs, s.info.ID)
	for _, r := range sortedRes(h) {
		if r.device == s.info.ID {
			d.removeResourceLocked(t, h, r)
		}
	}
	for _, b := range sortedBro(h) {
		if b.device == s.info.ID {
			d.endBroadcastLocked(t, h, b)
		}
	}
	delete(h.devices, s.info.ID)
	delete(d.devIdx, s.info.ID)
	t.removeDevice(s.info.ID)
	t.devSet = true
	id := s.info.ID
	t.afterUnlock(func() {
		if sink != nil {
			sink.Close()
		}
		if t.hooks.CloseDevice != nil {
			t.hooks.CloseDevice(id)
		}
	})
}

// RemoveAccount drops an account's whole hub: every device is removed as in a revoke, every
// subscriber is closed (there is no one left to tell), and the revision is forgotten.
func (d *Dir) RemoveAccount(account string) {
	d.mu.Lock()
	h := d.hubs[account]
	if h == nil {
		d.mu.Unlock()
		return
	}
	t := d.begin(h)
	ids := make([]string, 0, len(h.devices))
	for id := range h.devices {
		ids = append(ids, id)
	}
	sort.Strings(ids)
	for _, id := range ids {
		d.removeDeviceLocked(t, h, h.devices[id])
	}
	for id, sink := range h.subs {
		delete(h.subs, id)
		t.afterUnlock(sink.Close)
	}
	delete(d.hubs, account)
	d.resDirty, d.revDirty = true, true
	// no commit: nobody is subscribed to a hub that no longer exists
	d.mu.Unlock()
	for _, f := range t.after {
		f()
	}
}

// --- lookups for tunnels (7.5) ---

// Target is a live producer a tunnel can be opened to.
type Target struct {
	Account      string
	DeviceID     string
	ResourceID   string
	ShareID      string
	BroadcastID  string
	Ctrl         Sink
	MaxListeners int
}

// ResolveResource finds the resource inside ONE account, with its producer online.
func (d *Dir) ResolveResource(account, resourceID string) (Target, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	return d.resolveResourceLocked(account, resourceID)
}

func (d *Dir) resolveResourceLocked(account, resourceID string) (Target, bool) {
	h := d.hubs[account]
	if h == nil {
		return Target{}, false
	}
	r := h.resources[resourceID]
	if r == nil {
		return Target{}, false
	}
	dev := h.devices[r.device]
	if dev == nil || dev.sink == nil {
		return Target{}, false
	}
	return Target{Account: account, DeviceID: r.device, ResourceID: r.id, ShareID: r.shareID, Ctrl: dev.sink}, true
}

// ResolveShare finds a resource by its outsider capability, wherever it lives. The caller never
// reveals the account.
func (d *Dir) ResolveShare(shareID string) (Target, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	r, ok := d.shareIdx[shareID]
	if !ok {
		return Target{}, false
	}
	return d.resolveResourceLocked(r.account, r.id)
}

// ResolveBroadcast finds a live broadcast by its id, wherever it lives.
func (d *Dir) ResolveBroadcast(broadcastID string) (Target, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	r, ok := d.broIdx[broadcastID]
	if !ok {
		return Target{}, false
	}
	h := d.hubs[r.account]
	if h == nil {
		return Target{}, false
	}
	b := h.broadcasts[r.id]
	if b == nil {
		return Target{}, false
	}
	dev := h.devices[b.device]
	if dev == nil || dev.sink == nil {
		return Target{}, false
	}
	return Target{Account: r.account, DeviceID: b.device, BroadcastID: b.id, Ctrl: dev.sink, MaxListeners: b.maxListeners}, true
}

// --- lookups for cast (7.8, ticket 166) ---

// LiveBroadcast returns a live broadcast of ONE account as the directory lists it (the record, for a
// cast offer) and the device that owns it. A broadcast of another account is not found: the id is a
// capability, but a cast never crosses an account.
func (d *Dir) LiveBroadcast(account, broadcastID string) (record json.RawMessage, ownerDeviceID string, ok bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[account]
	if h == nil {
		return nil, "", false
	}
	b := h.broadcasts[broadcastID]
	if b == nil {
		return nil, "", false
	}
	return append(json.RawMessage(nil), b.raw...), b.device, true
}

// ResourceRecord returns the raw record of a resource in ONE account.
func (d *Dir) ResourceRecord(account, resourceID string) (json.RawMessage, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[account]
	if h == nil {
		return nil, false
	}
	r := h.resources[resourceID]
	if r == nil {
		return nil, false
	}
	return append(json.RawMessage(nil), r.raw...), true
}

// Peer returns the control stream of a device of the account that is online. An unknown device, a
// device of another account and an offline one are all "not found" - one answer.
func (d *Dir) Peer(account, deviceID string) (Sink, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[account]
	if h == nil {
		return nil, false
	}
	s := h.devices[deviceID]
	if s == nil || s.sink == nil {
		return nil, false
	}
	return s.sink, true
}

// Receiver is Peer for a device that declared the `receiver` role (6.1): a device that did not is
// never asked to play anything.
func (d *Dir) Receiver(account, deviceID string) (Sink, bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[account]
	if h == nil {
		return nil, false
	}
	s := h.devices[deviceID]
	if s == nil || s.sink == nil {
		return nil, false
	}
	for _, r := range s.info.Roles {
		if r == "receiver" {
			return s.sink, true
		}
	}
	return nil, false
}

// --- counts (never a list) ---

// Counts are what a status may show.
type Counts struct {
	Accounts, Devices, Online, Resources, Broadcasts, Subscribers int
}

// Counts returns the directory's totals.
func (d *Dir) Counts() Counts {
	d.mu.Lock()
	defer d.mu.Unlock()
	var c Counts
	for _, h := range d.hubs {
		c.Accounts++
		c.Devices += len(h.devices)
		for _, dev := range h.devices {
			if dev.online {
				c.Online++
			}
		}
		c.Resources += len(h.resources)
		c.Broadcasts += len(h.broadcasts)
		c.Subscribers += len(h.subs)
	}
	return c
}

// Revision is an account's current revision (tests and status).
func (d *Dir) Revision(account string) int64 {
	d.mu.Lock()
	defer d.mu.Unlock()
	if h := d.hubs[account]; h != nil {
		return h.revision
	}
	return 0
}

// --- wiring after construction ---

// SetHooks installs the hooks. The host builds the Dir before the engine that implements them, so
// this is called once, before the first stream.
func (d *Dir) SetHooks(h Hooks) {
	d.mu.Lock()
	d.opt.Hooks = h
	d.mu.Unlock()
}

// SetCapsFor installs the resolver of one account's caps (the operator's per-account limits, 19 item AS).
// It is called under the directory's lock, so it must not call back into the directory.
func (d *Dir) SetCapsFor(f func(account string) Caps) {
	d.mu.Lock()
	d.opt.CapsFor = f
	d.mu.Unlock()
}

// SetPorts installs the port granter (nil: no third-party ports, a port request is port-unavailable).
func (d *Dir) SetPorts(p PortGranter) {
	d.mu.Lock()
	d.opt.Ports = p
	d.mu.Unlock()
}

// DeviceOnline reports whether a device of the account has a control stream.
func (d *Dir) DeviceOnline(account, deviceID string) bool {
	d.mu.Lock()
	defer d.mu.Unlock()
	if h := d.hubs[account]; h != nil {
		if s := h.devices[deviceID]; s != nil {
			return s.online
		}
	}
	return false
}
