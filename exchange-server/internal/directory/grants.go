package directory

import (
	"encoding/json"
	"sort"

	"fms/exchange-server/internal/wire"
)

// This file is what a `guest` account sees (DEVICE-EXCHANGE 19 item AO, 20 item BD, ticket 224): its own
// devices and what was given to it - nothing else, and never the secrets of the account that gave it.
//
// A grant gives ONE resource or ONE broadcast of a member account to ONE guest account. The directory
// does not store grants (the accounts store does); it asks a GrantSource, under its own lock, so a
// source must not call back into the directory.

// Grant is one thing given to a guest.
type Grant struct {
	ID        string // the grantId (20 item BD)
	Owner     string // the account that gave it
	Guest     string // the guest account it was given to
	Target    string // the resourceId or broadcastId
	Broadcast bool   // true for a broadcast, false for a resource
}

// GrantSource answers who was given what. Its methods are called under the directory's lock.
type GrantSource interface {
	// GrantsTo lists what was given to a guest account.
	GrantsTo(guest string) []Grant
	// GrantsBy lists what an owner account gave to anyone.
	GrantsBy(owner string) []Grant
	// GrantsOf lists the grants of one target of an owner (one per guest).
	GrantsOf(owner, target string) []Grant
}

// GrantPayload is the one member of a grant that carries a secret (27 item DJ): the box `sealed`, or - from a
// client without the capability - the clear `access` of a resource or `credentials` of a broadcast (20 item
// BD). It is held in memory only, delivered in the guest's record of the grant, never opened, never logged.
type GrantPayload struct {
	Member string          // "sealed", "access" or "credentials"
	Raw    json.RawMessage // one JSON object
}

// String never prints a member.
func (GrantPayload) String() string { return "directory.GrantPayload" }

// GoString never prints a member.
func (GrantPayload) GoString() string { return "directory.GrantPayload" }

type grantRef struct{ owner, id string }

// SetGrants installs the source of grants; nil means nothing is ever given.
func (d *Dir) SetGrants(g GrantSource) {
	d.mu.Lock()
	d.grants = g
	d.mu.Unlock()
}

// capsFor resolves the caps of one account under the lock.
func (d *Dir) capsFor(account string) Caps {
	if d.opt.CapsFor != nil {
		return d.opt.CapsFor(account)
	}
	return d.opt.Caps
}

// guestRecord is a record as a guest may hold it: without the access of the account that gave it (a
// grant carries its own access, built for it by the publisher - 20 item BD), without the outsider
// capability, the third-party port and the credentials of a protected broadcast. The boxes of the
// owning account's own devices go too (27 items DI and DK): `sealedAccess` and the `sealed` of a
// broadcast are addressed to the account's devices and open nothing for a guest, whose box is the
// grant's (27 item DJ).
func guestRecord(raw json.RawMessage) json.RawMessage { return guestRecordWith(raw, "", nil) }

// guestRecordWith is guestRecord plus what the grant itself adds (20 item BD, 27 item DJ): its `grantId`
// and its payload - the box, or the clear access or credentials - in place of the owner's.
func guestRecordWith(raw json.RawMessage, grantID string, p *GrantPayload) json.RawMessage {
	var record map[string]json.RawMessage
	if json.Unmarshal(raw, &record) != nil {
		return nil
	}
	for _, k := range []string{"access", "sealedAccess", "sealed", "shareId", "publicPort", "credentials", "verifiers"} {
		delete(record, k)
	}
	if grantID != "" {
		id, _ := json.Marshal(grantID)
		record["grantId"] = id
	}
	if p != nil && p.Member != "" {
		record[p.Member] = p.Raw
	}
	out, err := json.Marshal(record)
	if err != nil {
		return nil
	}
	return out
}

// grantedRecord is a record of the owner as the guest of one grant holds it. It runs under the lock.
func (d *Dir) grantedRecord(raw json.RawMessage, g Grant) json.RawMessage {
	var p *GrantPayload
	if x, ok := d.payloads[grantRef{g.Owner, g.ID}]; ok {
		p = &x
	}
	return guestRecordWith(raw, g.ID, p)
}

