package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"sort"
	"strings"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The sealed-content transcripts of DEVICE-EXCHANGE section 27 (ticket 234, T-6). Each runs against a server
// that advertises `sealed` (rigOpts.sealed); the older transcripts run with it off, as they always did.
//
// The server never opens a box. What these transcripts show is what it DOES with one: it validates the shape
// of the public values it keeps (the device key), stores a sealed member where section 27 puts it, delivers
// it to the devices that are entitled to it - and nothing else. The boxes are real (sealer.go): every one of
// them opens, with the private key of a recipient named in e2e-v1.json, to the payload listed in the `sealed`
// member of frames-v2.json beside the transcripts.

const (
	sealedRelayKey  = "sA7eFuojlttaoDVERblAn7QETvPRKWv-deYyha6sFGY" // 32 bytes, base64url: the group key of item DL
	sealedHostKeyFP = "SHA256:"                                     // + 43 characters, see fpOf
)

func sealedFP(c string) string { return sealedHostKeyFP + strings.Repeat(c, 43) }

// sealedParty names a device of a box: its name in e2e-v1.json (the file that holds its private key) and the
// deviceId the transcript's server knew it by.
type sealedParty struct {
	Device   string `json:"device"`
	DeviceID string `json:"deviceId"`
}

func (p party) view() sealedParty { return sealedParty{Device: p.Dev.Name, DeviceID: p.ID} }

// sealedClip is what a replayer needs of a sealed clipboard value: the clear text and what the sealed bytes
// (the raw step of the transcript) must come to.
type sealedClip struct {
	Clear        string `json:"clear"`
	ClearSHA256  string `json:"clearSha256"`
	SealedSize   int    `json:"sealedSize"`
	SealedSHA256 string `json:"sealedSha256"`
}

// sealedCase is one box of a transcript and what it opens to.
type sealedCase struct {
	Name       string          `json:"name"`
	Transcript string          `json:"transcript"`
	Context    string          `json:"context"`
	From       sealedParty     `json:"from"`
	To         []sealedParty   `json:"to"`
	Payload    json.RawMessage `json:"payload"`
	Box        json.RawMessage `json:"box"`
	Clipboard  *sealedClip     `json:"clipboard,omitempty"`
}

// sealedDevice is a device key as it appears in the directory.
type sealedDevice struct {
	Name   string      `json:"name"`
	E2EKey wire.E2EKey `json:"e2eKey"`
}

// sealedFile is the `sealed` member of frames-v2.json.
type sealedFile struct {
	Section string         `json:"section"`
	Keys    string         `json:"keys"`
	Notes   []string       `json:"notes"`
	Devices []sealedDevice `json:"devices"`
	Cases   []sealedCase   `json:"cases"`
}

// sealedCases collects the boxes the sealed scenarios make; generate resets it before the first scenario.
var sealedCases []sealedCase

func sealedNotes() []string {
	return []string{
		"Every box here was sealed by the generator (cmd/exchange-vectors/sealer.go) over the fixed keys of the shared vector file e2e-v1.json, with the ephemeral keys, the content keys and the nonces drawn from seeded sources: the bytes are a function of the code. The exchange server did not make, open or check any of them (item DH) - it stored and delivered them.",
		"A `device` names a device of e2e-v1.json (`devices[].name`): that file holds its private key. A replaying product opens the `box` as a recipient of `to` with that key, with the `context` given here (the one the reader expects for the place it found the box in, never a value read from the box) and the key of `from` as the key it trusts for the sender; the result must be the `payload`.",
		"`deviceId` is the id the transcript's server knew the device by: it is the `to` of the box's `keys` entries and the `from` of the box. Each transcript has its own server, so the same device of e2e-v1.json has another deviceId in every transcript.",
		"A `clipboard` member names the clear text of a sealed clipboard value: the raw bytes of the transcript step that follows `clipboard-ready` are those bytes sealed under the value key of item DN (HKDF-SHA256 of the box's content key, `info` `fmsx-e2e-v1 clipboard`, in segments of 65536 with the nonce of the segment's index and last flag), and `sealedSize` and `sealedSha256` are what the server counts and answers.",
	}
}

