package main

import (
	"bytes"
	"encoding/json"
	"fmt"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The schedule transcript of DEVICE-EXCHANGE 1.15 (ticket L0338, items FQ to FV): the frames `schedule-request`
// and `schedule-result`. It is printed by `-schedule` and is NOT part of frames-v2.json: that file is the adopted
// 1.1 corpus and keeps its bytes, exactly as the settings and the notes transcripts do. Its server advertises
// `sealed` AND `schedule` (rigOpts.sealed, rigOpts.schedule); no older scenario turns the forwarder on.
//
// The server never opens a box and keeps nothing (item FQ). The boxes of the round trip are real (sealer.go): the
// request opens, with the private key of the target, under the context `schedule:<requestId>`, the answer opens,
// with the private key of the requester, under `schedule-result:<requestId>`. The boxes of the frames the server
// refuses before it forwards anything are padding boxes: no device ever reads them.

// scheduleContractVersion is the version whose items this file carries: items FQ to FV (1.15).
const scheduleContractVersion = "1.15"

// The requests of the scenario: 1 to 64 characters of [A-Za-z0-9_-] (item FQ), chosen by the requester.
const (
	reqGuest       = "sched-guest"
	reqUnknown     = "sched-unknown"
	reqUnsupported = "sched-unsupported"
	reqPlaces      = "sched-places"
	reqList        = "sched-list"
	reqOffline     = "sched-offline"
	reqOrphan      = "sched-orphan"
)

// placeCamera is a resource id of the answer to the `places` verb (item FR): a lower-case UUID.
const placeCamera = "9d2f6a41-7c3e-4b58-a0e1-3f5b8c27d904"

// unreadBox is the box of a frame no device ever reads: the server forwards or refuses without looking at it.
var unreadBox = json.RawMessage(`{"ct":"AAAA"}`)

// scheduleFile is what `-schedule` prints: the transcript, and the boxes it carries with what they open to.
type scheduleFile struct {
	Contract        string        `json:"contract"`
	ContractVersion string        `json:"contractVersion"`
	Sections        string        `json:"sections"`
	Producer        string        `json:"producer"`
	Notes           []string      `json:"notes"`
	Transcripts     []*transcript `json:"transcripts"`
	Sealed          sealedFile    `json:"sealed"`
}

func scheduleNotes() []string {
	return []string{
		"The transcript was produced by the real exchange server (engine, directory, accounts) over TLS on loopback, with seeded id and token sources and the fixed device keys of e2e-v1.json. The clock never moves. The bytes are a function of the code and nothing else. It is a separate file: frames-v2.json is the adopted 1.1 corpus and does not change.",
		"A step is one frame as it crossed the stream above TLS: `json` is the body, `hex` is the whole frame (the 4-byte big-endian length, then the body), in the order the script read or wrote it.",
		"The server lists `schedule` in `enrolled.features` because it lists `sealed` and its forwarder is on (item FQ). The target declares the capability `schedule-v1` together with `e2e-v1`; a requester declares nothing.",
		"The order of the server's checks is the one of item FQ, and every outcome of it is a `schedule-result` that carries `requestId`, never a bare `refused`: a `guest` account (`not-permitted`), then a `toDeviceId` that is not a device of the account (`unavailable`), then a device that does not declare `schedule-v1` (`unsupported`), then presence (`unavailable` for an offline device, the same word as for an unknown id, so that the frame does not tell whether an id exists). The refusing result carries `fromDeviceId` (the device that was asked), `requestId` and `reason`, in clear and with no `box`.",
		"A request that passes every check is forwarded unchanged to the control stream of `toDeviceId`, with `fromDeviceId` added (the requester). The answering device sends `schedule-result` with `toDeviceId` (the requester), `requestId` and its box; the server forwards it to the requester with `fromDeviceId` (the answering device) in place of `toDeviceId`. A requester matches an answer by `requestId` and `fromDeviceId`. Two requests may be in flight at once, and the answers may come in any order.",
		"A `schedule-result` from a device whose `toDeviceId` is not an online device of the account is dropped with no answer: the next frame the sender reads is the echo of its own `keepalive`.",
		"The boxes of `sealed.cases` are the request and the answers of the round trip (contexts `schedule:<requestId>` and `schedule-result:<requestId>`). The boxes of the other frames are padding boxes ({\"ct\":\"AAAA\"}): the server never opens, checks or changes a box (item DH), so the refusals and the forwarding are the same for any object.",
		"Still not in this file, because a transcript has one server and one life: a frame with a missing or mistyped member (the server closes the stream and sends no answer, item EO), the frames while the feature is off, and the feature listed only together with `sealed`. They are tests of the server (internal/rendezvous, internal/wire).",
	}
}

func scheduleSealedNotes() []string {
	return []string{
		"Every box here was sealed by the generator (cmd/exchange-vectors/sealer.go) over the fixed keys of the shared vector file e2e-v1.json, with the ephemeral keys, the content keys and the nonces drawn from seeded sources. The exchange server did not make, open or check any of them (item DH): it forwarded them.",
		"A `device` names a device of e2e-v1.json (`devices[].name`): that file holds its private key. A replaying product opens the `box` as a recipient of `to` with that key and the `context` given here (built from the `requestId` of the frame, never read from the box), trusting the key of `from` for the sender; the result must be the `payload`. A request is addressed to the one device `toDeviceId`, an answer to the requester alone.",
		"`deviceId` is the id this transcript's server knew the device by: the `to` of the box's `keys` entries and the `from` of the box.",
	}
}

// scheduleDocument is the file `-schedule` prints.
func scheduleDocument(tr *transcript, cases []sealedCase) scheduleFile {
	f := scheduleFile{
		Contract: contractID, ContractVersion: scheduleContractVersion, Sections: "7.2, 7.4, items FQ to FV", Producer: producerName,
		Notes: scheduleNotes(), Transcripts: []*transcript{tr},
		Sealed: sealedFile{
			Section: "27 (item DH); the contexts of a schedule are schedule:<requestId> and schedule-result:<requestId> (item FR)",
			Keys:    "e2e-v1.json (DEVICE-EXCHANGE vectors, 27 items DF to DP): the private key of every device named here",
			Notes:   scheduleSealedNotes(),
			Cases:   cases,
		},
	}
	for _, n := range []string{"phone", "tablet"} {
		f.Sealed.Devices = append(f.Sealed.Devices, sealedDevice{Name: n, E2EKey: *e2eDevices[n].Key()})
	}
	return f
}

// enrollScheduleDevice enrolls a keyed device and checks that the server lists `schedule`. A device that answers
// schedule requests also declares `schedule-v1`; the stream comes back authenticated, its role not yet fixed.
func (r *rig) enrollScheduleDevice(tr *transcript, id, name, product string, rl []string, dev e2eDevice, answers bool) (*sess, party) {
	purpose := "enroll with the account's password, declaring the capability e2e-v1 and the device key (item DF)"
	caps := []string{wire.E2ECapability}
	if answers {
		purpose = "enroll with the account's password, declaring the capabilities e2e-v1 and schedule-v1 (item FQ) and the device key (item DF)"
		caps = append(caps, wire.CapabilitySchedule)
	}
	s := r.open(tr, id, "tls", purpose)
	p := party{Dev: dev, ID: r.newID()}
	s.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: p.ID, DeviceName: name, Product: product,
		ProductVersion: "1.0.0", Platform: "vector", Roles: rl, Capabilities: caps, KeepaliveSeconds: 30, E2EKey: dev.Key()})
	en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
	if !hasString(en.Features, wire.FeatureSealed) || !hasString(en.Features, wire.FeatureSchedule) {
		failf("%s: the server lists %v, want sealed and schedule", id, en.Features)
	}
	s.auth()
	return s, p
}

