package main

import (
	"encoding/json"
	"fmt"
	"strings"
	"time"

	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The scripted scenarios of frames-v2.json. Each runs against its own server (so one scenario's
// buckets and ids never touch another's), waits for every asynchronous effect before the next ordered
// step, and returns the transcript. A scenario that surprises the script panics with genFailure.
//
// A device's control stream is fixed by its first envelope after welcome. Where a scenario does not
// want the `changed` pushes of a subscription, the stream says keepalive first (which the server
// echoes) instead of subscribe: the transcript then holds only what the scenario is about.

const account = "alice"

type rendezvousParams = rendezvous.Params

func roles(r ...string) []string { return r }

func intPtr(n int) *int { return &n }

// subscribed waits until the directory holds n subscriptions.
func (r *rig) subscribed(n int) {
	r.until("the subscription", func() bool { return r.env.Dir.Counts().Subscribers >= n })
}

// control fixes the stream's role as a control stream with a keepalive and waits for the echo.
func (r *rig) control(s *sess, devID string) {
	s.send(&wire.Keepalive{})
	s.expect(wire.TypeKeepalive)
	r.online(account, devID)
}

// controlAs is control for a device of a login other than the scenario's default account.
func (r *rig) controlAs(login string, s *sess, devID string) {
	s.send(&wire.Keepalive{})
	s.expect(wire.TypeKeepalive)
	r.online(login, devID)
}

// scenarioEnrollAndHello: enroll with the password on the first stream, a keepalive, a bye; then a
// second stream of the same device with hello.
func scenarioEnrollAndHello() *transcript {
	r := newRig("enroll-and-hello", rigOpts{})
	defer mustClose(r)
	tr := r.newTranscript("enroll-and-hello",
		"A new device enrolls with the account's password and keeps the token; every later stream opens with hello. "+
			"The first envelope after welcome fixes the stream's role: a keepalive makes it the control stream.")
	s1, dev, token := r.enroll(tr, "s1", account, "Vector phone", "fms-android",
		roles("resource-producer", "resource-consumer", "broadcaster", "receiver"),
		`{"modes":["AUDIO_ONLY"],"transports":["RELAY","TUNNEL"]}`)
	r.control(s1, dev)
	s1.send(&wire.Bye{})
	tr.note("bye is not answered; the device goes offline")
	r.offline(account, dev)
	s1.close()

	s2 := r.hello(tr, "s2", "a later stream of the enrolled device: hello with the token", dev, token)
	r.control(s2, dev)
	s2.send(&wire.Bye{})
	r.offline(account, dev)
	s2.close()
	return tr
}

// scenarioPairing: an enrolled device asks for a one-time code, a new device enrolls with it.
func scenarioPairing() *transcript {
	r := newRig("pairing", rigOpts{})
	defer mustClose(r)
	tr := r.newTranscript("pairing",
		"An enrolled device asks for a pairing code (8 characters of Crockford base32, valid 10 minutes, single use); "+
			"a new device enrolls with the login and the code instead of the password.")
	a, aDev, _ := r.enroll(tr, "phone", account, "Vector phone", "fms-android", roles("resource-producer"), "")
	r.control(a, aDev)
	a.send(&wire.PairingRequest{})
	code := a.expect(wire.TypePairingCode).(*wire.PairingCode).Code

	w := r.open(tr, "watch", "tls", "a device that cannot type a password: enroll with the pairing code")
	w.send(&wire.Enroll{Login: account, PairingCode: code, DeviceID: r.newID(), DeviceName: "Vector watch", Product: "fms-wear",
		Roles: roles("broadcaster", "receiver"), Receiver: json.RawMessage(`{"modes":["AUDIO_ONLY"],"transports":["RELAY"]}`)})
	w.expect(wire.TypeEnrolled)
	w.auth()
	// the code is single use
	w2 := r.open(tr, "again", "tls", "the same code a second time")
	w2.send(&wire.Enroll{Login: account, PairingCode: code, DeviceID: r.newID(), DeviceName: "Another watch", Product: "fms-wear"})
	w2.expectRefused(wire.ReasonBadCredentials)
	w2.expectClosed()
	return tr
}

// scenarioRefusals produces every reason of 7.10 the way the server produces it.
func scenarioRefusals() *transcript {
	r := newRig("refusals", rigOpts{logins: []string{account, "bobby"},
		params: func(p *rendezvousParams) { p.ResourcesPerAccount = 1; p.CodesPerAccount = 1 }})
	defer mustClose(r)
	tr := r.newTranscript("refusals",
		"The refusal reasons of DEVICE-EXCHANGE 7.10 that are not about registration or passwords, produced by the real server: tls-required and "+
			"version-unsupported on a plain stream, id-collision, limit-reached, capacity, port-unavailable, unavailable, protection-unsupported, "+
			"device-revoked, bad-credentials and rate-limited. The failed-authentication bucket of this server holds 2 and does not refill during "+
			"the run, so the last three steps are the ones that spend it. (The refusals of registration, passwords and guests are in the "+
			"`registration`, `password` and `pace` transcripts.)")

	p1 := r.open(tr, "plain-enroll", "plain", "an enroll on a stream with no TLS")
	p1.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: r.newID(), DeviceName: "Plain", Product: "fms-windows"})
	p1.expectRefused(wire.ReasonTLSRequired)
	p1.expectClosed()

	p2 := r.open(tr, "legacy", "plain", "a frame of the superseded wire (schemaVersion 1): refused with the server's own version")
	p2.sendFrame(wire.LegacyProbeFrame())
	p2.expectRefused(wire.ReasonVersionUnsupported)
	p2.expectClosed()

	a, aDev, aToken := r.enroll(tr, "a-enroll", account, "Vector PC", "fms-windows", roles("resource-producer"), "")
	b := r.open(tr, "collision", "tls", "bobby enrolls a deviceId that alice's account already has")
	b.send(&wire.Enroll{Login: "bobby", Password: testkit.TestPassword, DeviceID: aDev, DeviceName: "Thief", Product: "fms-windows"})
	b.expectRefused(wire.ReasonIDCollision)
	b.expectClosed()

	a.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Unsafe protection","protection":{"scheme":"future"}}`, r.newID()))})
	a.expectRefused(wire.ReasonProtectionUnsupported)

	// a's stream becomes its control stream with a publish
	res1, res2 := r.newID(), r.newID()
	a.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"First"}`, res1))})
	a.expect(wire.TypePublished)
	a.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Second"}`, res2))})
	a.expectRefused(wire.ReasonLimitReached)
	tr.note("this server allows one resource per account: a count limit of the account is limit-reached; the stream stays open after a refusal")
	a.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"First","publicPort":0}`, res1))})
	a.expectRefused(wire.ReasonPortUnavailable)
	tr.note("this server has no third-party port pool")
	a.send(&wire.PairingRequest{})
	a.expect(wire.TypePairingCode)
	a.send(&wire.PairingRequest{})
	a.expectRefused(wire.ReasonCapacity)
	tr.note("this server holds one outstanding pairing code per account: a second request is capacity (the server cannot take more), and the first code stays valid")

	c := r.hello(tr, "unknown-id", "a connect for an id nobody holds: the one answer of every connect that cannot be served", aDev, aToken)
	c.send(&wire.Connect{ResourceID: r.newID()})
	c.expectRefused(wire.ReasonUnavailable)
	c.expectClosed()

	// a second device of the account, revoked by the first; its old token is then a revoked one
	bs, bDev, bToken := r.enroll(tr, "b-enroll", account, "Vector tablet", "fms-windows", roles("resource-consumer"), "")
	r.control(bs, bDev)
	a.send(&wire.Revoke{DeviceID: bDev})
	tr.note("revoke is not answered; the revoked device's streams are closed")
	bs.expectClosed()

	h1 := r.open(tr, "hello-revoked", "tls", "hello with the token of a revoked device")
	h1.send(&wire.Hello{DeviceID: bDev, DeviceToken: bToken})
	h1.expectRefused(wire.ReasonDeviceRevoked)
	h1.expectClosed()
	h2 := r.open(tr, "hello-wrong", "tls", "hello with a token that matches nothing: the answer cannot tell a stranger that a deviceId exists")
	h2.send(&wire.Hello{DeviceID: aDev, DeviceToken: strings.Repeat("A", 43)})
	h2.expectRefused(wire.ReasonBadCredentials)
	h2.expectClosed()
	h3 := r.open(tr, "hello-limited", "tls", "a third failure from the same address: the bucket is empty")
	h3.send(&wire.Hello{DeviceID: aDev, DeviceToken: strings.Repeat("B", 43)})
	h3.expectRefused(wire.ReasonRateLimited)
	h3.expectClosed()
	return tr
}

// scenarioDirectory: list, subscribe, changed (with a gap and a resync), publish, unpublish, revoke.
func scenarioDirectory() *transcript {
	r := newRig("directory", rigOpts{pool: true})
	defer mustClose(r)
	tr := r.newTranscript("directory",
		"The account's directory: list answers every record with a revision that grows with every change; subscribe pushes one changed per "+
			"change; a client that sees a gap in the revision lists again; publish answers with the granted third-party port; revoke removes the "+
			"device and everything it published in one changed.")
	a, aDev, aToken := r.enroll(tr, "a-control", account, "Vector PC", "fms-windows", roles("resource-consumer"), "")
	a.send(&wire.Subscribe{})
	r.online(account, aDev)
	r.subscribed(1)

	b, bDev, _ := r.enroll(tr, "b-control", account, "Vector phone", "fms-android", roles("resource-producer"), "")
	a.expect(wire.TypeChanged)
	tr.note("device B enrolled: it is listed, offline")
	b.send(&wire.List{})
	b.expect(wire.TypeDirectory)
	a.expect(wire.TypeChanged)
	tr.note("device B is online now")

	res1, share1 := r.newID(), r.newID()
	b.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(
		`{"resourceId":%q,"kind":"sftp-share","name":"Camera roll","shareId":%q,"publicPort":0,"access":{"descriptor":"FMSSFTP2:vector-not-a-secret","root":"/DCIM"}}`, res1, share1))})
	b.expect(wire.TypePublished)
	tr.note("publicPort 0 asked for any port: the answer carries the one that was granted")
	a.expect(wire.TypeChanged)
	tr.note("a consumer of the account is handed access; the record kept on disk has none")

	// A drops; two more changes happen while it is away; it comes back and lists
	a.send(&wire.Bye{})
	r.offline(account, aDev)
	a.close()
	res2 := r.newID()
	b.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Documents"}`, res2))})
	b.expect(wire.TypePublished)
	b.send(&wire.Unpublish{ResourceID: res2})
	tr.note("unpublish is not answered")
	r.until("the unpublish to take effect", func() bool { return r.env.Dir.Counts().Resources == 1 })

	a2 := r.hello(tr, "a-resync", "device A comes back: its last revision is stale, so it lists again", aDev, aToken)
	a2.send(&wire.List{})
	a2.expect(wire.TypeDirectory)
	tr.note("the revision moved by more than one since A last saw it: a gap, resolved by this list")
	a2.send(&wire.Subscribe{})
	r.subscribed(1)

	a2.send(&wire.Revoke{DeviceID: bDev})
	a2.expect(wire.TypeChanged)
	tr.note("revoking B removed the device and its resources in one transaction: one changed, one revision")
	b.expectClosed()
	return tr
}

