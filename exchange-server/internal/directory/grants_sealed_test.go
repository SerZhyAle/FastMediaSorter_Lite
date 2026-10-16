package directory

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"fms/exchange-server/internal/wire"
)

// Grants with devices and a sealed box (DEVICE-EXCHANGE 27 item DJ, ticket 234): a member's `grants` list the
// guest's devices, the guest's record of the grant carries the box, a change of the guest's devices is pushed to
// the member again, and a grant under a grantId already held replaces only the box.

const (
	grantG1 = "GGGGGGGGGGGGGGGGGGGGGA"
	devB2   = "AAAAAAAAAAAAAAAAAAAAAw"
	kemB    = "BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw"
)

// fakeGrants is the source of grants of a test: a plain slice.
type fakeGrants struct{ all []Grant }

func (f *fakeGrants) GrantsTo(guest string) []Grant {
	var out []Grant
	for _, g := range f.all {
		if g.Guest == guest {
			out = append(out, g)
		}
	}
	return out
}

func (f *fakeGrants) GrantsBy(owner string) []Grant {
	var out []Grant
	for _, g := range f.all {
		if g.Owner == owner {
			out = append(out, g)
		}
	}
	return out
}

func (f *fakeGrants) GrantsOf(owner, target string) []Grant {
	var out []Grant
	for _, g := range f.all {
		if g.Owner == owner && g.Target == target {
			out = append(out, g)
		}
	}
	return out
}

func (f *fakeGrants) drop(id string) {
	kept := f.all[:0]
	for _, g := range f.all {
		if g.ID != id {
			kept = append(kept, g)
		}
	}
	f.all = kept
}

// grantEnv is alice (member) with a published resource, bob (guest) with one device, both subscribed.
type grantEnv struct {
	*env
	src   *fakeGrants
	owner *fakeSink // alice's phone, subscribed
	guest *fakeSink // bob's tablet, subscribed
	g     Grant
}

func newGrantEnv(t *testing.T) *grantEnv {
	t.Helper()
	e := newEnv(t, nil)
	ge := &grantEnv{env: e, src: &fakeGrants{}, g: Grant{ID: grantG1, Owner: alice, Guest: bob, Target: resR1}}
	e.d.SetGrants(ge.src)
	ge.owner = e.online(alice, devA1)
	e.d.Subscribe(alice, devA1, ge.owner)
	ge.guest = e.online(bob, devB1)
	e.d.Subscribe(bob, devB1, ge.guest)
	e.publish(alice, devA1, resource(resR1, devA1, ""))
	ge.owner.reset()
	ge.guest.reset()
	return ge
}

// give records the grant in the source and tells the directory, as the engine does.
func (ge *grantEnv) give(box string, replaced bool) {
	ge.t.Helper()
	if !replaced {
		ge.src.all = append(ge.src.all, ge.g)
	}
	ge.d.GrantAdded(ge.g, &GrantPayload{Member: "sealed", Raw: json.RawMessage(box)}, replaced)
}

func grantEntryOf(t *testing.T, raw json.RawMessage) (id, guest, res string, devs []map[string]json.RawMessage) {
	t.Helper()
	var e struct {
		GrantID    string                       `json:"grantId"`
		Guest      string                       `json:"guest"`
		ResourceID string                       `json:"resourceId"`
		Devices    []map[string]json.RawMessage `json:"devices"`
	}
	if err := json.Unmarshal(raw, &e); err != nil {
		t.Fatalf("a grant entry is not JSON: %s", raw)
	}
	return e.GrantID, e.Guest, e.ResourceID, e.Devices
}