// requireMembers fails the scenario unless a record has every member of has and none of hasNot.
func requireMembers(what string, raw json.RawMessage, has, hasNot []string) {
	rec, err := wire.ParseRecord(raw)
	if err != nil {
		failf("%s: %v", what, err)
	}
	for _, k := range has {
		if !rec.Has(k) {
			failf("%s: no member %q: %s", what, k, raw)
		}
	}
	for _, k := range hasNot {
		if rec.Has(k) {
			failf("%s: member %q must be absent: %s", what, k, raw)
		}
	}
}

// seal makes a box for the transcript and records it for the `sealed` member of the file. It returns the
// box and the content key (a sealed clipboard value needs it).
func (r *rig) seal(tr *transcript, name, context string, from party, to []party, payload string) (json.RawMessage, []byte) {
	box, ck := sealBox(tr.Name+"/"+name, from, to, context, payload)
	c := sealedCase{Name: tr.Name + "/" + name, Transcript: tr.Name, Context: context, From: from.view(),
		Payload: json.RawMessage(payload), Box: box}
	for _, t := range to {
		c.To = append(c.To, t.view())
	}
	sealedCases = append(sealedCases, c)
	return box, ck
}

// enrollKeyed is enroll for a device that has the capability of item DF: it declares `e2e-v1` and its key.
// The stream comes back authenticated, its role not yet fixed.
func (r *rig) enrollKeyed(tr *transcript, id, login, name, product string, rl []string, dev e2eDevice) (*sess, party, string) {
	s := r.open(tr, id, "tls", "enroll with the account's password, declaring the capability e2e-v1 and the device key (item DF)")
	p := party{Dev: dev, ID: r.newID()}
	s.send(&wire.Enroll{Login: login, Password: testkit.TestPassword, DeviceID: p.ID, DeviceName: name, Product: product,
		ProductVersion: "1.0.0", Platform: "vector", Roles: rl, Capabilities: []string{wire.E2ECapability}, KeepaliveSeconds: 30,
		E2EKey: dev.Key()})
	en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
	if !hasString(en.Features, wire.FeatureSealed) {
		failf("%s: the server does not advertise sealed: %v", id, en.Features)
	}
	s.auth()
	return s, p, en.DeviceToken
}

func hasString(list []string, s string) bool {
	for _, x := range list {
		if x == s {
			return true
		}
	}
	return false
}