// dropGrantPayloadsLocked forgets the payloads of the grants of one target: what lives only while the
// producer is online (20 item BD) or while the broadcast runs.
func (d *Dir) dropGrantPayloadsLocked(owner, target string) {
	if d.grants == nil || len(d.payloads) == 0 {
		return
	}
	for _, g := range d.grants.GrantsOf(owner, target) {
		delete(d.payloads, grantRef{owner, g.ID})
	}
}

// guestDevice is a device of a guest account as its grantor sees it (27 item DJ): public values only.
type guestDevice struct {
	DeviceID   string       `json:"deviceId"`
	DeviceName string       `json:"deviceName"`
	E2EKey     *wire.E2EKey `json:"e2eKey,omitempty"`
}

// grantEntry is one element of a member account's `grants` (20 item BD, 27 item DJ): no secret, and the
// guest's devices - the recipients a box of the grant is addressed to.
type grantEntry struct {
	GrantID     string        `json:"grantId"`
	Guest       string        `json:"guest"`
	ResourceID  string        `json:"resourceId,omitempty"`
	BroadcastID string        `json:"broadcastId,omitempty"`
	Devices     []guestDevice `json:"devices"`
}

// renderGrantLocked renders one grant for the member that made it, with the guest's devices as they are now.
func (d *Dir) renderGrantLocked(g Grant) json.RawMessage {
	e := grantEntry{GrantID: g.ID, Guest: g.Guest, Devices: []guestDevice{}}
	if g.Broadcast {
		e.BroadcastID = g.Target
	} else {
		e.ResourceID = g.Target
	}
	if gh := d.hubs[g.Guest]; gh != nil {
		ids := make([]string, 0, len(gh.devices))
		for id := range gh.devices {
			ids = append(ids, id)
		}
		sort.Strings(ids)
		for _, id := range ids {
			info := gh.devices[id].info
			e.Devices = append(e.Devices, guestDevice{DeviceID: info.ID, DeviceName: info.Name, E2EKey: info.E2EKey.Clone()})
		}
	}
	out, err := json.Marshal(e)
	if err != nil {
		return nil
	}
	return out
}

// grantsOfLocked renders the grants a member account made, for its full list.
func (d *Dir) grantsOfLocked(owner string) []json.RawMessage {
	if d.grants == nil {
		return nil
	}
	var out []json.RawMessage
	for _, g := range d.grants.GrantsBy(owner) {
		if rec := d.renderGrantLocked(g); rec != nil {
			out = append(out, rec)
		}
	}
	return out
}

// pushGrantDevicesLocked tells the givers that the devices of a guest changed (27 item DJ): each member
// that holds a grant to it is pushed those grants again, in one `changed` of its own, so that the member's
// devices can seal the boxes anew for the devices the guest has now.
func (d *Dir) pushGrantDevicesLocked(guest string) {
	if d.grants == nil {
		return
	}
	per := map[string][]Grant{}
	for _, g := range d.grants.GrantsTo(guest) {
		per[g.Owner] = append(per[g.Owner], g)
	}
	owners := make([]string, 0, len(per))
	for o := range per {
		owners = append(owners, o)
	}
	sort.Strings(owners)
	for _, o := range owners {
		oh := d.hubs[o]
		if oh == nil {
			continue
		}
		t := d.begin(oh)
		for _, g := range per[o] {
			if rec := d.renderGrantLocked(g); rec != nil {
				t.upGrant[g.ID] = rec
			}
		}
		t.commit()
	}
}

// GuestSnapshot is the full directory of a guest account: its own devices and what was given to it.
// The revision is the guest account's own, and grows when its own devices change or a grant arrives,
// changes or goes.
func (d *Dir) GuestSnapshot(guest string) (revision int64, devices, resources, broadcasts []json.RawMessage) {
	d.mu.Lock()
	defer d.mu.Unlock()
	h := d.hubs[guest]
	if h == nil {
		return 0, nil, nil, nil
	}
	devices = d.rawDevices(h)
	resources, broadcasts = []json.RawMessage{}, []json.RawMessage{}
	if d.grants == nil {
		return h.revision, devices, resources, broadcasts
	}
	given := d.grants.GrantsTo(guest)
	sort.Slice(given, func(i, j int) bool {
		if given[i].Owner != given[j].Owner {
			return given[i].Owner < given[j].Owner
		}
		return given[i].Target < given[j].Target
	})
	for _, g := range given {
		oh := d.hubs[g.Owner]
		if oh == nil {
			continue
		}
		if g.Broadcast {
			if b := oh.broadcasts[g.Target]; b != nil {
				if rec := d.grantedRecord(b.raw, g); rec != nil {
					broadcasts = append(broadcasts, rec)
				}
			}
		} else if r := oh.resources[g.Target]; r != nil {
			if rec := d.grantedRecord(r.raw, g); rec != nil {
				resources = append(resources, rec)
			}
		}
	}
	return h.revision, devices, resources, broadcasts
}

