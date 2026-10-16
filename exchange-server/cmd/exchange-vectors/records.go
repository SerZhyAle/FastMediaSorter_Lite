package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"strconv"
	"strings"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// records-v1.json: the records of DEVICE-EXCHANGE section 6 as the real server takes them, hands them
// out and refuses them, with the Drive file envelope of 8.1 around each record that was accepted. A
// case is what a client SENT (`input`), what the server DID (`verdict`, with its reason), and what the
// server then LISTED (`listed`: the record exactly as an enrolled device of the account reads it, with
// the members the server owns - deviceId, presence, updatedAt, startedAt, publicPort - filled in).

type recordCase struct {
	Name    string     `json:"name"`
	Kind    string     `json:"kind"`
	Note    string     `json:"note,omitempty"`
	Input   string     `json:"input"`   // the JSON text a client sent, exactly
	Verdict string     `json:"verdict"` // accepted | refused | closed
	Reason  string     `json:"reason,omitempty"`
	Listed  string     `json:"listed,omitempty"` // the JSON text of the record as a device of the account reads it, exactly
	Drive   *driveFile `json:"drive,omitempty"`
}

// driveFile is the section 8.1 wrapper around a record, and the file it is written to.
type driveFile struct {
	FileName string `json:"fileName"`
	Text     string `json:"text"`
}

// descriptorVersion is the version member of a LIVE-BROADCAST descriptor the vectors carry (a different
// thing from the wire's schemaVersion).
const descriptorVersion = 1

// driveFileVersion is the version of the Drive file wrapper (8.1): a different thing from the wire's schemaVersion.
const driveFileVersion = 1

// driveTTL is the working ttlSeconds the vectors write: a device or resource record 1800, a broadcast 120.
func driveTTL(kind string) int {
	if kind == "broadcast" {
		return 120
	}
	return 1800
}

// wrapDrive writes the 8.1 wrapper in its one member order. A resource record carries `access` on Drive
// only with the user's opt-in (item X), so the vector is the default: without it.
func wrapDrive(kind, id string, listed json.RawMessage) *driveFile {
	var members map[string]json.RawMessage
	if err := json.Unmarshal(listed, &members); err != nil {
		failf("a listed record must parse: %v", err)
	}
	if kind == "resource" {
		delete(members, "access")
	}
	body, err := json.Marshal(members) // members sorted by name, values as they were
	if err != nil {
		failf("%v", err)
	}
	version := driveFileVersion
	if kind == "resource" {
		if _, protected := members["protection"]; protected {
			version = 2
		}
	}
	var b bytes.Buffer
	fmt.Fprintf(&b, `{"schemaVersion":%d,"type":%q,"writtenAt":%q,"ttlSeconds":%d,"record":`, version, kind,
		instant.Format("2006-01-02T15:04:05.000Z"), driveTTL(kind))
	b.Write(body)
	b.WriteByte('}')
	return &driveFile{FileName: "fmsx-" + kind + "-" + id + ".json", Text: b.String()}
}

// padTo returns a member value of n bytes of 'a'.
func padTo(n int) string { return strings.Repeat("a", n) }

// recordOfSize builds a resource or broadcast record whose JSON is exactly size bytes, by the length of
// its `pad` member.
func recordOfSize(prefix, suffix string, size int) string {
	base := prefix + `,"pad":""` + suffix
	if len(base) > size {
		failf("the record skeleton is already %d bytes", len(base))
	}
	return prefix + `,"pad":"` + padTo(size-len(base)) + `"` + suffix
}