// scenarioSealedDevices: item DF (the device key and the capability) and item DP (the feature).
func scenarioSealedDevices() *transcript {
	r := newRig("sealed-devices", rigOpts{sealed: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("sealed-devices",
		"The device key of DEVICE-EXCHANGE 27 item DF on a server that advertises `sealed` (item DP): a device enrolls or registers with the "+
			"capability `e2e-v1` and an `e2eKey` (`{v:1, kem}`, a P-256 point); `enrolled` and `welcome` list the feature `sealed`; the key is part of "+
			"the device's record, listed to every device of the account and pushed in `changed`; a device that predates the capability sends neither and "+
			"its record carries no key; a known deviceId enrolls again with its own key (a token rotation) or not at all (id-collision).")

	a, phone, phoneToken := r.enrollKeyed(tr, "phone-enroll", account, "Vector phone", "fms-android",
		roles("resource-producer", "resource-consumer"), e2eDevices["phone"])
	tr.note("`enrolled.features` lists `sealed`: the server keeps and delivers the device key, the boxes and the sealed clipboard kind (item DP)")
	a.send(&wire.Subscribe{})
	r.online(account, phone.ID)
	r.subscribed(1)

	b, tablet, _ := r.enrollKeyed(tr, "tablet-enroll", account, "Vector tablet", "fms-android", roles("resource-consumer"), e2eDevices["tablet"])
	ch := a.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the tablet's record", ch.Upserts.Devices[0], []string{"e2eKey"}, nil)
	tr.note("the device is listed, offline, with its e2eKey: a public value, kept on disk with the record and shown to every device of the account")
	b.send(&wire.List{})
	dir := b.expect(wire.TypeDirectory).(*wire.Directory)
	if len(dir.Devices) != 2 {
		failf("directory: %d devices", len(dir.Devices))
	}
	for _, d := range dir.Devices {
		requireMembers("a keyed device in the directory", d, []string{"e2eKey"}, nil)
	}
	a.expect(wire.TypeChanged)
	tr.note("the tablet is online now")

	leg, _, _ := r.enroll(tr, "legacy-enroll", account, "Vector PC", "fms-windows", roles("resource-consumer"), "")
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("a device with no key", ch.Upserts.Devices[0], nil, []string{"e2eKey"})
	tr.note("a device that predates item DF sends no capability and no e2eKey: its record has none either, and it is never a recipient of a box (item DP)")
	leg.close()
	a.send(&wire.List{})
	dir = a.expect(wire.TypeDirectory).(*wire.Directory)
	keyed := 0
	for _, d := range dir.Devices {
		if rec, _ := wire.ParseRecord(d); rec != nil && rec.Has("e2eKey") {
			keyed++
		}
	}
	if len(dir.Devices) != 3 || keyed != 2 {
		failf("directory: %d devices, %d keyed", len(dir.Devices), keyed)
	}

	// a key lives exactly as long as its deviceId (item DF)
	other := r.open(tr, "tablet-other-key", "tls", "enroll with the deviceId of the tablet and another key: a key lives exactly as long as its deviceId")
	other.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: tablet.ID, DeviceName: "Vector tablet", Product: "fms-android",
		Capabilities: []string{wire.E2ECapability}, E2EKey: e2eDevices["watch"].Key()})
	other.expectRefused(wire.ReasonIDCollision)
	other.expectClosed()
	tr.note("the device is kept as it was; a stranger who learnt the password cannot put a key of its own under a known deviceId")
	same := r.open(tr, "tablet-same-key", "tls", "enroll again with the same deviceId and the same key: a token rotation (item DF, item AT)")
	same.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: tablet.ID, DeviceName: "Vector tablet", Product: "fms-android",
		Capabilities: []string{wire.E2ECapability}, E2EKey: e2eDevices["tablet"].Key()})
	same.expect(wire.TypeEnrolled)
	same.auth()
	same.close()
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the tablet's record after the rotation", ch.Upserts.Devices[0], []string{"e2eKey"}, nil)
	tr.note("the rotation is a change of the record (its token and name are rewritten), and the key stays the same")
	a.send(&wire.Keepalive{})
	a.expect(wire.TypeKeepalive)

	// registration declares the key the same way (item AQ and item DF)
	inv, err := r.env.Ops.CreateInvitation("member", "")
	if err != nil {
		failf("create invitation: %v", err)
	}
	w := r.open(tr, "register", "tls", "a person registers a new account and enrolls the first device in one frame, with the capability and the key")
	watch := party{Dev: e2eDevices["watch"], ID: r.newID()}
	w.send(&wire.Register{Invitation: inv.Code, Login: "carol", Password: testkit.TestPassword, DeviceID: watch.ID,
		DeviceName: "Vector watch", Product: "fms-wear", ProductVersion: "1.0.0", Platform: "vector", Roles: roles("receiver"),
		Capabilities: []string{wire.E2ECapability}, KeepaliveSeconds: 30, E2EKey: watch.Dev.Key()})
	en := w.expect(wire.TypeEnrolled).(*wire.Enrolled)
	if !hasString(en.Features, wire.FeatureSealed) {
		failf("register: %v", en.Features)
	}
	w.auth()
	w.close()

	h := r.open(tr, "phone-hello", "tls", "a later stream of the phone: hello answers welcome, and welcome lists `sealed` as enrolled does")
	h.send(&wire.Hello{DeviceID: phone.ID, DeviceToken: phoneToken, ProductVersion: "1.0.0", KeepaliveSeconds: 30})
	if wl := h.expect(wire.TypeWelcome).(*wire.Welcome); !hasString(wl.Features, wire.FeatureSealed) {
		failf("welcome: %v", wl.Features)
	}
	h.auth()
	h.close()
	return tr
}

// sealedMember is the `sealed` member of frames-v2.json: the devices of e2e-v1.json by name, and every box.
func sealedMember() sealedFile {
	f := sealedFile{
		Section: "27",
		Keys:    "e2e-v1.json (DEVICE-EXCHANGE vectors, 27 items DF to DP): the private key of every device named here",
		Notes:   sealedNotes(),
		Cases:   sealedCases,
	}
	names := make([]string, 0, len(e2eDevices))
	for n := range e2eDevices {
		names = append(names, n)
	}
	sort.Strings(names)
	for _, n := range names {
		f.Devices = append(f.Devices, sealedDevice{Name: n, E2EKey: *e2eDevices[n].Key()})
	}
	return f
}