// The guest's record of the grant carries the grantId and the grantor's box and nothing of the owner's access.
func TestAGrantedRecordCarriesTheGrantIdAndTheBox(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)

	chs := ge.guest.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Resources) != 1 {
		t.Fatalf("the guest is told of the grant once: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Resources)[0]
	if got := string(rec.Raw("grantId")); got != `"`+grantG1+`"` {
		t.Fatalf("grantId = %s", got)
	}
	if !sameJSON(t, string(rec.Raw("sealed")), boxA) {
		t.Fatalf("the guest does not hold the box verbatim: %s", rec.Raw("sealed"))
	}
	for _, k := range []string{"access", "sealedAccess", "credentials", "shareId"} {
		if rec.Has(k) {
			t.Fatalf("the guest's record carries %q of the owner", k)
		}
	}

	// the full list carries the same record
	_, _, res, _ := ge.d.GuestSnapshot(bob)
	if len(res) != 1 || !bytes.Contains(res[0], []byte("grantId")) || !bytes.Contains(res[0], []byte("fromdeviceidAAAAAAAAAA")) {
		t.Fatalf("the guest's list lost the grant: %s", res)
	}
}

// A producer that republishes does not lose the guest's box: the grant outlives the record's revisions.
func TestARepublishedResourceKeepsTheGuestsBox(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)
	ge.guest.reset()
	ge.publish(alice, devA1, resource(resR1, devA1, `,"description":"v2"`))

	chs := ge.guest.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Resources) != 1 {
		t.Fatalf("the guest is told of the republish once: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Resources)[0]
	if !sameJSON(t, string(rec.Raw("sealed")), boxA) || !rec.Has("grantId") {
		t.Fatalf("the republished record lost the box or the grantId: %s", rec.Marshal())
	}
}

// The member's list and push carry `grants` with the guest's devices (public values only).
func TestAMembersGrantsListTheGuestsDevices(t *testing.T) {
	ge := newGrantEnv(t)
	// bob's tablet announces a key
	ge.d.DeviceEnrolled(DeviceInfo{ID: devB2, Account: bob, Name: "Laptop", Product: "fms-windows",
		Capabilities: []string{"e2e-v1"}, E2EKey: &wire.E2EKey{V: 1, KEM: kemB}})
	ge.owner.reset()
	ge.give(boxA, false)

	chs := ge.owner.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Grants) != 1 {
		t.Fatalf("the member is told of the grant once: %d", len(chs))
	}
	id, guest, res, devs := grantEntryOf(t, chs[0].Upserts.Grants[0])
	if id != grantG1 || guest != bob || res != resR1 || len(devs) != 2 {
		t.Fatalf("grant entry: %s %s %s %d devices", id, guest, res, len(devs))
	}
	withKey := 0
	for _, d := range devs {
		if _, ok := d["deviceName"]; !ok {
			t.Fatalf("a guest device has no name: %v", d)
		}
		if k, ok := d["e2eKey"]; ok {
			withKey++
			if !bytes.Contains(k, []byte(kemB)) {
				t.Fatalf("the key is not the device's: %s", k)
			}
		}
	}
	if withKey != 1 {
		t.Fatalf("%d guest devices with a key, want 1", withKey)
	}
	if bytes.Contains(chs[0].Upserts.Grants[0], []byte("NEEDLE")) {
		t.Fatalf("the member's entry carries the box: %s", chs[0].Upserts.Grants[0])
	}

	// the member's full list carries the same entry
	_, _, _, _, grants := ge.d.SnapshotFull(alice, devA1)
	if len(grants) != 1 || !bytes.Contains(grants[0], []byte(grantG1)) || bytes.Contains(grants[0], []byte("NEEDLE")) {
		t.Fatalf("the member's list: %s", grants)
	}
	// a device of a plain account never gets the member to list
	_, _, _, _, none := ge.d.SnapshotFull(bob, devB1)
	if len(none) != 0 {
		t.Fatalf("a guest's list carries grants: %s", none)
	}
}

