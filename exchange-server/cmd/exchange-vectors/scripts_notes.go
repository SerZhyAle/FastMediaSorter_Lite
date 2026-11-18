package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"strings"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The notes transcript of DEVICE-EXCHANGE 1.13 (ticket L0321, items FG to FM). It is printed by `-notes` and is
// NOT part of frames-v2.json: that file is the adopted 1.1 corpus and keeps its bytes, exactly as the settings
// transcript of 1.12 does. Its server advertises `sealed` AND holds the notes store (rigOpts.sealed, rigOpts.notes),
// which is what makes it list `notes` in `enrolled.features`; no older scenario opens the store.
//
// The server never opens a box. The boxes here are real (sealer.go): every one opens, with the private key of
// either device, under the context `note:<noteId>`, to the clear payload listed beside it in the `sealed` member.

// The version whose items this file carries: the notes of items FG to FM (1.13) and the vectors and rules
// items FW to GB fold into it (1.17, ticket L0322).
const notesContractVersion = "1.17"

// The notes of the scenario: lower-case 8-4-4-4-12 note ids (item FG) and the item ids of the checklist (item FH).
const (
	noteShopping = "5b0c6f1e-2a47-4d39-9e1b-7c83a4d2f610" // a text note, edited on both devices and then deleted
	noteCopy     = "c19e47a8-63d2-4f05-b8a1-0d5e92f3a7c4" // the local version that survives a conflict
	noteTodo     = "e3a85d02-1f6b-4c97-a420-8b17c5e0d9f3" // a checklist

	itemMilk   = "a1f0c2d4-5b6e-4789-8abc-def012345678"
	itemBread  = "b2e1d3c5-6c7f-4890-9bcd-ef0123456789"
	itemCoffee = "c3d2e4f6-7d80-4901-8cde-f01234567890"
)

func int64Ptr(n int64) *int64 { return &n }

// lastJSON is the body of the most recent frame the transcript recorded.
func (t *transcript) lastJSON() string {
	t.mu.Lock()
	defer t.mu.Unlock()
	if n := len(t.Steps); n > 0 {
		return t.Steps[n-1].JSON
	}
	return ""
}

// notesFile is what `-notes` prints: the transcript, and the boxes it carries with what they open to.
type notesFile struct {
	Contract        string        `json:"contract"`
	ContractVersion string        `json:"contractVersion"`
	Sections        string        `json:"sections"`
	Producer        string        `json:"producer"`
	Notes           []string      `json:"notes"`
	Transcripts     []*transcript `json:"transcripts"`
	Sealed          sealedFile    `json:"sealed"`
}

func notesNotes() []string {
	return []string{
		"The transcript was produced by the real exchange server (engine, notes store, accounts, directory) over TLS on loopback, with one injected clock that moves one minute between the phases of the script (so storedAt differs from put to put), seeded id and token sources and the fixed device keys of e2e-v1.json. The bytes are a function of the code and nothing else. It is a separate file: frames-v2.json is the adopted 1.1 corpus and does not change.",
		"A step is one frame as it crossed the stream above TLS: `json` is the body, `hex` is the whole frame (the 4-byte big-endian length, then the body), in the order the script read or wrote it. `notes_changed` is read on the other device's stream right after the put that caused it; the device that made the put is never told, and no stream is told by a refusal.",
		"The server lists `notes` in `enrolled.features` because it lists `sealed` and holds the notes store (item FI). A note is the box of item DH with the context `note:<noteId>`: the server stores it as the bytes it came with and returns it byte for byte, in `notes_changes` and in the `current` of a `note-conflict`; a tombstone has no box.",
		"The clear payloads of item FH are the `payload` members of `sealed.cases`: the cases `text-create`, `text-edit`, `text-edit-stale`, `text-copy` and `text-edit-after-delete` (each name is prefixed `notes-sync/`) are kind `text` (the title is a string), `checklist-create` is kind `checklist` (the title is null, the items are ordered by `position`). The notes box has one shape, the box of item DH; the clear sample is the payload that opens out of it.",
		"Every box addresses both devices of the account, the sender included (a recipient is any device of the entry that holds the role `notes` and a key the sender trusts, item FI), and opens, with the private key of either named device and the `context` given, to the `payload`; `from` is the key the reader trusts for the sender.",
		"The transcripts after `notes-sync` are the four conformance vectors of item GB (DEVICE-EXCHANGE 1.17, ticket L0322): `notes-paging-full-after-purge`, `notes-box-bound`, `notes-conflict-without-current` and `notes-count-bytes-and-policy`, each with its own server, on the cursor of a paged full answer, the delivery bound of a record, a `note-conflict` without `current`, and the count, the bytes and the administration wire. Their boundary boxes are padding boxes ({\"ct\":\"a..\"}), the shape of a sender that does not follow item FH; the box at the item FH bound and the boxes of the paging transcript are real and open like every sealed case.",
		"Still not in this file, because a transcript has one server and one life: a restart of the store, a malformed frame closing the stream, and a server without `sealed`. They are tests of the server (internal/rendezvous).",
	}
}

