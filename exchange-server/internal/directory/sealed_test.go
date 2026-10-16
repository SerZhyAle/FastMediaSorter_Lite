package directory

import (
	"bytes"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"testing"

	"fms/exchange-server/internal/wire"
)

// Sealed content (DEVICE-EXCHANGE 27, ticket 234): the directory keeps the public device key, holds a
// sealed box where it held the clear member, and never looks inside a box.

const (
	kemA = "BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw"
	boxA = `{"v":1,"from":"fromdeviceidAAAAAAAAAA","fromKey":"fromkeyNEEDLE","n":"nonceNEEDLE","ct":"ctNEEDLE","keys":[{"to":"AAAAAAAAAAAAAAAAAAAAAg","enc":"encNEEDLE","wk":"wkNEEDLE"}]}`
	boxB = `{"v":1,"from":"fromdeviceidBBBBBBBBBB","fromKey":"fromkey2NEEDLE","n":"nonce2NEEDLE","ct":"ct2NEEDLE","keys":[]}`
)

var boxNeedles = []string{"fromkeyNEEDLE", "nonceNEEDLE", "ctNEEDLE", "encNEEDLE", "wkNEEDLE", "fromkey2NEEDLE", "ct2NEEDLE"}

func sameJSON(t *testing.T, got, want string) bool {
	t.Helper()
	var a, b any
	if err := json.Unmarshal([]byte(got), &a); err != nil {
		t.Fatalf("not JSON: %s", got)
	}
	if err := json.Unmarshal([]byte(want), &b); err != nil {
		t.Fatal(err)
	}
	return reflect.DeepEqual(a, b)
}

func sealedResource(id, dev, box string) json.RawMessage {
	return json.RawMessage(`{"resourceId":"` + id + `","deviceId":"` + dev + `","kind":"sftp-share","name":"Photos","sealedAccess":` + box + `}`)
}

func sealedBroadcast(id, box string) json.RawMessage {
	return json.RawMessage(`{"broadcastId":"` + id + `","mode":"AUDIO_ONLY","title":"Mic","descriptor":{"endpoints":[` +
		`{"transport":"HTTP_ADTS","kind":"RELAY","seal":{"v":1}},{"transport":"HTTP_ADTS","kind":"TUNNEL","seal":{"v":1}}]},"sealed":` + box + `}`)
}