// scenarioTunnels: connect by resourceId, by shareId (an outsider) and by broadcastId, with the
// producer's open and attach. Only the envelopes are recorded - never the bridged bytes.
func scenarioTunnels() *transcript {
	r := newRig("tunnels", rigOpts{})
	defer mustClose(r)
	tr := r.newTranscript("tunnels",
		"A consumer opens a data stream and says connect; the server sends the producer open on its control stream; the producer opens a new data "+
			"stream, says hello and attach; the server answers the consumer connected and splices the two. After connected nothing is an envelope: "+
			"the transcript stops there.")
	p, pDev, pToken := r.enroll(tr, "producer-control", account, "Vector PC", "fms-windows", roles("resource-producer", "broadcaster"), "")
	r.control(p, pDev)

	res, share := r.newID(), r.newID()
	p.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(
		`{"resourceId":%q,"kind":"sftp-share","name":"Photos","shareId":%q,"access":{"descriptor":"FMSSFTP2:vector-not-a-secret"}}`, res, share))})
	p.expect(wire.TypePublished)

	attach := func(open *wire.Open, name string) {
		d := r.hello(tr, name, "the producer's data stream for the tunnel it was told about", pDev, pToken)
		d.send(&wire.Attach{TunnelID: open.TunnelID})
		d.stopRecording()
	}

	// by resourceId, as an enrolled device of the account
	cons, cDev, cToken := r.enroll(tr, "consumer-enroll", account, "Vector tablet", "fms-windows", roles("resource-consumer"), "")
	cons.close()
	cs := r.hello(tr, "consumer-by-resource", "an enrolled consumer: hello, then connect by resourceId", cDev, cToken)
	cs.send(&wire.Connect{ResourceID: res})
	open := p.expect(wire.TypeOpen).(*wire.Open)
	attach(open, "producer-data-1")
	cs.expect(wire.TypeConnected)
	tr.note("after connected the bytes are the tunnelled service's, not envelopes")
	cs.stopRecording()

	// by shareId, as an outsider: no hello at all, TLS only
	out := r.open(tr, "outsider-by-share", "tls", "someone outside the account: connect by shareId needs no hello")
	out.send(&wire.Connect{ShareID: share})
	open = p.expect(wire.TypeOpen).(*wire.Open)
	attach(open, "producer-data-2")
	out.expect(wire.TypeConnected)
	out.stopRecording()

	// by broadcastId with a scheme
	bid := r.newID()
	p.send(&wire.BroadcastStart{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Kitchen","descriptor":{"endpoints":[{"transport":"RTSP","url":"rtsp://192.0.2.1:8554/"}]}}`, bid)), MaxListeners: intPtr(4)})
	p.expect(wire.TypeBroadcastStarted)
	ob := r.open(tr, "outsider-by-broadcast", "tls", "a listener holding the broadcast id: connect by broadcastId and scheme")
	ob.send(&wire.Connect{BroadcastID: bid, Scheme: "rtsp"})
	open = p.expect(wire.TypeOpen).(*wire.Open)
	attach(open, "producer-data-3")
	ob.expect(wire.TypeConnected)
	ob.stopRecording()
	return tr
}

// scenarioRelay: the broadcaster's upload with its chunks and the listener's HTTP exchange.
func scenarioRelay() *transcript {
	r := newRig("relay", rigOpts{relay: true})
	defer mustClose(r)
	tr := r.newTranscript("relay",
		"The fan-out relay: the broadcaster starts a broadcast, opens a data stream, says upload and receives upload-accepted; from then on the "+
			"stream carries chunks (a 4-byte length counting one flag byte plus the payload, the flag byte, the payload; a length of zero ends the "+
			"broadcast). A listener GETs /v2/b/<broadcastId>/stream inside TLS and receives the header chunk and then the stream from a joinable chunk.")
	a, aDev, aToken := r.enroll(tr, "broadcaster-control", account, "Vector PC", "fms-windows", roles("broadcaster"), "")
	r.control(a, aDev)
	bid := r.newID()
	a.send(&wire.BroadcastStart{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"VIDEO_AUDIO","title":"Desk","descriptor":{"endpoints":[{"transport":"RELAY","url":"https://%s/v2/b/%s/stream"}]}}`,
		bid, publicEndpoint, bid)), MaxListeners: intPtr(4)})
	a.expect(wire.TypeBroadcastStarted)
	// the broadcaster replaces its record (a new title) and lowers the listener cap; not answered, so a keepalive is the barrier
	a.send(&wire.BroadcastUpdate{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"VIDEO_AUDIO","title":"Desk, updated","descriptor":{"endpoints":[{"transport":"RELAY","url":"https://%s/v2/b/%s/stream"}]}}`,
		bid, publicEndpoint, bid)), MaxListeners: intPtr(3)})
	a.send(&wire.Keepalive{})
	a.expect(wire.TypeKeepalive)

	// a listener that comes before the upload is attached gets 503 with Retry-After; an unknown id is 404; HEAD is 405
	r.rawHTTP(tr, "listener-too-early", "a listener for a live broadcast whose upload is not attached yet",
		fmt.Sprintf("GET %s HTTP/1.1\r\nHost: %s\r\nAccept: */*\r\n\r\n", wire.ListenPath(bid), publicEndpoint))
	r.rawHTTP(tr, "listener-unknown", "a listener for an id nobody holds",
		fmt.Sprintf("GET %s HTTP/1.1\r\nHost: %s\r\n\r\n", wire.ListenPath(r.newID()), publicEndpoint))
	r.rawHTTP(tr, "listener-head", "HEAD is not served and costs no listener slot",
		fmt.Sprintf("HEAD %s HTTP/1.1\r\nHost: %s\r\n\r\n", wire.ListenPath(bid), publicEndpoint))

	up := r.hello(tr, "upload", "the broadcaster's data stream", aDev, aToken)
	up.send(&wire.Upload{BroadcastID: bid, ContentType: "video/mp2t"})
	up.expect(wire.TypeUploadAccepted)
	up.stopRecording()
	total := 0
	chunk := func(flags byte, payload string, note string) {
		b, err := wire.AppendChunk(nil, flags, []byte(payload))
		if err != nil {
			failf("%v", err)
		}
		tr.add(step{Stream: "upload", Direction: c2s, Note: note, Chunk: &chunkStep{Flags: int(flags), PayloadHex: hexOf([]byte(payload)), Hex: hexOf(b)}})
		if err := up.c.SendRaw(b); err != nil {
			failf("%v", err)
		}
		total += len(payload)
	}
	chunk(wire.ChunkHeader, "PAT+PMT", "a header chunk: kept, and replayed to every new listener before its first joinable chunk")
	chunk(wire.ChunkJoinable, "IDR-1", "joinable: a listener may start here")
	chunk(0, "P-2", "an ordinary chunk")
	chunk(wire.ChunkJoinable, "IDR-3", "joinable")
	chunk(0, "P-4", "an ordinary chunk")
	r.until("the chunks to reach the relay", func() bool {
		_, _, _, in, ok := r.env.Relay.Held(bid)
		return ok && in >= int64(total)
	})

	// a listener that joins now gets the header, then starts at the newest joinable chunk. The upload ends
	// right after it is admitted, so the body ends with the final chunk and the exchange is whole; the end
	// cannot overtake what the listener was handed (a listener is given the ring before it is given the end).
	done := make(chan struct{})
	go func() {
		defer close(done)
		r.rawHTTP(tr, "listener", "a listener of a running broadcast: the Range header is ignored",
			fmt.Sprintf("GET %s HTTP/1.1\r\nHost: %s\r\nAccept: */*\r\nRange: bytes=0-\r\n\r\n", wire.ListenPath(bid), publicEndpoint))
	}()
	r.until("the listener to be admitted", func() bool { return r.env.Relay.Listeners(bid) == 1 })
	end := wire.EndChunk()
	tr.add(step{Stream: "upload", Direction: c2s, Note: "a length of zero ends the broadcast cleanly: every listener's body ends with the final chunk",
		Chunk: &chunkStep{Flags: 0, PayloadHex: "", Hex: hexOf(end)}})
	if err := up.c.SendRaw(end); err != nil {
		failf("%v", err)
	}
	select {
	case <-done:
	case <-time.After(5 * time.Second):
		failf("the listener never finished")
	}
	return tr
}