// scenarioSealedFolder: items DI (a sealed access), DK (a sealed broadcast record) and DG (a recipient that
// leaves).
func scenarioSealedFolder() *transcript {
	r := newRig("sealed-folder", rigOpts{sealed: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("sealed-folder",
		"A producer that seals publishes a folder with `sealedAccess` - a box with the context `access:<resourceId>` addressed to every device of "+
			"the account that holds the capability - and no `access` (item DI), and a live broadcast whose endpoints carry `seal` and whose record carries "+
			"`sealed`, a box with the context `broadcast:<broadcastId>` (item DK). The server stores the box where it stored the clear member, delivers "+
			"it to the account's devices in `changed` and `directory`, and never opens it. A device that joins, or is revoked, changes the recipients: the "+
			"producer publishes and updates again with a new box (items DI and DK, item DG).")

	a, tablet, _ := r.enrollKeyed(tr, "tablet-control", account, "Vector tablet", "fms-android", roles("resource-consumer"), e2eDevices["tablet"])
	a.send(&wire.Subscribe{})
	r.online(account, tablet.ID)
	r.subscribed(1)
	w, watch, _ := r.enrollKeyed(tr, "watch-enroll", account, "Vector watch", "fms-wear", roles("receiver"), e2eDevices["watch"])
	a.expect(wire.TypeChanged)
	w.close()
	p, phone, _ := r.enrollKeyed(tr, "phone-control", account, "Vector phone", "fms-android",
		roles("resource-producer", "broadcaster"), e2eDevices["phone"])
	a.expect(wire.TypeChanged)
	r.control(p, phone.ID)
	a.expect(wire.TypeChanged)
	tr.note("the producer is online: its sealed access is delivered while it is")

	// the folder: no access, a sealed one in its place
	res := r.newID()
	publish := func(label string, to []party) {
		box, _ := r.seal(tr, label, "access:"+res, phone, to, `{"descriptor":"FMSSFTP2:vector-not-a-secret","root":"/DCIM"}`)
		p.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(
			`{"resourceId":%q,"kind":"sftp-share","name":"Camera roll","hostKeyFingerprint":%q,"root":"/DCIM","sealedAccess":%s}`,
			res, sealedFP("A"), box))})
		p.expect(wire.TypePublished)
		ch := a.expect(wire.TypeChanged).(*wire.Changed)
		requireMembers("the sealed folder in changed", ch.Upserts.Resources[0], []string{"sealedAccess", "hostKeyFingerprint", "root"}, []string{"access"})
	}
	publish("access", []party{tablet, watch})
	tr.note("the record carries `sealedAccess` and no `access`; hostKeyFingerprint and root stay in clear: they are the resource's identity, not secrets (item BA)")

	// the broadcast: endpoints with a seal, a record with a box
	bid := r.newID()
	liveRecord := func(label string, to []party, title string) json.RawMessage {
		box, _ := r.seal(tr, label, "broadcast:"+bid, phone, to, fmt.Sprintf(`{"relayKey":%q,"tunnelPin":%q}`, sealedRelayKey, sealedFP("B")))
		return json.RawMessage(fmt.Sprintf(
			`{"broadcastId":%q,"mode":"VIDEO_AUDIO","title":%q,"descriptor":{"endpoints":[`+
				`{"transport":"RTSP","url":"rtsp://192.168.1.97:8554/"},`+
				`{"transport":"RELAY","url":"https://%s/v2/b/%s/stream","targetLatencyMs":2000,"seal":{"v":1}},`+
				`{"transport":"TUNNEL","url":"fmsx://%s/b/%s/rtsp","inner":"rtsp://192.168.1.97:8554/","targetLatencyMs":2000,"seal":{"v":1}}]},"sealed":%s}`,
			bid, title, publicEndpoint, bid, publicEndpoint, bid, box))
	}
	p.send(&wire.BroadcastStart{Broadcast: liveRecord("broadcast", []party{tablet, watch}, "Desk"), MaxListeners: intPtr(4)})
	p.expect(wire.TypeBroadcastStarted)
	ch := a.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the sealed broadcast in changed", ch.Upserts.Broadcasts[0], []string{"sealed", "descriptor"}, []string{"credentials"})
	tr.note("the LAN endpoint has no seal (it does not pass through the server); RELAY and TUNNEL carry `seal: {v:1}` and no key; the keys are in the box (item DK)")

	// a device joins the account: the producer publishes and updates again, with a box that addresses it too
	s, stranger, _ := r.enrollKeyed(tr, "third-device-enroll", account, "Vector laptop", "fms-windows", roles("resource-consumer"), e2eDevices["stranger"])
	a.expect(wire.TypeChanged)
	s.close()
	publish("access-device-joined", []party{tablet, watch, stranger})
	tr.note("a device joined, so the producer publishes again with a new box that addresses it (item DI): the server replaces the stored box")
	p.send(&wire.BroadcastUpdate{Broadcast: liveRecord("broadcast-device-joined", []party{tablet, watch, stranger}, "Desk"), MaxListeners: intPtr(4)})
	a.expect(wire.TypeChanged)
	tr.note("broadcast-update replaces the record with a new box; it is not answered (item DK)")

	// a device is revoked: it stops being a recipient the moment the removal arrives (item DG)
	p.send(&wire.Revoke{DeviceID: watch.ID})
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	if len(ch.Removals.DeviceIDs) != 1 || ch.Removals.DeviceIDs[0] != watch.ID {
		failf("revoke: %+v", ch.Removals)
	}
	tr.note("the removal is the producer's cue: it seals no more to the watch")
	publish("access-device-revoked", []party{tablet, stranger})
	p.send(&wire.BroadcastUpdate{Broadcast: liveRecord("broadcast-device-revoked", []party{tablet, stranger}, "Desk"), MaxListeners: intPtr(4)})
	a.expect(wire.TypeChanged)

	a.send(&wire.List{})
	dir := a.expect(wire.TypeDirectory).(*wire.Directory)
	if len(dir.Resources) != 1 || len(dir.Broadcasts) != 1 {
		failf("directory: %d resources, %d broadcasts", len(dir.Resources), len(dir.Broadcasts))
	}
	requireMembers("the sealed folder in directory", dir.Resources[0], []string{"sealedAccess"}, []string{"access"})
	requireMembers("the sealed broadcast in directory", dir.Broadcasts[0], []string{"sealed"}, []string{"credentials"})
	tr.note("the directory answers the boxes as the producer sent them, byte for byte")
	return tr
}