// guestChange is what one guest is told after a transaction of an owner.
type guestChange struct {
	upRes, upBro []json.RawMessage
	rmRes, rmBro []string
}

// pushGuestLocked sends ONE `changed` to every subscriber of a guest account, with the guest hub's own
// next revision. A subscriber that cannot take it is dropped, as everywhere in the directory.
func (d *Dir) pushGuestLocked(guest string, c *guestChange) {
	h := d.hubs[guest]
	if h == nil {
		return
	}
	h.revision++
	d.revDirty = true
	if len(h.subs) == 0 {
		return
	}
	ch := &wire.Changed{
		Revision: h.revision,
		Upserts:  wire.Upserts{Devices: []json.RawMessage{}, Resources: nonNil(c.upRes), Broadcasts: nonNil(c.upBro)},
		Removals: wire.Removals{DeviceIDs: []string{}, ResourceIDs: nonNilIDs(c.rmRes), BroadcastIDs: nonNilIDs(c.rmBro)},
	}
	frame, err := wire.EncodeFrame(ch, wire.MaxFrameAuth)
	for id, sink := range h.subs {
		if err != nil {
			go sink.Close()
			delete(h.subs, id)
			continue
		}
		if sink.SendFrame(frame) != nil {
			delete(h.subs, id)
		}
	}
}

func nonNil(in []json.RawMessage) []json.RawMessage {
	if in == nil {
		return []json.RawMessage{}
	}
	return in
}

func nonNilIDs(in []string) []string {
	if in == nil {
		return []string{}
	}
	return in
}

// fanOutToGuestsLocked tells the guests of an owner what one transaction of the owner changed in what
// they were given. It runs inside commit, under the lock, after the owner's own subscribers.
func (d *Dir) fanOutToGuestsLocked(owner string, t *tx) {
	if d.grants == nil || (len(t.upRes)+len(t.rmRes)+len(t.upBro)+len(t.rmBro)) == 0 {
		return
	}
	per := map[string]*guestChange{}
	get := func(g string) *guestChange {
		c := per[g]
		if c == nil {
			c = &guestChange{}
			per[g] = c
		}
		return c
	}
	for id, raw := range t.upRes {
		for _, g := range d.grants.GrantsOf(owner, id) {
			if rec := d.grantedRecord(raw, g); rec != nil {
				c := get(g.Guest)
				c.upRes = append(c.upRes, rec)
			}
		}
	}
	for id := range t.rmRes {
		for _, g := range d.grants.GrantsOf(owner, id) {
			c := get(g.Guest)
			c.rmRes = append(c.rmRes, id)
		}
	}
	for id, raw := range t.upBro {
		for _, g := range d.grants.GrantsOf(owner, id) {
			if rec := d.grantedRecord(raw, g); rec != nil {
				c := get(g.Guest)
				c.upBro = append(c.upBro, rec)
			}
		}
	}
	for id := range t.rmBro {
		for _, g := range d.grants.GrantsOf(owner, id) {
			c := get(g.Guest)
			c.rmBro = append(c.rmBro, id)
		}
	}
	guests := make([]string, 0, len(per))
	for g := range per {
		guests = append(guests, g)
	}
	sort.Strings(guests)
	for _, g := range guests {
		d.pushGuestLocked(g, per[g])
	}
}