// When the guest's devices change the member is pushed its grants again, in one `changed` per member.
func TestGuestDeviceChangesPushTheGrantsAgain(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)
	ge.owner.reset()

	// enroll
	ge.d.DeviceEnrolled(DeviceInfo{ID: devB2, Account: bob, Name: "Laptop", Product: "fms-windows",
		Capabilities: []string{"e2e-v1"}, E2EKey: &wire.E2EKey{V: 1, KEM: kemB}})
	chs := ge.owner.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Grants) != 1 {
		t.Fatalf("an enrolled guest device is pushed to the member once: %d", len(chs))
	}
	_, _, _, devs := grantEntryOf(t, chs[0].Upserts.Grants[0])
	if len(devs) != 2 {
		t.Fatalf("after the enroll the entry lists %d devices, want 2", len(devs))
	}

	// revoke
	ge.owner.reset()
	if !ge.d.RevokeDevice(bob, devB2) {
		t.Fatal("the device was there")
	}
	chs = ge.owner.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Grants) != 1 {
		t.Fatalf("a revoked guest device is pushed to the member once: %d", len(chs))
	}
	_, _, _, devs = grantEntryOf(t, chs[0].Upserts.Grants[0])
	if len(devs) != 1 {
		t.Fatalf("after the revoke the entry lists %d devices, want 1", len(devs))
	}

	// a device change of an account that gave nothing and was given nothing pushes nobody
	ge.owner.reset()
	ge.d.DeviceEnrolled(DeviceInfo{ID: "AAAAAAAAAAAAAAAAAAAABA", Account: alice, Name: "Tab", Product: "fms-windows"})
	for _, c := range ge.owner.changes(t) {
		if len(c.Upserts.Grants) != 0 {
			t.Fatalf("an unrelated device change pushed grants: %v", c.Upserts.Grants)
		}
	}
}

// A grant that names a grantId already held replaces the box and changes nothing else: the guest gets the new
// box, the member is told nothing and loses no revision.
func TestReplacingAGrantChangesOnlyItsBox(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)
	ge.owner.reset()
	ge.guest.reset()
	revOwner := ge.d.Revision(alice)

	ge.give(boxB, true)

	if got := ge.d.Revision(alice); got != revOwner {
		t.Fatalf("the member's revision moved %d -> %d on a replacement", revOwner, got)
	}
	if n := len(ge.owner.changes(t)); n != 0 {
		t.Fatalf("the member was told %d times of a replacement", n)
	}
	chs := ge.guest.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Resources) != 1 {
		t.Fatalf("the guest is told of the new box once: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Resources)[0]
	if !sameJSON(t, string(rec.Raw("sealed")), boxB) {
		t.Fatalf("the guest does not hold the replacing box: %s", rec.Raw("sealed"))
	}
	_, _, res, _ := ge.d.GuestSnapshot(bob)
	if len(res) != 1 || bytes.Contains(res[0], []byte("nonceNEEDLE")) || !bytes.Contains(res[0], []byte("nonce2NEEDLE")) {
		t.Fatalf("the old box survived in the list: %s", res)
	}
	if len(ge.src.all) != 1 {
		t.Fatalf("a replacement made a second grant: %d", len(ge.src.all))
	}
}

// The box lives in memory only while the producer is online: a producer that leaves, or a withdrawn resource,
// takes the box with it, and no file in the data directory ever holds one.
func TestTheGrantsBoxIsMemoryOnlyAndLeavesWithTheProducer(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)
	ge.publish(alice, devA1, resource(resR1, devA1, `,"description":"x"`)) // forces a save of the state

	_ = filepath.Walk(ge.dir, func(p string, info os.FileInfo, err error) error {
		if err == nil && !info.IsDir() {
			b, _ := os.ReadFile(p)
			for _, n := range boxNeedles {
				if bytes.Contains(b, []byte(n)) {
					t.Errorf("%s holds a box (%s)", p, n)
				}
			}
		}
		return nil
	})

	sink := ge.owner
	ge.d.Offline(alice, devA1, sink)
	_, _, res, _ := ge.d.GuestSnapshot(bob)
	for _, r := range res {
		if bytes.Contains(r, []byte("NEEDLE")) {
			t.Fatalf("a box outlived its producer: %s", r)
		}
	}

	// the producer comes back and republishes: the grant is still there but the box is gone until the
	// grantor sends it again
	o2 := ge.online(alice, devA1)
	ge.d.Subscribe(alice, devA1, o2)
	ge.publish(alice, devA1, resource(resR1, devA1, ""))
	_, _, res, _ = ge.d.GuestSnapshot(bob)
	for _, r := range res {
		if bytes.Contains(r, []byte("NEEDLE")) {
			t.Fatalf("a box came back without a grant: %s", r)
		}
	}
	ge.give(boxB, true)
	_, _, res, _ = ge.d.GuestSnapshot(bob)
	if len(res) != 1 || !bytes.Contains(res[0], []byte("nonce2NEEDLE")) {
		t.Fatalf("the grantor's new box is not delivered: %s", res)
	}
}