func notesSealedNotes() []string {
	return []string{
		"Every box here was sealed by the generator (cmd/exchange-vectors/sealer.go) over the fixed keys of the shared vector file e2e-v1.json, with the ephemeral keys, the content keys and the nonces drawn from seeded sources. The exchange server did not make, open or check any of them (item DH): it stored and delivered them.",
		"A `device` names a device of e2e-v1.json (`devices[].name`): that file holds its private key. A replaying product opens the `box` as a recipient of `to` with that key and the `context` given here (never a value read from the box), trusting the key of `from` for the sender; the result must be the `payload`.",
		"`deviceId` is the id this transcript's server knew the device by: the `to` of the box's `keys` entries and the `from` of the box.",
	}
}

// notesDocument is the file `-notes` prints: the notes-sync transcript of items FG to FM and the four
// conformance vectors of item GB (DEVICE-EXCHANGE 1.17, ticket L0322).
func notesDocument(trs []*transcript, cases []sealedCase) notesFile {
	f := notesFile{
		Contract: contractID, ContractVersion: notesContractVersion, Sections: "7.2, 7.4, 7.10, items FG to FM, FW to GB", Producer: producerName,
		Notes: notesNotes(), Transcripts: trs,
		Sealed: sealedFile{
			Section: "27 (item DH); the context of a note is note:<noteId>",
			Keys:    "e2e-v1.json (DEVICE-EXCHANGE vectors, 27 items DF to DP): the private key of every device named here",
			Notes:   notesSealedNotes(),
			Cases:   cases,
		},
	}
	for _, n := range []string{"phone", "tablet"} {
		f.Sealed.Devices = append(f.Sealed.Devices, sealedDevice{Name: n, E2EKey: *e2eDevices[n].Key()})
	}
	return f
}

// enrollNoteDevice enrolls a device that takes part in the notes sync: it declares the capability e2e-v1 and
// its key (item DF) and the role `notes` (item FI), and the server must list both `sealed` and `notes`. The
// stream comes back authenticated, its role not yet fixed.
func (r *rig) enrollNoteDevice(tr *transcript, id, name, product string, dev e2eDevice) (*sess, party) {
	s := r.open(tr, id, "tls", "enroll with the account's password, declaring the capability e2e-v1, the device key (item DF) and the role notes (item FI)")
	p := party{Dev: dev, ID: r.newID()}
	s.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: p.ID, DeviceName: name, Product: product,
		ProductVersion: "1.0.0", Platform: "vector", Roles: roles(wire.RoleNotes), Capabilities: []string{wire.E2ECapability},
		KeepaliveSeconds: 30, E2EKey: dev.Key()})
	en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
	if !hasString(en.Features, wire.FeatureSealed) || !hasString(en.Features, wire.FeatureNotes) {
		failf("%s: the server lists %v, want sealed and notes", id, en.Features)
	}
	s.auth()
	return s, p
}