// scenarioSealedGrant: item DJ - a member account gives a folder and a broadcast to a guest account with a
// box addressed to the guest's devices, replaces the box when the guest's devices change, and takes it back.
func scenarioSealedGrant() *transcript {
	r := newRig("sealed-grant", rigOpts{sealed: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("sealed-grant",
		"A device of a member account gives one folder or one live broadcast to a guest account (DEVICE-EXCHANGE 20 item BD, 27 item DJ): `grant` "+
			"carries `sealed`, a box with the context `grant:<grantId>` addressed to the guest's devices, in place of `access` or `credentials`. The "+
			"member's own devices see the grant in `changed`, with the guest's devices - `{deviceId, deviceName, e2eKey}`, public values the server already "+
			"holds; the guest's devices see the granted record with the box and the `grantId`, never the owner's own `access` or boxes. When the guest's "+
			"devices change the server pushes the member's grants again and a device sends `grant` with the same `grantId` and a new box, which costs the "+
			"member no revision. `ungrant` takes a grant back and is not answered.")
	if _, _, err := r.env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		failf("guest account: %v", err)
	}

	a, tablet, _ := r.enrollKeyed(tr, "tablet-control", account, "Vector tablet", "fms-android", roles("resource-consumer"), e2eDevices["tablet"])
	a.send(&wire.Subscribe{})
	r.online(account, tablet.ID)
	r.subscribed(1)
	p, phone, _ := r.enrollKeyed(tr, "phone-control", account, "Vector phone", "fms-android",
		roles("resource-producer", "broadcaster"), e2eDevices["phone"])
	a.expect(wire.TypeChanged)
	r.control(p, phone.ID)
	a.expect(wire.TypeChanged)

	res, bid := r.newID(), r.newID()
	access, _ := r.seal(tr, "access", "access:"+res, phone, []party{tablet}, `{"descriptor":"FMSSFTP2:vector-not-a-secret","root":"/DCIM"}`)
	p.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(
		`{"resourceId":%q,"kind":"sftp-share","name":"Camera roll","hostKeyFingerprint":%q,"root":"/DCIM","sealedAccess":%s}`, res, sealedFP("A"), access))})
	p.expect(wire.TypePublished)
	a.expect(wire.TypeChanged)
	bbox, _ := r.seal(tr, "broadcast", "broadcast:"+bid, phone, []party{tablet}, fmt.Sprintf(`{"relayKey":%q,"tunnelPin":%q}`, sealedRelayKey, sealedFP("B")))
	p.send(&wire.BroadcastStart{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Kitchen","descriptor":{"endpoints":[{"transport":"RELAY","url":"https://%s/v2/b/%s/stream","seal":{"v":1}}]},"sealed":%s}`,
		bid, publicEndpoint, bid, bbox))})
	p.expect(wire.TypeBroadcastStarted)
	a.expect(wire.TypeChanged)

	g, guest, _ := r.enrollKeyed(tr, "guest-control", "visitor", "Vector guest phone", "fms-android", roles("resource-consumer"), e2eDevices["guest"])
	g.send(&wire.Subscribe{})
	r.online("visitor", guest.ID)
	r.subscribed(2)

	// the folder is given: the box is the guest's, with the context grant:<grantId>
	g1 := r.newID()
	gbox, _ := r.seal(tr, "grant-access", "grant:"+g1, phone, []party{guest}, `{"access":{"descriptor":"FMSSFTP2:vector-guest-not-a-secret","root":"/DCIM"}}`)
	p.send(&wire.Grant{GrantID: g1, Guest: "visitor", ResourceID: res, Sealed: gbox})
	p.expect(wire.TypeGranted)
	tr.note("granted answers with the grantId; the guest's devices and the member's own are told below")
	ch := a.expect(wire.TypeChanged).(*wire.Changed)
	if len(ch.Upserts.Grants) != 1 {
		failf("the member's grants: %d", len(ch.Upserts.Grants))
	}
	requireMembers("the member's grant", ch.Upserts.Grants[0], []string{"grantId", "guest", "resourceId", "devices"}, []string{"sealed", "access", "credentials"})
	tr.note("the member's devices see the grant with the guest's devices and their public keys, and no secret; they seal anew when the guest's devices change")
	gc := g.expect(wire.TypeChanged).(*wire.Changed)
	if len(gc.Upserts.Resources) != 1 {
		failf("the guest's resources: %d", len(gc.Upserts.Resources))
	}
	requireMembers("the granted record", gc.Upserts.Resources[0], []string{"grantId", "sealed"}, []string{"access", "sealedAccess"})
	tr.note("the guest sees the granted record with the box and the grantId, in place of the owner's access and of the owner's own box")
	g.send(&wire.List{})
	gd := g.expect(wire.TypeDirectory).(*wire.Directory)
	if len(gd.Resources) != 1 || len(gd.Devices) != 1 {
		failf("the guest's directory: %d resources, %d devices", len(gd.Resources), len(gd.Devices))
	}
	requireMembers("the granted record in the guest's directory", gd.Resources[0], []string{"grantId", "sealed"}, []string{"access", "sealedAccess"})

	// the guest gets a second device: the member's grants are pushed again with both devices
	s2, guest2, _ := r.enrollKeyed(tr, "guest-second-enroll", "visitor", "Vector guest tablet", "fms-android", roles("resource-consumer"), e2eDevices["stranger"])
	g.expect(wire.TypeChanged)
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	var entry struct {
		Devices []json.RawMessage `json:"devices"`
	}
	if len(ch.Upserts.Grants) != 1 || json.Unmarshal(ch.Upserts.Grants[0], &entry) != nil || len(entry.Devices) != 2 {
		failf("the grants pushed again: %+v", ch.Upserts.Grants)
	}
	tr.note("the guest's devices changed: the server pushes the member's grants again, now with both devices, so that a device of the member can seal for the new one")
	s2.close()

	// a grant that names a grantId the account holds replaces that grant's box and changes nothing else
	gbox2, _ := r.seal(tr, "grant-access-replaced", "grant:"+g1, phone, []party{guest, guest2}, `{"access":{"descriptor":"FMSSFTP2:vector-guest-not-a-secret","root":"/DCIM"}}`)
	p.send(&wire.Grant{GrantID: g1, Guest: "visitor", ResourceID: res, Sealed: gbox2})
	p.expect(wire.TypeGranted)
	gc = g.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the replaced granted record", gc.Upserts.Resources[0], []string{"grantId", "sealed"}, []string{"access"})
	a.send(&wire.Keepalive{})
	a.expect(wire.TypeKeepalive)
	tr.note("a replacing grant changes nothing the member sees (no changed, no revision); the guest receives the record with the new box (item DJ)")

	// the broadcast is given: the payload is {credentials, relayKey, tunnelPin}
	g2 := r.newID()
	bgrant, _ := r.seal(tr, "grant-broadcast", "grant:"+g2, phone, []party{guest, guest2},
		fmt.Sprintf(`{"credentials":{"user":"listener","password":"vector-listener-pw"},"relayKey":%q,"tunnelPin":%q}`, sealedRelayKey, sealedFP("B")))
	p.send(&wire.Grant{GrantID: g2, Guest: "visitor", BroadcastID: bid, Sealed: bgrant})
	p.expect(wire.TypeGranted)
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the member's broadcast grant", ch.Upserts.Grants[0], []string{"grantId", "guest", "broadcastId", "devices"}, nil)
	gc = g.expect(wire.TypeChanged).(*wire.Changed)
	requireMembers("the granted broadcast", gc.Upserts.Broadcasts[0], []string{"grantId", "sealed"}, []string{"credentials"})

	// what a grant is refused for
	p.send(&wire.Grant{GrantID: r.newID(), Guest: "visitor", ResourceID: r.newID(), Sealed: gbox})
	p.expectRefused(wire.ReasonUnavailable)
	tr.note("a resource the account does not hold: unavailable, the answer of everything a grant cannot name")
	p.send(&wire.Grant{GrantID: r.newID(), Guest: account, ResourceID: res, Sealed: gbox})
	p.expectRefused(wire.ReasonUnavailable)
	tr.note("a login that is not a guest account is the same answer as a login that does not exist")
	p.send(&wire.Grant{GrantID: r.newID(), Kind: wire.GrantKindLink, ResourceID: res})
	p.expectRefused(wire.ReasonUnavailable)
	tr.note("a link with a password is parsed so that it can be answered; this server serves none, and a folder link is end to end already (item DJ)")
	g.send(&wire.Grant{GrantID: r.newID(), Guest: "visitor", ResourceID: res, Sealed: gbox})
	g.expectRefused(wire.ReasonNotPermitted)
	tr.note("a guest account gives nothing; its stream stays open after the refusal")

	// the folder is taken back
	p.send(&wire.Ungrant{GrantID: g1})
	tr.note("ungrant is not answered")
	gc = g.expect(wire.TypeChanged).(*wire.Changed)
	if len(gc.Removals.ResourceIDs) != 1 || gc.Removals.ResourceIDs[0] != res {
		failf("the guest's removals: %+v", gc.Removals)
	}
	ch = a.expect(wire.TypeChanged).(*wire.Changed)
	if len(ch.Removals.GrantIDs) != 1 || ch.Removals.GrantIDs[0] != g1 {
		failf("the member's removals: %+v", ch.Removals)
	}
	tr.note("the guest is told the record is gone; the member's devices are told the grant is")
	return tr
}