// A device record carries its public key as `e2eKey`, and a device without one carries none (item DF).
func TestADeviceRecordCarriesItsE2EKey(t *testing.T) {
	key := &wire.E2EKey{V: 1, KEM: kemA}
	e := newEnv(t, func(o *Options) {})
	obs := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, obs)
	obs.reset()
	nd := DeviceInfo{ID: "AAAAAAAAAAAAAAAAAAAAAw", Account: alice, Name: "Tablet", Product: "fms-windows", Capabilities: []string{"e2e-v1"}, E2EKey: key}
	e.d.DeviceEnrolled(nd)

	chs := obs.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Devices) != 1 {
		t.Fatalf("the new device is announced: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Devices)[0]
	var got wire.E2EKey
	if !rec.Has("e2eKey") || json.Unmarshal(rec.Raw("e2eKey"), &got) != nil || got != *key {
		t.Fatalf("the pushed record does not carry the key: %s", rec.Marshal())
	}
	// the full list has it too, and a device that sent none has no member
	_, devices, _, _ := e.d.Snapshot(alice)
	withKey, without := 0, 0
	for _, r := range recordsOf(devices) {
		if r.Has("e2eKey") {
			withKey++
		} else {
			without++
		}
	}
	if withKey != 1 || without != 2 {
		t.Fatalf("%d devices with a key, %d without", withKey, without)
	}
	// a restart keeps it: the accounts store gives the key back with the device
	d2 := New(Options{Dir: e.dir, Now: e.ck.Now})
	if err := d2.Load([]DeviceInfo{nd}); err != nil {
		t.Fatal(err)
	}
	_, devices, _, _ = d2.Snapshot(alice)
	if len(devices) != 1 || !bytes.Contains(devices[0], []byte(kemA)) {
		t.Fatalf("a reloaded device lost its key: %s", devices)
	}
}

// `sealedAccess` lives only while the producer is online: not on disk, not after it leaves (item DI).
func TestSealedAccessIsMemoryOnlyWhileTheProducerIsOnline(t *testing.T) {
	e := newEnv(t, nil)
	prod := e.online(alice, devA1)
	cons := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, cons)
	cons.reset()

	e.publish(alice, devA1, sealedResource(resR1, devA1, boxA))
	chs := cons.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Resources) != 1 {
		t.Fatalf("the consumer is pushed the resource: %d", len(chs))
	}
	rec := recordsOf(chs[0].Upserts.Resources)[0]
	if rec.Has("access") || !sameJSON(t, string(rec.Raw("sealedAccess")), boxA) {
		t.Fatalf("the box is delivered as it came, in place of access: %s", rec.Marshal())
	}
	if r := e.resources(alice)[0]; !sameJSON(t, string(r.Raw("sealedAccess")), boxA) {
		t.Fatal("the full list has the box")
	}
	// a producer that publishes again with a new box (a device joined) replaces it
	e.publish(alice, devA1, sealedResource(resR1, devA1, boxB))
	if r := e.resources(alice)[0]; !sameJSON(t, string(r.Raw("sealedAccess")), boxB) {
		t.Fatal("a second publish replaces the box")
	}
	// nothing of the box reaches the file
	if err := e.d.Flush(); err != nil {
		t.Fatal(err)
	}
	disk, err := os.ReadFile(filepath.Join(e.dir, "directory.json"))
	if err != nil {
		t.Fatal(err)
	}
	for _, n := range boxNeedles {
		if bytes.Contains(disk, []byte(n)) {
			t.Fatalf("%q reached directory.json", n)
		}
	}
	if !bytes.Contains(disk, []byte(resR1)) || bytes.Contains(disk, []byte("sealedAccess")) {
		t.Fatalf("the record is on disk without its box: %s", disk)
	}
	// the producer leaves: the record stays, offline, without the box
	e.d.Offline(alice, devA1, prod)
	r := e.resources(alice)[0]
	if p, _ := r.Str("presence"); p != "offline" || r.Has("sealedAccess") || r.Has("access") {
		t.Fatalf("an offline resource holds no box: %s", r.Marshal())
	}
	// a restart: the stored form has none either
	d2 := New(Options{Dir: e.dir, Now: e.ck.Now})
	if err := d2.Load(devs()); err != nil {
		t.Fatal(err)
	}
	_, _, rs, _ := d2.Snapshot(alice)
	if len(rs) != 1 || bytes.Contains(rs[0], []byte("sealedAccess")) {
		t.Fatalf("a reloaded resource holds a box: %s", rs)
	}
}

// A box is one JSON object and nothing more is asked of it (item DH); the rest of the record is as before.
func TestABoxThatIsNotAnObjectIsMalformed(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	for _, bad := range []string{`"text"`, `[1]`, `null`, `7`} {
		if _, err := e.d.Publish(alice, devA1, sealedResource(resR1, devA1, bad)); !errors.Is(err, ErrMalformed) {
			t.Errorf("sealedAccess %s: %v", bad, err)
		}
		if _, err := e.d.BroadcastStart(alice, devA1, sealedBroadcast(broB1, bad), nil); !errors.Is(err, ErrMalformed) {
			t.Errorf("sealed %s: %v", bad, err)
		}
	}
	if len(e.resources(alice)) != 0 {
		t.Fatal("a malformed record stored nothing")
	}
	// the server does not read what is inside: an empty object and unknown members pass
	e.publish(alice, devA1, sealedResource(resR1, devA1, `{}`))
	e.publish(alice, devA1, sealedResource(resR2, devA1, `{"v":9,"anything":[1,2,{"x":null}],"keys":"not-even-a-list"}`))
	if n := len(e.resources(alice)); n != 2 {
		t.Fatalf("%d resources", n)
	}
}