// Taking a grant back forgets the box and tells the member and the guest.
func TestTakingAGrantBackTellsBothSides(t *testing.T) {
	ge := newGrantEnv(t)
	ge.give(boxA, false)
	ge.owner.reset()
	ge.guest.reset()

	ge.src.drop(grantG1)
	ge.d.GrantRemoved(ge.g, true)

	oc := ge.owner.changes(t)
	if len(oc) != 1 || len(oc[0].Removals.GrantIDs) != 1 || oc[0].Removals.GrantIDs[0] != grantG1 {
		t.Fatalf("the member is not told which grant went: %+v", oc)
	}
	gc := ge.guest.changes(t)
	if len(gc) != 1 || len(gc[0].Removals.ResourceIDs) != 1 || gc[0].Removals.ResourceIDs[0] != resR1 {
		t.Fatalf("the guest is not told which resource went: %+v", gc)
	}
	// granting the same target again does not bring the old box back
	ge.src.all = append(ge.src.all, ge.g)
	ge.d.GrantAdded(ge.g, nil, false)
	_, _, res, _ := ge.d.GuestSnapshot(bob)
	for _, r := range res {
		if bytes.Contains(r, []byte("NEEDLE")) {
			t.Fatalf("a forgotten box came back: %s", r)
		}
	}
}

// A broadcast grant carries the payload in the guest's broadcast record, and its end drops it.
func TestABroadcastGrantCarriesItsPayloadUntilTheBroadcastEnds(t *testing.T) {
	ge := newGrantEnv(t)
	id, err := ge.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	if err != nil {
		t.Fatal(err)
	}
	g := Grant{ID: "GGGGGGGGGGGGGGGGGGGGGQ", Owner: alice, Guest: bob, Target: id, Broadcast: true}
	ge.src.all = append(ge.src.all, g)
	ge.guest.reset()
	ge.d.GrantAdded(g, &GrantPayload{Member: "credentials", Raw: json.RawMessage(`{"user":"CREDNEEDLE","pass":"CREDNEEDLE"}`)}, false)

	chs := ge.guest.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Broadcasts) != 1 {
		t.Fatalf("the guest is told of the broadcast grant once: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Broadcasts)[0]
	if !rec.Has("credentials") || !rec.Has("grantId") {
		t.Fatalf("the broadcast record has no payload or grantId: %s", rec.Marshal())
	}
	ge.d.BroadcastEnd(alice, devA1, id)
	_, _, _, bro := ge.d.GuestSnapshot(bob)
	if len(bro) != 0 {
		t.Fatalf("an ended broadcast is still listed: %s", bro)
	}
}

// GrantPayload never prints a member, so a log line that formats one stays clean.
func TestAGrantPayloadNeverPrintsItsMember(t *testing.T) {
	p := GrantPayload{Member: "sealed", Raw: json.RawMessage(boxA)}
	for _, s := range []string{p.String(), (&p).String()} {
		if bytes.Contains([]byte(s), []byte("NEEDLE")) {
			t.Fatalf("payload printed: %s", s)
		}
	}
}