// scenarioCast: cast, cast-offer, cast-answer, cast-result and cast-stop, with the refusals.
func scenarioCast() *transcript {
	r := newRig("cast", rigOpts{cast: true})
	defer mustClose(r)
	tr := r.newTranscript("cast",
		"Cast: a device asks one named receiver of the same account to play a broadcast. The server forwards cast-offer to the receiver (the "+
			"broadcast record, never access), the receiver's cast-answer to the caster as cast-result, and cast-stop to the other end. An offline "+
			"receiver is answered at once; a receiver that does not answer in 60 s is a timeout.")
	rcvBlock := `{"modes":["AUDIO_ONLY","VIDEO_AUDIO"],"transports":["RELAY","TUNNEL"]}`
	a, aDev, _ := r.enroll(tr, "caster", account, "Vector PC", "fms-windows", roles("broadcaster"), "")
	r.control(a, aDev)
	b, bDev, _ := r.enroll(tr, "receiver", account, "Vector phone", "fms-android", roles("receiver"), rcvBlock)
	r.control(b, bDev)
	off, oDev, _ := r.enroll(tr, "offline-receiver", account, "Vector watch", "fms-wear", roles("receiver"), rcvBlock)
	off.close() // enrolled, never online

	bid := r.newID()
	a.send(&wire.BroadcastStart{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Kitchen","descriptor":{"endpoints":[]}}`, bid))})
	a.expect(wire.TypeBroadcastStarted)

	cast := func(to string) { a.send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: to}) }
	// accepted
	cast(bDev)
	b.expect(wire.TypeCastOffer)
	tr.note("the offer carries the broadcast record and the caster's device id, never access")
	b.send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	if res := a.expect(wire.TypeCastResult).(*wire.CastResult); !res.Accepted {
		failf("the cast was not accepted: %+v", res)
	}
	// stop: the caster to the receiver, and the receiver by itself to the caster
	a.send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: bDev})
	b.expect(wire.TypeCastStop)
	tr.note("forwarded as it is, to the other end of the pair")
	b.send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: bDev})
	a.expect(wire.TypeCastStop)
	// declined, and unsupported
	for _, reason := range []string{wire.CastDeclined, wire.CastUnsupported} {
		cast(bDev)
		b.expect(wire.TypeCastOffer)
		b.send(&wire.CastAnswer{BroadcastID: bid, Accepted: false, Reason: reason})
		if res := a.expect(wire.TypeCastResult).(*wire.CastResult); res.Accepted || res.Reason != reason {
			failf("the cast was not %s: %+v", reason, res)
		}
	}
	// a receiver that is enrolled but offline: answered at once
	cast(oDev)
	if res := a.expect(wire.TypeCastResult).(*wire.CastResult); res.Reason != wire.CastUnavailable {
		failf("%+v", res)
	}
	tr.note("an offline receiver, a receiver of another account and an unknown one are the same answer, at once")
	// a receiver that does not answer within 60 s
	cast(bDev)
	b.expect(wire.TypeCastOffer)
	r.clock.Add(61 * time.Second)
	if res := a.expect(wire.TypeCastResult).(*wire.CastResult); res.Reason != wire.CastTimeout {
		failf("%+v", res)
	}
	tr.note("no cast-answer within 60 s: a timeout; a late answer is dropped")
	// a broadcast that is not live
	a.send(&wire.Cast{BroadcastID: r.newID(), ReceiverDeviceID: bDev})
	a.expectRefused(wire.ReasonUnavailable)
	// the caster ends the broadcast (not answered)
	a.send(&wire.BroadcastEnd{BroadcastID: bid})
	a.send(&wire.Keepalive{})
	a.expect(wire.TypeKeepalive)
	return tr
}