// scenarioSealedClipboard: item DN - a sealed value is `kind` sealed, `size` the sealed bytes and `sealed` the
// box; the server stores the bytes and the box and answers both.
func scenarioSealedClipboard() *transcript {
	r := newRig("sealed-clipboard", rigOpts{sealed: true, logins: []string{account},
		params: func(p *rendezvousParams) { p.ClipboardProbe = 100 * time.Millisecond }})
	defer mustClose(r)
	tr := r.newTranscript("sealed-clipboard",
		"The sealed clipboard of DEVICE-EXCHANGE 27 item DN on the slot of section 22: a put declares `kind` `sealed`, `size` the number of sealed bytes "+
			"and `sealed` the box with the context `clipboard` (its payload holds kind, mimeType, size and sha256 of the clear value; a sealed put carries "+
			"no mimeType and no name in clear); the sealed bytes follow `clipboard-ready` raw; the server counts, hashes and stores the sealed bytes and "+
			"answers `clipboard-state` and `clipboard-value` with `kind` `sealed` and the box, byte for byte. A sealed put with no box, or one that names the value in clear, is not an envelope the codec reads: it is malformed, the stream is "+
			"closed with no answer, and no transcript can hold it.")

	a, phone, aTok := r.enrollKeyed(tr, "phone-control", account, "Vector phone", "fms-android", roles("resource-consumer"), e2eDevices["phone"])
	r.control(a, phone.ID)
	b, tablet, bTok := r.enrollKeyed(tr, "tablet-enroll", account, "Vector tablet", "fms-android", roles("resource-consumer"), e2eDevices["tablet"])
	b.close()

	a.send(&wire.ClipboardInfo{})
	if st := a.expect(wire.TypeClipboardState).(*wire.ClipboardState); st.Value != nil {
		failf("the clipboard should start empty")
	}

	clear := []byte("A sealed clipboard text: only the devices of the account read it.")
	sum := sha256.Sum256(clear)
	box, ck := r.seal(tr, "clipboard", "clipboard", phone, []party{tablet},
		fmt.Sprintf(`{"kind":"text","mimeType":"text/plain","size":%d,"sha256":%q}`, len(clear), hex.EncodeToString(sum[:])))
	sealed := sealClipboard(ck, clear)
	ssum := sha256.Sum256(sealed)
	sealedCases[len(sealedCases)-1].Clipboard = &sealedClip{Clear: string(clear), ClearSHA256: hex.EncodeToString(sum[:]),
		SealedSize: len(sealed), SealedSHA256: hex.EncodeToString(ssum[:])}

	put := r.hello(tr, "put", "the sender's data stream: clipboard-put declares kind sealed, the sealed size and the box", phone.ID, aTok)
	put.send(&wire.ClipboardPut{Kind: wire.ClipKindSealed, Size: int64(len(sealed)), Sealed: box})
	put.expect(wire.TypeClipboardReady)
	put.stopRecording()
	put.sendRaw(fmt.Sprintf("exactly the %d sealed bytes follow clipboard-ready: raw, as for any value", len(sealed)), sealed)
	put.halfClose()
	put.startRecording()
	stored := put.expect(wire.TypeClipboardStored).(*wire.ClipboardStored)
	if stored.SHA256 != hex.EncodeToString(ssum[:]) {
		failf("the server hashed %s, the sealed bytes are %x", stored.SHA256, ssum)
	}
	tr.note("the SHA-256 is that of the sealed bytes: the server never sees the clear value")

	a.send(&wire.ClipboardInfo{})
	st := a.expect(wire.TypeClipboardState).(*wire.ClipboardState)
	if st.Value == nil || st.Value.Kind != wire.ClipKindSealed || st.Value.Size != int64(len(sealed)) || !sameJSON(st.Value.Sealed, box) {
		failf("clipboard-state: %+v", st)
	}
	requireMembers("a sealed clipboard-state", mustMarshal(st.Value), []string{"clipId", "sha256", "storedAt", "sourceDeviceId", "kind", "sealed"}, []string{"mimeType", "name"})
	tr.note("clipboard-state carries kind sealed and the box, and no mimeType and no name: the type of the value is inside the box")

	get := r.hello(tr, "get", "a reader's data stream: clipboard-get", tablet.ID, bTok)
	get.send(&wire.ClipboardGet{})
	val := get.expect(wire.TypeClipboardValue).(*wire.ClipboardValue)
	if val.Kind != wire.ClipKindSealed || val.Size != int64(len(sealed)) || !sameJSON(val.Sealed, box) || val.SHA256 != stored.SHA256 {
		failf("clipboard-value: %+v", val)
	}
	get.stopRecording()
	if got := get.readRaw(fmt.Sprintf("exactly the %d sealed bytes follow the members; the server closes the stream after them", len(sealed)), len(sealed)); !bytes.Equal(got, sealed) {
		failf("the sealed bytes came back changed")
	}
	get.expectClosed()

	return tr
}

func sameJSON(a, b json.RawMessage) bool {
	var x, y bytes.Buffer
	if json.Compact(&x, a) != nil || json.Compact(&y, b) != nil {
		return false
	}
	return bytes.Equal(x.Bytes(), y.Bytes())
}

func mustMarshal(v any) json.RawMessage {
	b, err := json.Marshal(v)
	if err != nil {
		failf("%v", err)
	}
	return b
}