// scenarioNotesSync is the server side of the notes sync (DEVICE-EXCHANGE 1.13, items FG to FM): the cases of
// item FM that a transcript can hold. A member account with two devices that hold the role `notes` creates a
// note on one and reads it on the other, edits it back, meets a conflict, keeps the local version as a new note,
// syncs a checklist, deletes a note and meets the tombstone with a stale edit; a guest is refused on both
// frames. It returns the transcript and the boxes it carries.
func scenarioNotesSync() (*transcript, []sealedCase) {
	r := newRig("notes-sync", rigOpts{sealed: true, notes: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("notes-sync",
		"The notes of an account on the exchange server (DEVICE-EXCHANGE 1.13, items FG to FM): one record per note, kept on disk, sealed. A member's "+
			"device reads the changes with notes_list (answered by notes_changes) and stores one note as a whole record, or deletes it, with notes_put "+
			"(answered by notes_stored or refused); the account's other devices that hold the role `notes` are told by notes_changed. A put is accepted "+
			"only when its baseRevision is the stored revision, or 0 for a note the server never stored; otherwise it is refused note-conflict with the "+
			"stored record as `current`, box included, and nothing is stored. A guest is refused not-permitted before any other check. "+
			"`enrolled.features` carries `notes` because the server lists `sealed` and holds the store.")
	var cases []sealedCase

	a, phone := r.enrollNoteDevice(tr, "phone-control", "Vector phone", "fms-android", e2eDevices["phone"])
	tr.note("`enrolled.features` lists `sealed` and `notes`: the server holds the notes store and a note is a box of item DH (item FI)")
	r.control(a, phone.ID)
	b, tablet := r.enrollNoteDevice(tr, "tablet-control", "Vector tablet", "fms-android", e2eDevices["tablet"])
	r.control(b, tablet.ID)

	// --- what the script needs: the clock, the clear payloads of item FH, the boxes and the frames ---
	noteTS := func() string { return r.clock.Now().UTC().Format("2006-01-02T15:04:05.000Z") }
	jstr := func(s string) string {
		raw, err := json.Marshal(s)
		if err != nil {
			failf("%v", err)
		}
		return string(raw)
	}
	textPayload := func(title, body, created string) string {
		return fmt.Sprintf(`{"formatVersion":1,"title":%s,"kind":"text","text":%s,"createdAt":%q,"updatedAt":%q}`,
			jstr(title), jstr(body), created, noteTS())
	}
	checklistPayload := func(created string) string {
		return fmt.Sprintf(`{"formatVersion":1,"title":null,"kind":"checklist","items":[`+
			`{"itemId":%q,"text":"Milk","checked":true,"position":0},`+
			`{"itemId":%q,"text":"Bread","checked":false,"position":1},`+
			`{"itemId":%q,"text":"Coffee","checked":false,"position":2}],"createdAt":%q,"updatedAt":%q}`,
			itemMilk, itemBread, itemCoffee, created, noteTS())
	}
	// seal makes the box of a note for both devices under the context note:<noteId> and records it with the clear payload
	seal := func(name, noteID string, from party, payload string) json.RawMessage {
		if len(payload) > wire.MaxNotePayload {
			failf("%s: the payload is %d bytes, above the %d of item FH", name, len(payload), wire.MaxNotePayload)
		}
		to := []party{phone, tablet}
		context := "note:" + noteID
		box, _ := sealBox(tr.Name+"/"+name, from, to, context, payload)
		c := sealedCase{Name: tr.Name + "/" + name, Transcript: tr.Name, Context: context, From: from.view(),
			Payload: json.RawMessage(payload), Box: box}
		for _, t := range to {
			c.To = append(c.To, t.view())
		}
		cases = append(cases, c)
		return box
	}
	put := func(s *sess, noteID string, base int64, box json.RawMessage) {
		s.send(&wire.NotesPut{NoteID: noteID, BaseRevision: int64Ptr(base), Sealed: box})
	}
	list := func(s *sess, since int64) *wire.NotesChanges {
		s.send(&wire.NotesList{Since: int64Ptr(since)})
		return s.expect(wire.TypeNotesChanges).(*wire.NotesChanges)
	}
	stored := func(s *sess, noteID string, revision int64) {
		n := s.expect(wire.TypeNotesStored).(*wire.NotesStored)
		if n.NoteID != noteID || n.Revision != revision || n.StoredAt != r.clock.Now().UTC().Format(time.RFC3339) {
			failf("the put of %s: %+v, want revision %d stored at the clock", noteID, n, revision)
		}
	}
	pushed := func(s *sess, revision int64) {
		if n := s.expect(wire.TypeNotesChanged).(*wire.NotesChanged); n.NotesRevision != revision {
			failf("notes_changed: %+v, want notesRevision %d", n, revision)
		}
	}
	conflict := func(s *sess) *wire.NoteRecord {
		e := s.expect(wire.TypeRefused).(*wire.Refused)
		if e.Reason != string(wire.ReasonNoteConflict) {
			failf("expected refused note-conflict, got %s", e.Reason)
		}
		if e.Current == nil {
			failf("a note-conflict on a note the server holds carries `current`")
		}
		return e.Current
	}
	wantRecord := func(what string, rec wire.NoteRecord, noteID string, revision int64, from party, box json.RawMessage) {
		if rec.NoteID != noteID || rec.Revision != revision || rec.SourceDeviceID != from.ID || rec.Deleted || !bytes.Equal(rec.Sealed, box) {
			failf("%s: %+v", what, rec)
		}
	}

	// --- an account that never held a note ---
	if c := list(a, 0); c.NotesRevision != 0 || c.NotesHorizon != 0 || len(c.Records) != 0 || c.More || c.Full || !strings.Contains(tr.lastJSON(), `"records":[]`) {
		failf("the first listing: %+v %s", c, tr.lastJSON())
	}
	tr.note("an account that never held a note: notesRevision 0, notesHorizon 0, `records` is the empty list (never null), `more` and `full` are written false")

	// --- a create on the phone, read by the tablet ---
	r.clock.Add(time.Minute)
	created := noteTS()
	shoppingV1 := seal("text-create", noteShopping, phone, textPayload("Shopping", "Milk\nBread\n", created))
	put(a, noteShopping, 0, shoppingV1)
	stored(a, noteShopping, 1)
	tr.note("a create: baseRevision 0 for a note the server never stored; the answer is the record's revision and the server's clock at the store")
	pushed(b, 1)
	tr.note("notes_changed goes to the account's other devices that hold the role `notes` and carries only notesRevision; the phone, whose put caused it, is never told")
	c := list(b, 0)
	if c.NotesRevision != 1 || c.NotesHorizon != 0 || len(c.Records) != 1 || c.More || c.Full {
		failf("the tablet's first listing: %+v", c)
	}
	wantRecord("the record the tablet reads", c.Records[0], noteShopping, 1, phone, shoppingV1)
	tr.note("the other device reads the record: the box comes back byte for byte, sourceDeviceId is the phone, `full` is false because since 0 is not below the horizon 0")

	// --- an edit on the tablet with the right base, coming back to the phone ---
	r.clock.Add(time.Minute)
	shoppingV2 := seal("text-edit", noteShopping, tablet, textPayload("Shopping", "Milk\nBread\nCoffee\n", created))
	put(b, noteShopping, 1, shoppingV2)
	stored(b, noteShopping, 2)
	tr.note("an edit with the stored revision as its base: the record is replaced as a whole and takes the next revision of the account")
	pushed(a, 2)

	// --- a stale edit on the phone: item FJ, a note-conflict ---
	r.clock.Add(time.Minute)
	staleBox := seal("text-edit-stale", noteShopping, phone, textPayload("Shopping", "Milk\nBread\nTea\n", created))
	put(a, noteShopping, 1, staleBox)
	cur := conflict(a)
	wantRecord("the current of the conflict", *cur, noteShopping, 2, tablet, shoppingV2)
	tr.note("item FJ: baseRevision 1 is not the stored revision 2, so the put is refused note-conflict; `current` is the stored record with its box, nothing is stored and nothing is pushed")

	// --- the local version survives as a new note ---
	copyBox := seal("text-copy", noteCopy, phone, textPayload("Shopping (Vector phone)", "Milk\nBread\nTea\n", noteTS()))
	put(a, noteCopy, 0, copyBox)
	stored(a, noteCopy, 3)
	tr.note("what a client does with a note-conflict on a live edit (item FJ): the stored version keeps the noteId, the local version is put as a new note with baseRevision 0")
	pushed(b, 3)
	c = list(b, 2)
	if c.NotesRevision != 3 || len(c.Records) != 1 || c.More || c.Full {
		failf("the tablet's listing since 2: %+v", c)
	}
	wantRecord("the copy", c.Records[0], noteCopy, 3, phone, copyBox)
	tr.note("a listing since the last revision the device holds returns only the records above it")

	// --- a checklist on the tablet ---
	r.clock.Add(time.Minute)
	todoBox := seal("checklist-create", noteTodo, tablet, checklistPayload(noteTS()))
	put(b, noteTodo, 0, todoBox)
	stored(b, noteTodo, 4)
	pushed(a, 4)
	c = list(a, 3)
	if c.NotesRevision != 4 || len(c.Records) != 1 || c.More || c.Full {
		failf("the phone's listing since 3: %+v", c)
	}
	wantRecord("the checklist", c.Records[0], noteTodo, 4, tablet, todoBox)
	tr.note("a note of kind `checklist` is a box like any other: the server never looks inside it")

	// --- a delete on the phone, and a stale edit against the tombstone on the tablet ---
	r.clock.Add(time.Minute)
	a.send(&wire.NotesPut{NoteID: noteShopping, BaseRevision: int64Ptr(2), Deleted: true})
	stored(a, noteShopping, 5)
	tr.note("a delete is a put with `deleted` and no box; it keeps the record as a tombstone and takes the next revision")
	pushed(b, 5)
	r.clock.Add(time.Minute)
	afterDelete := seal("text-edit-after-delete", noteShopping, tablet, textPayload("Shopping", "Milk\nBread\nCoffee\nSugar\n", created))
	put(b, noteShopping, 2, afterDelete)
	tomb := conflict(b)
	if tomb.NoteID != noteShopping || tomb.Revision != 5 || !tomb.Deleted || len(tomb.Sealed) != 0 || tomb.SourceDeviceID != phone.ID {
		failf("the current of the conflict with a tombstone: %+v", tomb)
	}
	tr.note("a deleted note is never brought back by an older copy: the edit carries the older baseRevision 2 and is refused note-conflict, and `current` is the tombstone (`deleted` true, no box)")
	c = list(b, 4)
	if c.NotesRevision != 5 || len(c.Records) != 1 || c.Records[0].NoteID != noteShopping || c.Records[0].Revision != 5 || !c.Records[0].Deleted || len(c.Records[0].Sealed) != 0 {
		failf("the tablet's listing since 4: %+v", c)
	}
	tr.note("the tombstone is listed like any record, with no box")
	c = list(b, 0)
	if c.NotesRevision != 5 || c.NotesHorizon != 0 || len(c.Records) != 3 || c.More || c.Full ||
		c.Records[0].NoteID != noteCopy || c.Records[1].NoteID != noteTodo || c.Records[2].NoteID != noteShopping {
		failf("the whole store: %+v", c)
	}
	tr.note("the store holds one record per note, ascending by revision: a replaced record is listed once, at its newest revision")

	// --- a guest ---
	if _, _, err := r.env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		failf("guest account: %v", err)
	}
	g, gDev, _ := r.enroll(tr, "guest-control", "visitor", "Vector guest", "fms-windows", roles("resource-consumer"), "")
	r.controlAs("visitor", g, gDev)
	g.send(&wire.NotesList{Since: int64Ptr(0)})
	g.expectRefused(wire.ReasonNotPermitted)
	g.send(&wire.NotesPut{NoteID: noteShopping, BaseRevision: int64Ptr(0), Sealed: shoppingV1})
	g.expectRefused(wire.ReasonNotPermitted)
	tr.note("a guest is not-permitted on both frames, before the frame members, the conflict and the limits, and nothing is stored or read")
	return tr, cases
}