// scenarioSchedule is the server side of the remote schedule routing (DEVICE-EXCHANGE 1.15, items FQ to FV): a
// guest is refused, a request meets an unknown device, a device without `schedule-v1` and an offline one, two
// requests are forwarded and answered in the opposite order, and a result for a device that is not online is
// dropped. It returns the transcript and the boxes it carries.
func scenarioSchedule() (*transcript, []sealedCase) {
	r := newRig("schedule", rigOpts{sealed: true, schedule: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("schedule",
		"The routing of schedule requests between the devices of one account (DEVICE-EXCHANGE 1.15, items FQ to FV): a device sends "+
			"schedule-request with the requestId, the target and a sealed box; the server forwards it unchanged to the target's control "+
			"stream, adding fromDeviceId, and forwards the target's schedule-result the other way. The server opens nothing and keeps nothing. "+
			"Every outcome of its own is a schedule-result with a reason: not-permitted (a guest), unavailable (an unknown or an offline "+
			"device) or unsupported (a device without schedule-v1). `enrolled.features` carries `schedule` because the server lists `sealed` "+
			"and its forwarder is on.")
	var cases []sealedCase

	// seal makes the box of one frame for one device and records it with the clear payload.
	seal := func(name, context string, from, to party, payload string) json.RawMessage {
		box, _ := sealBox(tr.Name+"/"+name, from, []party{to}, context, payload)
		cases = append(cases, sealedCase{Name: tr.Name + "/" + name, Transcript: tr.Name, Context: context, From: from.view(),
			To: []sealedParty{to.view()}, Payload: json.RawMessage(payload), Box: box})
		return box
	}
	wantResult := func(what string, e wire.Envelope, from, requestID, reason string) {
		res := e.(*wire.ScheduleResult)
		if res.FromDeviceID != from || res.RequestID != requestID || res.Reason != reason || len(res.Box) != 0 || res.ToDeviceID != "" {
			failf("%s: %+v", what, res)
		}
	}
	wantForwarded := func(what string, e wire.Envelope, from party, to party, requestID string, box json.RawMessage) {
		req := e.(*wire.ScheduleRequest)
		if req.FromDeviceID != from.ID || req.ToDeviceID != to.ID || req.RequestID != requestID || !bytes.Equal(req.Box, box) {
			failf("%s: %+v", what, req)
		}
	}
	wantAnswer := func(what string, e wire.Envelope, from party, requestID string, box json.RawMessage) {
		res := e.(*wire.ScheduleResult)
		if res.FromDeviceID != from.ID || res.ToDeviceID != "" || res.RequestID != requestID || res.Reason != "" || !bytes.Equal(res.Box, box) {
			failf("%s: %+v", what, res)
		}
	}
	echo := func(s *sess) {
		s.send(&wire.Keepalive{})
		s.expect(wire.TypeKeepalive)
	}

	// --- the devices: the requester, the target, a device that cannot answer ---
	a, phone := r.enrollScheduleDevice(tr, "phone-control", "Vector phone", "fms-web", roles("resource-consumer"), e2eDevices["phone"], false)
	r.control(a, phone.ID)
	b, tablet := r.enrollScheduleDevice(tr, "tablet-control", "Vector tablet", "fms-windows", roles("resource-producer"), e2eDevices["tablet"], true)
	r.control(b, tablet.ID)
	w, watch := r.enrollScheduleDevice(tr, "watch-control", "Vector watch", "fms-wear", roles("receiver"), e2eDevices["watch"], false)
	r.control(w, watch.ID)
	tr.note("`enrolled.features` lists `schedule` with `sealed`; only the tablet declares `schedule-v1` (with `e2e-v1`), the phone is the requester and declares nothing, the watch holds a key but cannot answer")

	// --- a guest: item AO, checked before anything else ---
	if _, _, err := r.env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		failf("guest account: %v", err)
	}
	g, gDev, _ := r.enroll(tr, "guest-control", "visitor", "Vector guest", "fms-web", roles("resource-consumer"), "")
	r.controlAs("visitor", g, gDev)
	g.send(&wire.ScheduleRequest{ToDeviceID: tablet.ID, RequestID: reqGuest, Box: unreadBox})
	wantResult("the guest's request", g.expect(wire.TypeScheduleResult), tablet.ID, reqGuest, wire.ScheduleReasonNotPermitted)
	tr.note("item AO: a `guest` account is answered with a schedule-result whose reason is `not-permitted`, not with a `refused`; `fromDeviceId` is the device that was asked and nothing is forwarded")

	// --- the target is not a device of the account ---
	ghost := r.newID()
	a.send(&wire.ScheduleRequest{ToDeviceID: ghost, RequestID: reqUnknown, Box: unreadBox})
	wantResult("the request for an unknown device", a.expect(wire.TypeScheduleResult), ghost, reqUnknown, wire.ScheduleReasonUnavailable)
	tr.note("a `toDeviceId` that is not a device of the account is `unavailable`: the same answer as for an offline device, so the frame does not tell whether an id exists")

	// --- a device of the account that does not declare schedule-v1 ---
	a.send(&wire.ScheduleRequest{ToDeviceID: watch.ID, RequestID: reqUnsupported, Box: unreadBox})
	wantResult("the request for a device without schedule-v1", a.expect(wire.TypeScheduleResult), watch.ID, reqUnsupported, wire.ScheduleReasonUnsupported)
	tr.note("a device that is online but did not declare `schedule-v1` is `unsupported`; nothing reaches the watch")
	echo(w)
	tr.note("the watch's stream was told nothing: the next frame it reads is the echo of its own keepalive")

	// --- two requests in flight, answered in the opposite order ---
	placesBox := seal("request-places", "schedule:"+reqPlaces, phone, tablet, `{"v":1,"verb":"places"}`)
	listBox := seal("request-list", "schedule:"+reqList, phone, tablet, `{"v":1,"verb":"list"}`)
	a.send(&wire.ScheduleRequest{ToDeviceID: tablet.ID, RequestID: reqPlaces, Box: placesBox})
	wantForwarded("the first request, forwarded", b.expect(wire.TypeScheduleRequest), phone, tablet, reqPlaces, placesBox)
	tr.note("the request is forwarded unchanged to the target's control stream with `fromDeviceId` added; `toDeviceId` and `box` are as the requester sent them")
	a.send(&wire.ScheduleRequest{ToDeviceID: tablet.ID, RequestID: reqList, Box: listBox})
	wantForwarded("the second request, forwarded", b.expect(wire.TypeScheduleRequest), phone, tablet, reqList, listBox)
	tr.note("a second request is in flight before the first is answered: these frames are not an answered request of item EF, so they do not count against the one request a control stream may await")

	listAnswer := seal("answer-list", "schedule-result:"+reqList, tablet, phone,
		`{"v":1,"outcome":"ok","operations":[],"truncated":false}`)
	b.send(&wire.ScheduleResult{ToDeviceID: phone.ID, RequestID: reqList, Box: listAnswer})
	wantAnswer("the answer to the second request", a.expect(wire.TypeScheduleResult), tablet, reqList, listAnswer)
	placesAnswer := seal("answer-places", "schedule-result:"+reqPlaces, tablet, phone,
		fmt.Sprintf(`{"v":1,"outcome":"ok","places":[{"deviceId":%q,"resourceId":%q,"name":"Camera","readOnly":false}],"truncated":false}`,
			tablet.ID, placeCamera))
	b.send(&wire.ScheduleResult{ToDeviceID: phone.ID, RequestID: reqPlaces, Box: placesAnswer})
	wantAnswer("the answer to the first request", a.expect(wire.TypeScheduleResult), tablet, reqPlaces, placesAnswer)
	tr.note("the answer goes the other way with `fromDeviceId` (the answering device) in place of `toDeviceId`, and carries a box or a reason, never both; the requester matches it by `requestId` and `fromDeviceId`, so the order of the answers is free")

	// --- a result for a device that is not online: dropped, no answer ---
	b.send(&wire.ScheduleResult{ToDeviceID: ghost, RequestID: reqOrphan, Box: unreadBox})
	echo(b)
	tr.note("a schedule-result whose `toDeviceId` is not an online device of the account is dropped with no answer: the sender has no request waiting and the server keeps nothing; the next frame the sender reads is the echo of its own keepalive")

	// --- the target goes offline ---
	b.send(&wire.Bye{})
	r.offline(account, tablet.ID)
	b.close()
	a.send(&wire.ScheduleRequest{ToDeviceID: tablet.ID, RequestID: reqOffline, Box: unreadBox})
	wantResult("the request for an offline device", a.expect(wire.TypeScheduleResult), tablet.ID, reqOffline, wire.ScheduleReasonUnavailable)
	tr.note("a device that declared `schedule-v1` but holds no control stream is `unavailable`: the server queues nothing and does not wake it (item BM)")
	return tr, cases
}