// scenarioRegistration: a person registers with a one-time invitation code or with the shared invitation
// password (19 items AQ and AR), and what a guest account may not do (item AV).
func scenarioRegistration() *transcript {
	r := newRig("registration", rigOpts{logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("registration",
		"Registration: a person with an invitation code (single use, no expiry) or the shared invitation password chooses a login and a password "+
			"and enrolls the first device in one frame; `enrolled` carries the kind of the new account. A code that was used, withdrawn or never "+
			"made is one answer (invitation-invalid). The invitation is consumed only by a registration that succeeded: a taken login or a weak "+
			"password costs nothing. A guest account is refused publish and broadcast-start with not-permitted; the cap on accounts is "+
			"limit-reached and leaves the invitation unused.")
	mk := func(kind string) string {
		inv, err := r.env.Ops.CreateInvitation(kind, "")
		if err != nil {
			failf("create invitation: %v", err)
		}
		return inv.Code
	}
	member, guest := mk("member"), mk("guest")
	frame := func(code, shared, login, pw string) *wire.Register {
		return &wire.Register{InvitationCode: code, SharedPassword: shared, Login: login, Password: pw, DeviceID: r.newID(),
			DeviceName: "Vector phone", Product: "fms-android", ProductVersion: "1.0.0", Platform: "vector",
			Roles: roles("resource-consumer"), KeepaliveSeconds: 30}
	}
	refused := func(id, purpose string, m *wire.Register, reason wire.Reason) {
		s := r.open(tr, id, "tls", purpose)
		s.send(m)
		s.expectRefused(reason)
		s.expectClosed()
	}
	registered := func(id, purpose string, m *wire.Register, kind string) *sess {
		s := r.open(tr, id, "tls", purpose)
		s.send(m)
		en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
		if en.AccountKind != kind {
			failf("%s: the account is %q, expected %q", id, en.AccountKind, kind)
		}
		s.auth()
		return s
	}

	refused("unknown-code", "a code nobody made: invitation-invalid, and the answer a used or a withdrawn code gets too",
		frame(r.newID(), "", "carol", testkit.TestPassword), wire.ReasonInvitationInvalid)
	refused("login-taken", "a valid code, a login that is taken: told only now that the invitation is proven",
		frame(member, "", account, testkit.TestPassword), wire.ReasonLoginTaken)
	refused("weak-password", "a valid code, a password the policy of 1.2 refuses",
		frame(member, "", "carol", "short"), wire.ReasonPasswordPolicy)
	tr.note("neither refusal consumed the invitation: the same code registers below")

	carol := frame(member, "", "carol", testkit.TestPassword)
	s := registered("member", "the first device of a new member account", carol, "member")
	s.send(&wire.Keepalive{})
	s.expect(wire.TypeKeepalive)
	r.online("carol", carol.DeviceID)
	refused("code-used", "the same code a second time: byte for byte the answer of a code nobody made",
		frame(member, "", "dave", testkit.TestPassword), wire.ReasonInvitationInvalid)

	// a guest invitation: the account is a guest and holds nothing of its own
	g := registered("guest", "a guest invitation: the account is a guest, it publishes nothing and starts no broadcast",
		frame(guest, "", "visitor", testkit.TestPassword), "guest")
	g.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Guest share"}`, r.newID()))})
	g.expectRefused(wire.ReasonNotPermitted)
	tr.note("the stream stays open after the refusal")
	g.send(&wire.BroadcastStart{Broadcast: json.RawMessage(fmt.Sprintf(
		`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Guest","descriptor":{"endpoints":[]}}`, r.newID()))})
	g.expectRefused(wire.ReasonNotPermitted)

	// the shared invitation password: off until the operator sets it, not used up, one kind for everyone who uses it
	if err := r.env.Ops.SetSharedInvitation("Shared-vector-pw-1", "member"); err != nil {
		failf("shared invitation: %v", err)
	}
	registered("shared-password", "registration by the shared invitation password: the kind is the one it was set for",
		frame("", "Shared-vector-pw-1", "erin", testkit.TestPassword), "member")

	// the cap on accounts: four accounts exist, the cap is four
	if _, err := r.env.Ops.SetPolicy(intPtr(4), nil); err != nil {
		failf("policy: %v", err)
	}
	another := mk("member")
	refused("over-cap", "the account cap is reached: limit-reached, and the invitation stays unused",
		frame(another, "", "dave", testkit.TestPassword), wire.ReasonLimitReached)
	if _, err := r.env.Ops.SetPolicy(intPtr(10), nil); err != nil {
		failf("policy: %v", err)
	}
	registered("after-cap-raised", "the cap raised: the same invitation registers", frame(another, "", "dave", testkit.TestPassword), "member")
	return tr
}

// scenarioPasswordAndSignOut: a password change signs the other devices out (their records stay), a
// signed-out device signs in again, and a person deletes their own account (19 items AT and AU).
func scenarioPasswordAndSignOut() *transcript {
	r := newRig("password", rigOpts{logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("password",
		"Changing a password needs the old one; the device that changed it keeps working with a new token and every other device of the account is "+
			"signed out: its streams are closed, its record stays, and its next hello is signed-out. A signed-out device signs in again with the new "+
			"password. Deleting the account needs the password too, is answered before the account goes, and removes everything the operator's "+
			"removal removes.")
	const newPW = "Vector-new-pw-2"
	a, aDev, _ := r.enroll(tr, "a-enroll", account, "Vector PC", "fms-windows", roles("resource-producer"), "")
	r.control(a, aDev)
	b, bDev, bToken := r.enroll(tr, "b-enroll", account, "Vector phone", "fms-android", roles("resource-consumer"), "")
	r.control(b, bDev)

	a.send(&wire.PasswordChange{OldPassword: "Wrongpass1", NewPassword: newPW})
	a.expectRefused(wire.ReasonBadCredentials)
	tr.note("a wrong old password changes nothing and signs nobody out")
	a.send(&wire.PasswordChange{OldPassword: testkit.TestPassword, NewPassword: "short"})
	a.expectRefused(wire.ReasonPasswordPolicy)
	a.send(&wire.PasswordChange{OldPassword: testkit.TestPassword, NewPassword: newPW})
	changed := a.expect(wire.TypePasswordChanged).(*wire.PasswordChanged)
	tr.note("the answer carries a new device token for the caller; its old token is dead")
	b.expectClosed()

	h := r.open(tr, "b-signed-out", "tls", "hello of the signed-out device with its old token: the record stays, the answer says it is signed out")
	h.send(&wire.Hello{DeviceID: bDev, DeviceToken: bToken})
	h.expectRefused(wire.ReasonSignedOut)
	h.expectClosed()

	r.hello(tr, "a-new-token", "the changing device says hello with its new token", aDev, changed.DeviceToken).close()

	again := r.open(tr, "b-sign-in", "tls", "the signed-out device signs in again with the new password, keeping its device id")
	again.send(&wire.Enroll{Login: account, Password: newPW, DeviceID: bDev, DeviceName: "Vector phone", Product: "fms-android",
		ProductVersion: "1.0.0", Platform: "vector", Roles: roles("resource-consumer"), KeepaliveSeconds: 30})
	en := again.expect(wire.TypeEnrolled).(*wire.Enrolled)
	again.auth()
	again.close()

	a.send(&wire.AccountDelete{Password: "Wrongpass1"})
	a.expectRefused(wire.ReasonBadCredentials)
	a.send(&wire.AccountDelete{Password: newPW})
	a.expect(wire.TypeAccountDeleted)
	tr.note("answered first, then the account goes: every stream of it is closed")
	a.expectClosed()

	gone := r.open(tr, "b-after-delete", "tls", "a device of a deleted account: the answer of any stranger")
	gone.send(&wire.Hello{DeviceID: bDev, DeviceToken: en.DeviceToken})
	gone.expectRefused(wire.ReasonBadCredentials)
	gone.expectClosed()
	return tr
}

// scenarioClipboard: the clipboard slot of section 22 (ticket 229). A put is clipboard-put, then
// clipboard-ready, then exactly size raw bytes, then clipboard-stored; a get answers
// clipboard-value plus the bytes; clipboard-info on the control stream shows what sits there; a
// guest is refused both directions with not-permitted; the declared and the actual size limits and
// the account's one upload in flight are refused with the old value kept.
func scenarioClipboard() *transcript {
	r := newRig("clipboard", rigOpts{logins: []string{account, "bobby"},
		params: func(p *rendezvousParams) {
			p.ClipboardBytes = 64 // the account's limit, small so a refusal is one byte away
			p.ClipboardProbe = 100 * time.Millisecond
		}})
	defer mustClose(r)
	tr := r.newTranscript("clipboard",
		"The clipboard on the server: one value per member account, in memory only, the latest complete upload the value. "+
			"A put declares kind and size on a data stream, the server answers clipboard-ready, the sender writes exactly size raw bytes - no "+
			"frames, no chunk headers - and the server answers clipboard-stored (clipId, sha256, storedAt) and closes. A get answers the members "+
			"and the bytes; clipboard-info on the control stream answers the members alone. `welcome.features` carries `clipboard`.")

	a, aDev, aToken := r.enroll(tr, "phone-control", account, "Vector phone", "fms-android",
		roles("resource-consumer"), "")
	r.control(a, aDev)
	a.send(&wire.ClipboardInfo{})
	a.expect(wire.TypeClipboardState)
	tr.note("the clipboard starts empty: clipboard-state carries no value")

	// a complete put: ready, exactly size raw bytes, half-close, stored
	put1 := r.hello(tr, "put-1", "the sender's data stream: clipboard-put declares kind and size", aDev, aToken)
	put1.send(&wire.ClipboardPut{Kind: "text", Size: 16})
	put1.expect(wire.TypeClipboardReady)
	put1.stopRecording()
	first := []byte("clipboard-one-16")
	put1.sendRaw("exactly the 16 declared bytes follow clipboard-ready: raw, no frames, no chunk headers", first)
	put1.halfClose()
	put1.startRecording()
	stored1 := put1.expect(wire.TypeClipboardStored).(*wire.ClipboardStored)
	tr.note("the value replaced the slot at its last byte; the answer names the clipId, the SHA-256 of the bytes and the server's clock")

	a.send(&wire.ClipboardInfo{})
	st := a.expect(wire.TypeClipboardState).(*wire.ClipboardState)
	if st.Value == nil || st.Value.ClipID != stored1.ClipID || st.Value.Size != 16 || st.Value.SourceDeviceID != aDev {
		failf("clipboard-state: %+v", st)
	}
	tr.note("clipboard-info answers the members alone: what the server set, then kind, mimeType and name")

	// a get: the members, then exactly size raw bytes, then the server closes the stream
	get1 := r.hello(tr, "get-1", "a reader's data stream: clipboard-get", aDev, aToken)
	get1.send(&wire.ClipboardGet{})
	val := get1.expect(wire.TypeClipboardValue).(*wire.ClipboardValue)
	if val.ClipID != stored1.ClipID || val.Size != 16 || val.SHA256 != stored1.SHA256 {
		failf("clipboard-value: %+v", val)
	}
	get1.stopRecording()
	if got := get1.readRaw("exactly the value's 16 bytes follow the members; the server closes the stream after them", 16); string(got) != string(first) {
		failf("the value's bytes: %q", got)
	}
	get1.expectClosed()

	// a second put replaces the value; the first clipId is no longer the value
	put2 := r.hello(tr, "put-2", "the next complete put replaces the value", aDev, aToken)
	put2.send(&wire.ClipboardPut{Kind: "text", Size: 8})
	put2.expect(wire.TypeClipboardReady)
	put2.stopRecording()
	put2.sendRaw("8 declared bytes", []byte("replaced"))
	put2.halfClose()
	put2.startRecording()
	stored2 := put2.expect(wire.TypeClipboardStored).(*wire.ClipboardStored)

	stale := r.hello(tr, "get-stale", "a get naming the clipId the client showed before: it never receives another value in its place", aDev, aToken)
	stale.send(&wire.ClipboardGet{ClipID: stored1.ClipID})
	stale.expectRefused(wire.ReasonUnavailable)
	stale.expectClosed()

	// an interrupted put changes nothing
	short := r.hello(tr, "put-short", "a put of 64 declared bytes that stops after 4: what it carried is dropped, the value stays", aDev, aToken)
	short.send(&wire.ClipboardPut{Kind: "text", Size: 64})
	short.expect(wire.TypeClipboardReady)
	short.stopRecording()
	short.sendRaw("4 of the 64 declared bytes; the sender's side closes here and the server drops what arrived", []byte("half"))
	r.until("the interrupted put's bytes to be counted", func() bool {
		_, held := r.env.Engine.Clipboard().Counts()
		return held == 8+4
	})
	short.halfClose()
	r.until("the interrupted put to be dropped", func() bool {
		_, held := r.env.Engine.Clipboard().Counts()
		return held == 8
	})
	tr.note("no clipboard-stored follows: the stream is closed and the value from put-2 is still the value")

	// an actual oversize put: two bytes beyond the declaration, written in one piece so the byte
	// beyond the size is already there when the server looks
	over := r.hello(tr, "put-over", "a put of 4 declared bytes that sends 6: the stream is closed and dropped", aDev, aToken)
	over.send(&wire.ClipboardPut{Kind: "text", Size: 4})
	over.expect(wire.TypeClipboardReady)
	over.stopRecording()
	over.sendRaw("6 bytes for a declared 4: one byte beyond the size is enough", []byte("sixby!"))
	over.expectClosed()
	tr.note("the stream is dropped and the old value is whole; no clipboard-stored answers an oversize put")

	// the declared limit: one byte above the account's clipboardBytes, refused before a byte flows
	big := r.hello(tr, "put-too-big", "a put declaring 65 bytes: the account's clipboardBytes is 64", aDev, aToken)
	big.send(&wire.ClipboardPut{Kind: "text", Size: 65})
	big.expectRefused(wire.ReasonLimitReached)
	big.expectClosed()
	tr.note("refused before a byte flows; the value being replaced does not count against the new one")

	// the account's one upload in flight: a second overlapping put is capacity, the first finishes whole
	c1 := r.hello(tr, "put-capacity-first", "the first of two overlapping puts is taken", aDev, aToken)
	c1.send(&wire.ClipboardPut{Kind: "text", Size: 8})
	c1.expect(wire.TypeClipboardReady)
	c1.stopRecording()
	c1.sendRaw("2 of the 8 declared bytes; the upload is in flight", []byte("12"))
	c2 := r.hello(tr, "put-capacity-second", "the account's second overlapping put", aDev, aToken)
	c2.send(&wire.ClipboardPut{Kind: "text", Size: 8})
	c2.expectRefused(wire.ReasonCapacity)
	c2.expectClosed()
	tr.note("this server takes at most one upload per account at a time (ticket 229 T-1); the wire's word is capacity (item BZ)")
	c1.sendRaw("the remaining 6 declared bytes", []byte("345678"))
	c1.halfClose()
	c1.startRecording()
	if s := c1.expect(wire.TypeClipboardStored).(*wire.ClipboardStored); s.ClipID == stored2.ClipID {
		failf("a stored value has a clipId new for every stored value")
	}
	tr.note("the completion that landed later is the value (item BX)")

	// a guest is refused both directions with not-permitted; its state is an empty one
	if _, _, err := r.env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		failf("guest account: %v", err)
	}
	g, gDev, gToken := r.enroll(tr, "guest-enroll", "visitor", "Vector guest", "fms-windows", roles("resource-consumer"), "")
	r.controlAs("visitor", g, gDev)
	g.send(&wire.ClipboardInfo{})
	gst := g.expect(wire.TypeClipboardState).(*wire.ClipboardState)
	if gst.Value != nil {
		failf("a guest's clipboard-state carries a value: %+v", gst)
	}
	tr.note("a guest is answered an empty state: it holds no slot and announces nothing")
	gp := r.hello(tr, "guest-put", "a guest account's put", gDev, gToken)
	gp.send(&wire.ClipboardPut{Kind: "text", Size: 8})
	gp.expectRefused(wire.ReasonNotPermitted)
	gp.expectClosed()
	gg := r.hello(tr, "guest-get", "a guest account's get", gDev, gToken)
	gg.send(&wire.ClipboardGet{})
	gg.expectRefused(wire.ReasonNotPermitted)
	gg.expectClosed()
	tr.note("both directions are refused with one reason: a guest announces nothing")

	// another account never reads the slot: an empty clipboard, never a stranger's value
	b, bDev, bToken := r.enroll(tr, "bobby-control", "bobby", "Vector PC", "fms-windows", roles("resource-consumer"), "")
	r.controlAs("bobby", b, bDev)
	b.send(&wire.ClipboardInfo{})
	bst := b.expect(wire.TypeClipboardState).(*wire.ClipboardState)
	if bst.Value != nil {
		failf("another account's clipboard-state carries a value: %+v", bst)
	}
	bg := r.hello(tr, "bobby-get", "another account's get", bDev, bToken)
	bg.send(&wire.ClipboardGet{})
	bg.expect(wire.TypeClipboardEmpty)
	bg.expectClosed()
	tr.note("the slot is personal: another account sees an empty clipboard")

	_ = aToken
	return tr
}

// scenarioPace: a password check of one login or one address is paced, never counted into a lockout
// (19 item AW). The pacer's sleep is a no-op in the generator and its clock is the scenario's.
func scenarioPace() *transcript {
	r := newRig("pace", rigOpts{logins: []string{account}, pace: true})
	defer mustClose(r)
	tr := r.newTranscript("pace",
		"Password checks are paced: one check per second per login and per address, a check right behind the last waits for its slot, and a "+
			"check that would have to wait for a second slot is turned away with rate-limited before the password is looked at. Nothing remembers a "+
			"failure: there is no lockout, and the right password is evaluated as soon as its slot comes. (The server of this transcript uses an "+
			"interval of 2 s and an injected clock, so the bytes do not depend on the machine.)")
	wrong := func(id, purpose string) *sess {
		s := r.open(tr, id, "tls", purpose)
		s.send(&wire.Enroll{Login: account, Password: "Wrongpass1", DeviceID: r.newID(), DeviceName: "Guesser", Product: "fms-windows"})
		return s
	}
	for _, step := range []struct {
		id, purpose string
		reason      wire.Reason
	}{
		{"first", "a first wrong password: evaluated at once", wire.ReasonBadCredentials},
		{"second", "a second right behind it: waits for its slot, then evaluated", wire.ReasonBadCredentials},
		{"third", "a third inside the window: turned away before the password is looked at", wire.ReasonRateLimited},
	} {
		s := wrong(step.id, step.purpose)
		s.expectRefused(step.reason)
		s.expectClosed()
	}
	r.clock.Add(10 * time.Second)
	s, _, _ := r.enroll(tr, "right-password", account, "Vector PC", "fms-windows", roles("resource-producer"), "")
	tr.note("the window passed; no failure was remembered, so the right password is welcome at once")
	s.close()
	return tr
}
