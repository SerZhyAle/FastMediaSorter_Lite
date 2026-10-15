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
	Owner     string // the account that gave it
	Target    string // the resourceId or broadcastId
	Broadcast bool   // true for a broadcast, false for a resource
}

// GrantSource answers who was given what. Its methods are called under the directory's lock.
type GrantSource interface {
	// GrantsTo lists what was given to a guest account.
	GrantsTo(guest string) []Grant
	// GrantsBy lists what an owner account gave to anyone.
	GrantsBy(owner string) []struct {
		Guest string
		Grant
	}
	// GuestsOf lists the guests that hold a grant of one target of an owner.
	GuestsOf(owner, target string) []string
}

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
// capability, the third-party port and the credentials of a protected broadcast.
func guestRecord(raw json.RawMessage) json.RawMessage {
	var record map[string]json.RawMessage
	if json.Unmarshal(raw, &record) != nil {
		return nil
	}
	for _, k := range []string{"access", "shareId", "publicPort", "credentials", "verifiers"} {
		delete(record, k)
	}
	out, err := json.Marshal(record)
	if err != nil {
		return nil
	}
	return out
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
				if rec := guestRecord(b.raw); rec != nil {
					broadcasts = append(broadcasts, rec)
				}
			}
		} else if r := oh.resources[g.Target]; r != nil {
			if rec := guestRecord(r.raw); rec != nil {
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
		for _, g := range d.grants.GuestsOf(owner, id) {
			if rec := guestRecord(raw); rec != nil {
				c := get(g)
				c.upRes = append(c.upRes, rec)
			}
		}
	}
	for id := range t.rmRes {
		for _, g := range d.grants.GuestsOf(owner, id) {
			c := get(g)
			c.rmRes = append(c.rmRes, id)
		}
	}
	for id, raw := range t.upBro {
		for _, g := range d.grants.GuestsOf(owner, id) {
			if rec := guestRecord(raw); rec != nil {
				c := get(g)
				c.upBro = append(c.upBro, rec)
			}
		}
	}
	for id := range t.rmBro {
		for _, g := range d.grants.GuestsOf(owner, id) {
			c := get(g)
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

// GrantAdded tells the guest's subscribers that something was given to it: the record as the guest may
// hold it, if the target exists right now.
func (d *Dir) GrantAdded(owner, guest, target string, broadcast bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	oh := d.hubs[owner]
	if oh == nil {
		return
	}
	var c guestChange
	if broadcast {
		if b := oh.broadcasts[target]; b != nil {
			c.upBro = append(c.upBro, guestRecord(b.raw))
		}
	} else if r := oh.resources[target]; r != nil {
		c.upRes = append(c.upRes, guestRecord(r.raw))
	}
	if len(c.upRes)+len(c.upBro) > 0 {
		d.pushGuestLocked(guest, &c)
	}
}

// GrantRemoved tells the guest's subscribers that something is no longer theirs.
func (d *Dir) GrantRemoved(guest, target string, broadcast bool) {
	d.mu.Lock()
	defer d.mu.Unlock()
	var c guestChange
	if broadcast {
		c.rmBro = []string{target}
	} else {
		c.rmRes = []string{target}
	}
	d.pushGuestLocked(guest, &c)
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
