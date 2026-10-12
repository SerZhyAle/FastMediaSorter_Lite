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
	r := newRig("refusals", rigOpts{logins: []string{account, "bobby"}, params: func(p *rendezvousParams) { p.ResourcesPerAccount = 1 }})
	defer mustClose(r)
	tr := r.newTranscript("refusals",
		"Every refusal reason of DEVICE-EXCHANGE 7.10 produced by the real server: tls-required and version-unsupported on a plain stream, "+
			"id-collision, capacity, port-unavailable, unavailable, device-revoked, bad-credentials and rate-limited. "+
			"The failed-authentication bucket of this server holds 2 and does not refill during the run, so the last three steps are the ones that spend it.")

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
	a.expectRefused(wire.ReasonCapacity)
	tr.note("this server allows one resource per account; the stream stays open after a refusal")
	a.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"First","publicPort":0}`, res1))})
	a.expectRefused(wire.ReasonPortUnavailable)
	tr.note("this server has no third-party port pool")

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
	h3 := r.open(tr, "enroll-limited", "tls", "a third failure from the same address: the bucket is empty")
	h3.send(&wire.Enroll{Login: account, Password: "Wrongpass1", DeviceID: r.newID(), DeviceName: "Guesser", Product: "fms-windows"})
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