// GrantAdded is the directory's half of a grant that was just recorded (20 item BD, 27 item DJ). The payload p
// is kept in memory - the box, or the clear access or credentials; nil keeps none, as for a grant the
// operator made. The guest's subscribers are told the record as the guest may hold it, with the payload
// and the grantId, if the target exists right now. A NEW grant is pushed to the member's own devices as
// well, in a `changed` that lists it with the guest's devices; a REPLACING one (replaced) changes nothing
// the member sees - the box is the guest's - and costs it no revision (item DJ).
func (d *Dir) GrantAdded(g Grant, p *GrantPayload, replaced bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	ref := grantRef{g.Owner, g.ID}
	if p != nil {
		d.payloads[ref] = *p
	} else {
		delete(d.payloads, ref)
	}
	oh := d.hubs[g.Owner]
	if oh == nil {
		return
	}
	var c guestChange
	if g.Broadcast {
		if b := oh.broadcasts[g.Target]; b != nil {
			if rec := d.grantedRecord(b.raw, g); rec != nil {
				c.upBro = append(c.upBro, rec)
			}
		}
	} else if r := oh.resources[g.Target]; r != nil {
		if rec := d.grantedRecord(r.raw, g); rec != nil {
			c.upRes = append(c.upRes, rec)
		}
	}
	if len(c.upRes)+len(c.upBro) > 0 {
		d.pushGuestLocked(g.Guest, &c)
	}
	if !replaced {
		t := d.begin(oh)
		if rec := d.renderGrantLocked(g); rec != nil {
			t.upGrant[g.ID] = rec
		}
		t.commit()
	}
}

// GrantRemoved is the directory's half of a grant that was taken back: its payload is forgotten and the
// member's own devices are told it is gone. The guest's subscribers are told too (tellGuest), unless the
// record already left with its target in a transaction of the owner that told them.
func (d *Dir) GrantRemoved(g Grant, tellGuest bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	delete(d.payloads, grantRef{g.Owner, g.ID})
	if tellGuest {
		var c guestChange
		if g.Broadcast {
			c.rmBro = []string{g.Target}
		} else {
			c.rmRes = []string{g.Target}
		}
		d.pushGuestLocked(g.Guest, &c)
	}
	if oh := d.hubs[g.Owner]; oh != nil && g.ID != "" {
		t := d.begin(oh)
		t.rmGrant[g.ID] = true
		t.commit()
	}
}

// OwnerGone tells every guest that what an owner gave it is gone, before the owner's grants are erased.
func (d *Dir) OwnerGone(owner string) {
	d.mu.Lock()
	defer d.mu.Unlock()
	if d.grants == nil {
		return
	}
	per := map[string]*guestChange{}
	for _, g := range d.grants.GrantsBy(owner) {
		delete(d.payloads, grantRef{owner, g.ID})
		c := per[g.Guest]
		if c == nil {
			c = &guestChange{}
			per[g.Guest] = c
		}
		if g.Broadcast {
			c.rmBro = append(c.rmBro, g.Target)
		} else {
			c.rmRes = append(c.rmRes, g.Target)
		}
	}
	guests := make([]string, 0, len(per))
	for g := range per {
		guests = append(guests, g)
	}
	sort.Strings(guests)
	for _, g := range guests {
		d.pushGuestLocked(g, per[g])
	}
}

// WithdrawAll takes every resource and every live broadcast of an account out of the directory at once,
// pushed as ordinary removals: what an account that became a guest had published (19 item AO).
func (d *Dir) WithdrawAll(account string) {
	d.mu.Lock()
	h := d.hubs[account]
	if h == nil {
		d.mu.Unlock()
		return
	}
	t := d.begin(h)
	for _, r := range sortedRes(h) {
		d.removeResourceLocked(t, h, r)
	}
	for _, b := range sortedBro(h) {
		d.endBroadcastLocked(t, h, b)
	}
	t.finish()
}

// SignOutDevice ends every stream of a device WITHOUT removing it (19 item AT: a password change signs
// devices out, it never removes them): the control stream closes, so the device goes offline through the
// usual path and its tunnels and data streams are ended by the host's hook. The device record, its
// resources (offline, without access) and its place in the account's list stay.
func (d *Dir) SignOutDevice(account, deviceID string) {
	d.mu.Lock()
	var sink Sink
	if h := d.hubs[account]; h != nil {
		if s := h.devices[deviceID]; s != nil {
			sink = s.sink
		}
	}
	hook := d.opt.Hooks.CloseDevice
	d.mu.Unlock()
	if sink != nil {
		sink.Close()
	}
	if hook != nil {
		hook(deviceID)
	}
}