// A sealed broadcast keeps its box and its endpoint members, and `broadcast-update` replaces the box
// (item DK). The directory leaves the descriptor alone.
func TestASealedBroadcastKeepsItsBoxAndTheSealOfItsEndpoints(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	cons := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, cons)
	cons.reset()

	if _, err := e.d.BroadcastStart(alice, devA1, sealedBroadcast(broB1, boxA), nil); err != nil {
		t.Fatal(err)
	}
	chs := cons.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Broadcasts) != 1 {
		t.Fatalf("pushed: %d", len(chs))
	}
	check := func(raw json.RawMessage, box string) {
		t.Helper()
		rec, err := wire.ParseRecord(raw)
		if err != nil {
			t.Fatal(err)
		}
		if !sameJSON(t, string(rec.Raw("sealed")), box) {
			t.Fatalf("the box: %s", rec.Raw("sealed"))
		}
		var d struct {
			Endpoints []struct {
				Kind string          `json:"kind"`
				Seal json.RawMessage `json:"seal"`
			} `json:"endpoints"`
		}
		if err := json.Unmarshal(rec.Raw("descriptor"), &d); err != nil || len(d.Endpoints) != 2 ||
			string(d.Endpoints[0].Seal) != `{"v":1}` || string(d.Endpoints[1].Seal) != `{"v":1}` {
			t.Fatalf("the endpoints lost their seal: %s", rec.Raw("descriptor"))
		}
	}
	check(chs[0].Upserts.Broadcasts[0], boxA)
	cons.reset()
	if err := e.d.BroadcastUpdate(alice, devA1, sealedBroadcast(broB1, boxB), nil); err != nil {
		t.Fatal(err)
	}
	chs = cons.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Broadcasts) != 1 {
		t.Fatalf("the update is pushed: %d", len(chs))
	}
	check(chs[0].Upserts.Broadcasts[0], boxB)
	// the full list has only the new box
	_, _, _, bs := e.d.Snapshot(alice)
	if len(bs) != 1 || bytes.Contains(bs[0], []byte("fromkeyNEEDLE")) {
		t.Fatalf("the old box is still listed: %s", bs)
	}
	// it is memory only: nothing of it is ever written
	if err := e.d.Flush(); err != nil {
		t.Fatal(err)
	}
	disk, _ := os.ReadFile(filepath.Join(e.dir, "directory.json"))
	for _, n := range boxNeedles {
		if bytes.Contains(disk, []byte(n)) {
			t.Fatalf("%q reached directory.json", n)
		}
	}
}

// A protected resource withholds its access from a device that cannot present the PIN proof (item AH);
// the box that replaces `access` is withheld with it (item DI).
func TestAProtectedSealedResourceIsWithheldLikeItsAccess(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.online(alice, devA2) // no pin capability
	raw := sealedResource(resR1, devA1, boxA)
	e.publish(alice, devA1, append(raw[:len(raw)-1:len(raw)-1], []byte(`,"protection":{"scheme":"pin-v1"},"shareId":"`+shrS1+`"}`)...))

	hasBox := func(rs []json.RawMessage) (box, shared bool) {
		t.Helper()
		if len(rs) != 1 {
			t.Fatalf("%d resources", len(rs))
		}
		r, err := wire.ParseRecord(rs[0])
		if err != nil || !r.Has("protection") {
			t.Fatalf("the protection block went missing: %s", rs[0])
		}
		return r.Has("sealedAccess"), r.Has("shareId") || r.Has("access")
	}
	_, _, rs, _ := e.d.SnapshotFor(alice, devA2)
	if box, shared := hasBox(rs); box || shared {
		t.Fatalf("a device without the capability was handed the box or an access path: %s", rs[0])
	}
	// a device that can present the proof is handed the box as it came
	info := devs()[1]
	info.Capabilities = []string{wire.PinCapability}
	e.d.DeviceEnrolled(info)
	e.online(alice, devA2)
	_, _, rs, _ = e.d.SnapshotFor(alice, devA2)
	if box, _ := hasBox(rs); !box {
		t.Fatalf("a device with the capability lost the box: %s", rs[0])
	}
}

// What a guest holds of an owner's record carries none of the owner's own boxes (items DI, DK and DJ).
func TestAGuestIsHandedNoBoxOfTheOwningAccount(t *testing.T) {
	for _, k := range []string{"sealedAccess", "sealed", "access", "credentials"} {
		raw := json.RawMessage(`{"resourceId":"r","name":"n","` + k + `":{"v":1,"x":"NEEDLE"},"kept":1}`)
		out := guestRecord(raw)
		if bytes.Contains(out, []byte("NEEDLE")) || !bytes.Contains(out, []byte(`"kept"`)) {
			t.Errorf("%s: %s", k, out)
		}
	}
}