func generateRecords() []recordCase {
	r := newRig("records", rigOpts{pool: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("records", "")

	a, aDev, aToken := r.enroll(tr, "a", account, "Records PC", "fms-windows", roles("resource-producer", "broadcaster"), "")
	r.control(a, aDev)
	b, bDev, _ := r.enroll(tr, "b", account, "Records phone", "fms-android", roles("resource-producer", "broadcaster"), "")
	r.control(b, bDev)
	_ = aToken
	var cases []recordCase
	// a throwaway device: a case that ends in a closed stream must not take a working device with it
	throwaway := func(name string) *sess {
		s, _, _ := r.enroll(tr, name, account, "Throwaway "+name, "fms-windows", roles("resource-producer", "broadcaster"), "")
		return s
	}

	// list reads the directory on A's control stream.
	list := func() *wire.Directory {
		a.send(&wire.List{})
		return a.expect(wire.TypeDirectory).(*wire.Directory)
	}
	find := func(raws []json.RawMessage, key, id string) json.RawMessage {
		for _, raw := range raws {
			var m map[string]json.RawMessage
			if err := json.Unmarshal(raw, &m); err != nil {
				failf("%v", err)
			}
			if string(m[key]) == strconv.Quote(id) {
				return raw
			}
		}
		return nil
	}

	// --- devices: a record is what the server builds from an enroll ---
	devCase := func(name, note, members string, wantClosed bool) {
		id := r.newID()
		body := fmt.Sprintf(`{"schemaVersion":%d,"type":"enroll","login":%q,"password":%q,"deviceId":%q,%s}`,
			wire.SchemaVersion, account, testkit.TestPassword, id, members)
		s := r.open(tr, "dev-"+name, "tls", "")
		s.sendFrame(testkit.RawFrame(body))
		c := recordCase{Name: name, Kind: "device", Note: note, Input: "{" + members + "}"}
		if wantClosed {
			s.expectClosed()
			c.Verdict = "closed"
			cases = append(cases, c)
			return
		}
		s.expect(wire.TypeEnrolled)
		s.auth()
		s.send(&wire.Keepalive{})
		s.expect(wire.TypeKeepalive)
		r.online(account, id)
		c.Verdict = "accepted"
		c.Listed = string(find(list().Devices, "deviceId", id))
		if c.Listed == "" {
			failf("device %s is not listed", name)
		}
		c.Drive = wrapDrive("device", id, json.RawMessage(c.Listed))
		cases = append(cases, c)
		s.close()
	}
	devCase("device-minimal", "only the required members: the optional ones are simply absent from the listed record",
		`"deviceName":"Minimal","product":"streamsplayer-windows"`, false)
	devCase("device-full", "every optional member; receiver.plays and an unknown nested member travel verbatim",
		`"deviceName":"Everything","product":"fms-android","productVersion":"2.6.1","platform":"android",`+
			`"roles":["resource-producer","resource-consumer","broadcaster","receiver"],`+
			`"receiver":{"modes":["AUDIO_ONLY","VIDEO_AUDIO"],"transports":["HTTP","RTSP","RELAY","TUNNEL"],`+
			`"plays":[{"mode":"AUDIO_ONLY","transport":"RELAY"},{"mode":"VIDEO_AUDIO","transport":"RTSP"}],"future":{"a":{"b":[1,2]}}}`, false)
	devCase("device-unknown-role", "a role the server does not know is not kept: only the roles of 6.1 are - the four of the first edition and `clipboard` (item BV), which is kept",
		`"deviceName":"Odd roles","product":"fms-windows","roles":["receiver","clipboard","time-traveller"],"receiver":{"modes":["AUDIO_ONLY"],"transports":["RELAY"]}`, false)
	devCase("device-unknown-product", "an unknown product value is kept and shown raw",
		`"deviceName":"Strange","product":"some-other-product"`, false)
	devCase("device-receiver-null", "a receiver member that is null says nothing: it is absent, not malformed",
		`"deviceName":"Null receiver","product":"fms-windows","roles":["resource-consumer"],"receiver":null`, false)
	devCase("device-receiver-too-many-modes", "a receiver list of nine entries is above the bound of eight: the enroll is malformed and the stream is closed with no answer",
		`"deviceName":"Too many","product":"fms-windows","receiver":{"modes":["1","2","3","4","5","6","7","8","9"]}`, true)

	// --- resources: publish on a control stream ---
	type outcome struct {
		verdict, reason string
	}
	publish := func(s *sess, raw string) outcome {
		s.send(&wire.Publish{Resource: json.RawMessage(raw)})
		_ = s.c.SetReadDeadline(time.Now().Add(5 * time.Second))
		e, err := s.c.Recv(5 * time.Second)
		if err != nil {
			return outcome{verdict: "closed"}
		}
		switch v := e.(type) {
		case *wire.Published:
			return outcome{verdict: "accepted"}
		case *wire.Refused:
			return outcome{verdict: "refused", reason: v.Reason}
		}
		failf("publish: %s", e.TypeName())
		return outcome{}
	}
	resCase := func(name, note string, s *sess, id, raw string) {
		c := recordCase{Name: name, Kind: "resource", Note: note, Input: raw}
		o := publish(s, raw)
		c.Verdict, c.Reason = o.verdict, o.reason
		if o.verdict == "accepted" {
			c.Listed = string(find(list().Resources, "resourceId", id))
			if c.Listed == "" {
				failf("resource %s is not listed", name)
			}
			c.Drive = wrapDrive("resource", id, json.RawMessage(c.Listed))
		}
		cases = append(cases, c)
	}
	id := r.newID()
	resCase("resource-minimal", "the required members; the server fills deviceId, presence and updatedAt", a, id,
		fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Minimal"}`, id))
	minimalID := id
	id = r.newID()
	shareID := r.newID()
	resCase("resource-full", "access, shareId and a third-party port request; the granted port replaces the request; presence and deviceId are the server's", a, id,
		fmt.Sprintf(`{"resourceId":%q,"deviceId":"ignored-the-server-forces-the-publisher","kind":"sftp-share","name":"Everything","updatedAt":"2001-01-01T00:00:00Z",`+
			`"presence":"offline","access":{"descriptor":"FMSSFTP2:vector-not-a-secret","root":"/DCIM"},"shareId":%q,"publicPort":0}`, id, shareID))
	id = r.newID()
	resCase("resource-unknown-members-deep", "members the server does not know, three levels deep, come back verbatim", a, id,
		fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Future","future":{"a":{"b":{"c":[1,2,{"d":null}]}}},"x-extra":true}`, id))
	id = r.newID()
	resCase("resource-unknown-kind", "an unknown kind is stored and listed; a reader shows it as unsupported", a, id,
		fmt.Sprintf(`{"resourceId":%q,"kind":"future-kind","name":"Not an sftp share"}`, id))
	id = r.newID()
	resCase("resource-16KiB-exact", "a record of exactly 16384 bytes as sent is refused with capacity and stores nothing: the bound is the record as delivered (item BS), and the members the server adds (deviceId, presence, updatedAt) make this one longer than 16384 when it is listed", a, id,
		recordOfSize(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Big"`, id), `}`, 16384))
	// the largest input whose record is within the bound in every form the server lists it in: a record with
	// none of the producer's live members is widest once its device is offline (presence `offline` is a byte
	// longer than `online`), so the input is 16384 less what the server adds to it in that form
	serverAdds := len(fmt.Sprintf(`,"deviceId":%q`, aDev)) + len(`,"presence":"offline"`) + len(fmt.Sprintf(`,"updatedAt":%q`, wire.FormatTime(instant)))
	id = r.newID()
	resCase("resource-delivered-16KiB-exact", fmt.Sprintf("the largest record that is accepted: %d bytes as sent, listed at 16383 bytes while its device is online and at exactly 16384 once the device is offline (presence `offline` is a byte longer than `online`)", 16384-serverAdds), a, id,
		recordOfSize(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Edge"`, id), `}`, 16384-serverAdds))
	id = r.newID()
	resCase("resource-delivered-16KiB-plus-1", fmt.Sprintf("one byte more than the largest accepted record (%d bytes as sent) is refused with capacity and stores nothing: its delivered form would be 16385 bytes", 16384-serverAdds+1), a, id,
		recordOfSize(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Edge"`, id), `}`, 16384-serverAdds+1))
	id = r.newID()
	resCase("resource-16KiB-plus-1", "a record one byte over 16 KiB is refused with capacity and stores nothing", a, id,
		recordOfSize(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Too big"`, id), `}`, 16385))
	resCase("resource-held-by-another-device", "the same resourceId from another device of the account is an id-collision", b, minimalID,
		fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Mine now?"}`, minimalID))
	resCase("resource-published-again-by-its-device", "the same device publishing a resourceId it holds replaces the record", a, minimalID,
		fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Minimal, renamed"}`, minimalID))
	resCase("resource-share-id-held-by-another", "a shareId held by another resource is an id-collision", b, r.newID(),
		fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Same share","shareId":%q}`, r.newID(), shareID))
	resCase("resource-not-an-id", "a resourceId that is not 16 random bytes in base64url is malformed: the stream is closed", throwaway("res-bad-id"), "short",
		`{"resourceId":"short","kind":"sftp-share","name":"Bad id"}`)

	// --- broadcasts: broadcast-start on a control stream ---
	start := func(s *sess, raw string, maxListeners *int) outcome {
		s.send(&wire.BroadcastStart{Broadcast: json.RawMessage(raw), MaxListeners: maxListeners})
		e, err := s.c.Recv(5 * time.Second)
		if err != nil {
			return outcome{verdict: "closed"}
		}
		switch v := e.(type) {
		case *wire.BroadcastStarted:
			return outcome{verdict: "accepted"}
		case *wire.Refused:
			return outcome{verdict: "refused", reason: v.Reason}
		}
		failf("broadcast-start: %s", e.TypeName())
		return outcome{}
	}
	end := func(s *sess, bid string) {
		s.send(&wire.BroadcastEnd{BroadcastID: bid})
		r.until("the broadcast to end", func() bool { return r.env.Dir.Counts().Broadcasts == 0 })
	}
	broCase := func(name, note string, s *sess, bid, raw string, keep bool) {
		c := recordCase{Name: name, Kind: "broadcast", Note: note, Input: raw}
		o := start(s, raw, nil)
		c.Verdict, c.Reason = o.verdict, o.reason
		if o.verdict == "accepted" {
			c.Listed = string(find(list().Broadcasts, "broadcastId", bid))
			if c.Listed == "" {
				failf("broadcast %s is not listed", name)
			}
			c.Drive = wrapDrive("broadcast", bid, json.RawMessage(c.Listed))
			if !keep {
				end(s, bid)
			}
		}
		cases = append(cases, c)
	}
	id = r.newID()
	broCase("broadcast-minimal", "the required members; the server stamps deviceId, startedAt and updatedAt", a, id,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","descriptor":{}}`, id), false)
	id = r.newID()
	broCase("broadcast-full", "title, a startedAt of the client's own and a descriptor with the three kinds of endpoint, verbatim", a, id,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"VIDEO_AUDIO","title":"Kitchen","startedAt":"2026-10-07T11:55:00Z","descriptor":{"schemaVersion":%d,"endpoints":[`+
			`{"transport":"HTTP","url":"http://192.0.2.1:8768/live"},`+
			`{"transport":"RELAY","url":"https://%s/v2/b/%s/stream","targetLatencyMs":2000},`+
			`{"transport":"TUNNEL","url":"fmsx://%s/b/%s/rtsp","inner":"rtsp://192.0.2.1:8554/","targetLatencyMs":2000}]}}`, id, descriptorVersion, publicEndpoint, id, publicEndpoint, id), false)
	id = r.newID()
	broCase("broadcast-unknown-members-deep", "unknown members of the record and of its descriptor travel verbatim", a, id,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","descriptor":{"endpoints":[],"future":{"a":{"b":{"c":1}}}},"x-extra":[1,{"y":2}]}`, id), false)
	id = r.newID()
	broCase("broadcast-without-descriptor", "a broadcast with no descriptor is malformed: the stream is closed with no answer", throwaway("bro-no-descriptor"), id,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY"}`, id), false)
	id = r.newID()
	broCase("broadcast-16KiB-plus-1", "a record one byte over 16 KiB is refused with capacity", a, id,
		recordOfSize(fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","descriptor":{}`, id), `}`, 16385), false)
	id = r.newID()
	broCase("broadcast-held", "kept live for the two cases below", a, id,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Held","descriptor":{}}`, id), true)
	heldID := id
	broCase("broadcast-id-held-by-another-device", "the same broadcastId from another device is an id-collision", b, heldID,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","descriptor":{}}`, heldID), true)
	broCase("broadcast-started-again-by-its-device", "the same device starting a broadcastId it holds replaces the record", a, heldID,
		fmt.Sprintf(`{"broadcastId":%q,"mode":"AUDIO_ONLY","title":"Held, replaced","descriptor":{}}`, heldID), true)
	end(a, heldID)
	return cases
}
