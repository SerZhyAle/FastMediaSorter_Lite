package main

import (
	"encoding/json"
	"fmt"
	"math"
	"strings"
	"time"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// The four conformance vectors of item GB (DEVICE-EXCHANGE 1.17, ticket L0322), each its own transcript and
// its own server: notes-paging-full-after-purge (items FW and FX), notes-box-bound (item FX),
// notes-conflict-without-current (item FX) and notes-count-bytes-and-policy (items FY and FZ). Like the
// notes-sync transcript they belong to the `-notes` file, not to the adopted 1.1 corpus of frames-v2.json.
//
// The bound and limit transcripts carry padding boxes ({"ct":"a.."}), not the boxes of the e2e file: the
// server never opens a box, and these vectors are about the sizes and the refusals, not about a box that
// opens. The one exception is the box at the item FH bound, which is real and is a sealed case.

const (
	// the note ids of the limit vectors, lower-case 8-4-4-4-12 (item FG)
	notePageA = "0aa11a2b-3c4d-4e5f-8a9b-0c1d2e3f4001"
	notePageB = "0bb22b3c-4d5e-4f6a-8b0c-1d2e3f404002"
	notePageC = "0cc33c4d-5e6f-4071-8c1d-2e3f40504003"
	notePageD = "0dd44d5e-6f70-4081-8d2e-3f4060604004"

	noteBound = "1ee55e6f-7081-4901-8e3f-407070704005"
	noteFH    = "1ff66f70-8192-4a02-8f40-708080804006"

	noteConflict   = "2aa77a08-1a23-4b13-8a51-809090904007"
	noteSurvivor   = "2bb88b29-2b34-4c34-8b52-81a1a1a14009"
	noteCountA     = "3bb88b19-2b34-4c24-8b62-81a1a1a14008"
	noteCountB     = "3cc99c2a-3c45-4d35-8c73-82b2b2b24009"
	noteCountGone  = "3dd00d3b-4d56-4e46-8d84-83c3c3c3400a"
	noteCountLater = "4ee11e4c-5e67-4f57-8e95-84d4d4d4400b"
)

// widestNoteTime is a `storedAt` of the width the store writes, for the widest-record measure of item FX
// (the same width the server's own check takes).
const widestNoteTime = "9999-12-31T23:59:59Z"

// padBox is a padding box of exactly n bytes (n of 9 and more): one JSON object the wire accepts, its
// ciphertext filled with the tag. The server stores and lists it without opening it.
func padBox(n int, tag string) json.RawMessage {
	if n < 9+len(tag) {
		failf("a padding box of %d bytes", n)
	}
	return json.RawMessage(`{"ct":"` + tag + strings.Repeat("a", n-9-len(tag)) + `"}`)
}

// boxNoteFits reports whether the record of box, alone and with the counters at their widest, still
// encodes within one delivery frame: the measure of item FX the server itself takes.
func boxNoteFits(noteID, deviceID string, box json.RawMessage) bool {
	return wire.NoteFitsFrame(wire.NoteRecord{NoteID: noteID, Revision: math.MaxInt64, StoredAt: widestNoteTime,
		SourceDeviceID: deviceID, Sealed: box})
}

// widestFrameLen is the length of the `notes_changes` frame that carries rec alone, counters at their
// widest: the length item FX bounds (wire.MaxFrameAuth), computed with the codec itself.
func widestFrameLen(rec wire.NoteRecord) int {
	body, err := wire.Encode(&wire.NotesChanges{NotesRevision: math.MaxInt64, NotesHorizon: math.MaxInt64, Records: []wire.NoteRecord{rec}})
	if err != nil {
		failf("encode the widest list frame: %v", err)
	}
	return len(body)
}

// widestBoundBox finds, by binary search, the longest padding box whose record still fits one delivery
// frame at the widest counters: the exact edge of item FX, computed by the generator and never written
// by hand. It returns the box and its length.
func widestBoundBox(noteID, deviceID, tag string) (json.RawMessage, int) {
	lo, hi := 9, wire.MaxFrameAuth // a box of 9 bytes fits; a box of the frame's size cannot
	for hi-lo > 1 {
		mid := (lo + hi) / 2
		if boxNoteFits(noteID, deviceID, padBox(mid, tag)) {
			lo = mid
		} else {
			hi = mid
		}
	}
	return padBox(lo, tag), lo
}

// notesPagedBox seals a real box of about 150 KiB for both devices of the paging account: with boxes of
// this size one `notes_changes` frame holds at most one live record (item GB: three live records, one per
// page).
func notesPagedBox(label, noteID string, from party, to []party, created string) json.RawMessage {
	const padding = 110_000
	payload := fmt.Sprintf(`{"formatVersion":1,"title":%q,"kind":"text","text":%q,"createdAt":%q,"updatedAt":%q}`,
		label, strings.Repeat("y", padding), created, created)
	if len(payload) > wire.MaxNotePayload {
		failf("%s: the payload is above the bound of item FH", label)
	}
	box, _ := sealBox("notes-paging/"+label, from, to, "note:"+noteID, payload)
	return box
}

// enrollNotesTokened enrolls a notes device like rig.enrollNoteDevice and also returns the device token,
// so a scenario can open a second stream of the same device (item FW: a continuation on a new stream).
func enrollNotesTokened(r *rig, tr *transcript, id, name string, dev e2eDevice) (*sess, party, string) {
	s := r.open(tr, id, "tls", "enroll with the account's password, declaring the capability e2e-v1, the device key (item DF) and the role notes (item FI)")
	p := party{Dev: dev, ID: r.newID()}
	s.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: p.ID, DeviceName: name, Product: "fms-android",
		ProductVersion: "1.0.0", Platform: "vector", Roles: roles(wire.RoleNotes), Capabilities: []string{wire.E2ECapability},
		KeepaliveSeconds: 30, E2EKey: dev.Key()})
	en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
	if !hasString(en.Features, wire.FeatureSealed) || !hasString(en.Features, wire.FeatureNotes) {
		failf("%s: the server lists %v, want sealed and notes", id, en.Features)
	}
	s.auth()
	return s, p, en.DeviceToken
}

// expectNotesChanges reads one page of a listing and holds it.
func expectNotesChanges(s *sess) *wire.NotesChanges {
	return s.expect(wire.TypeNotesChanges).(*wire.NotesChanges)
}

// scenarioNotesPagingFullAfterPurge is the `notes-paging-full-after-purge` vector of item GB (items FW and
// FX, ticket L0322): three live records with boxes of about 150 KiB, so one record per frame - a full page
// with `more` true, a continuation on a new stream with `full: true` and the last revision, and a last page
// with `more` false; then a store whose `notesHorizon` stands above every live revision after a purge,
// asked from a `since` below it, where the continuation answers the records above `since` and does not
// restart from the lowest - the loop the `full` flag closes. A fourth note the client holds and that no page
// names is the deletion rule of item FI: the client applies it once, when the listing ends, and never to a
// frame that is not the last.
func scenarioNotesPagingFullAfterPurge() *transcript {
	r := newRig("notes-paging-full-after-purge", rigOpts{sealed: true, notes: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("notes-paging-full-after-purge",
		"The cursor of a paged full answer (DEVICE-EXCHANGE 1.17, items FW and FX): "+`notes_list`+" gains an optional "+
			"`full` flag; the answer depends only on the request and the store, and the server keeps no cursor between "+
			"requests, so the continuation here travels on a stream of its own, opened with the device's hello. The live "+
			"records of the account carry boxes of about 150 KiB, at most one record per frame. After the sweep purges a "+
			"tombstone, the horizon stands above every live revision: a `since` below it starts a full listing from the "+
			"lowest record, and a `full: true` continuation with `since` set to the revision of the last record received "+
			"answers the records above it, never from the lowest again.")
	a, phone, phoneToken := enrollNotesTokened(r, tr, "phone-control", "Vector phone", e2eDevices["phone"])
	r.control(a, phone.ID)
	b, tablet, _ := enrollNotesTokened(r, tr, "tablet-control", "Vector tablet", e2eDevices["tablet"])
	r.control(b, tablet.ID)

	noteTS := func() string { return r.clock.Now().UTC().Format("2006-01-02T15:04:05.000Z") }
	toBoth := []party{phone, tablet}
	created := noteTS()
	boxA := notesPagedBox("page-a", notePageA, phone, toBoth, created)
	boxB := notesPagedBox("page-b", notePageB, tablet, toBoth, created)

	// three live records of about 150 KiB, one per page, and the fourth note as a tombstone
	for i, x := range []struct {
		id  string
		box json.RawMessage
		rev int64
	}{
		{notePageA, boxA, 1}, {notePageB, boxB, 2},
	} {
		r.clock.Add(time.Minute)
		a.send(&wire.NotesPut{NoteID: x.id, BaseRevision: int64Ptr(0), Sealed: x.box})
		if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != x.rev {
			failf("the put of %s: revision %d, want %d", x.id, st.Revision, x.rev)
		}
		b.expect(wire.TypeNotesChanged)
		tr.note(fmt.Sprintf("record %d is stored: its box is %d bytes, at most one record per frame", i+1, len(x.box)))
	}
	// the fourth note of the case: stored, then deleted, before any listing, so no page names it
	r.clock.Add(time.Minute)
	boxC := notesPagedBox("page-c", notePageC, phone, toBoth, noteTS())
	a.send(&wire.NotesPut{NoteID: notePageC, BaseRevision: int64Ptr(0), Sealed: boxC})
	if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 3 {
		failf("the put of the third live record: revision %d, want 3", st.Revision)
	}
	b.expect(wire.TypeNotesChanged)
	tr.note("the third live record: like the first two, its box is about 150 KiB, one record per page")
	r.clock.Add(time.Minute)
	boxD := notesPagedBox("page-d", notePageD, phone, toBoth, noteTS())
	a.send(&wire.NotesPut{NoteID: notePageD, BaseRevision: int64Ptr(0), Sealed: boxD})
	if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 4 {
		failf("the put of the fourth note: revision %d, want 4", st.Revision)
	}
	b.expect(wire.TypeNotesChanged)
	r.clock.Add(time.Minute)
	a.send(&wire.NotesPut{NoteID: notePageD, BaseRevision: int64Ptr(4), Deleted: true})
	if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 5 {
		failf("the delete of the fourth note: revision %d, want 5", st.Revision)
	}
	b.expect(wire.TypeNotesChanged)
	tr.note("a fourth note exists as a tombstone: a client that never saw it learns nothing of it - the deletion rule of item FI is applied once, when a listing ends, to a note the client held as synced and no page of that listing named")

	// the stateless cursor of item FW: pages asked on a stream of their own, answers a function of the
	// request and the store alone
	c := r.hello(tr, "phone-pages", "a continuation on a stream of its own, opened with the device's hello: the server keeps no cursor between requests (item FW)", phone.ID, phoneToken)
	r.control(c, phone.ID)
	pagesOf := func(since int64, full bool, note string) *wire.NotesChanges {
		c.send(&wire.NotesList{Since: int64Ptr(since), Full: full})
		ans := expectNotesChanges(c)
		tr.note(note)
		return ans
	}
	pg := pagesOf(0, true, "a listing asked with `full: true`: the server answers with the records above `since`, `full` true and one record per page, `more` true because records are left out")
	if !pg.Full || !pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageA || pg.NotesRevision != 5 || pg.NotesHorizon != 0 {
		failf("the first full page: %+v", pg)
	}
	pg = pagesOf(1, true, "the continuation carries `full: true` and the revision of the last record received: the answer starts above it, not from the lowest")
	if !pg.Full || !pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageB {
		failf("the second full page: %+v", pg)
	}
	pg = pagesOf(2, true, "the next page holds the last live record and the tombstone: the tombstone is empty, so it fits beside the box, and `more` is false - the listing ends here")
	if !pg.Full || pg.More || len(pg.Records) != 2 || pg.Records[0].NoteID != notePageC || pg.Records[1].NoteID != notePageD || !pg.Records[1].Deleted || len(pg.Records[1].Sealed) != 0 {
		failf("the last full page: %+v", pg)
	}

	// the sweep: the tombstone leaves, the horizon rises above every live revision, the counter stays (item GA)
	r.clock.Add(181 * 24 * time.Hour)
	if n, err := r.env.Notes.PurgeTombstones(r.clock.Now()); err != nil || n != 1 {
		failf("the sweep purged %d tombstones: %v", n, err)
	}
	tr.note("the sweep of item FG purges the tombstone 181 days after its storedAt: `notesHorizon` rises to 5, above every live revision (1 to 3), and `notesRevision` stays 5 - the counter is the account's, never the store's maximum (item GA)")

	// a `since` below the horizon starts a full listing from the lowest record (item FI), and the
	// continuation answers above `since` instead of restarting: the loop the `full` flag closes (item FW)
	pg = pagesOf(0, true, "the same request after the purge, with `full`: `notesHorizon` no longer meets the eye, the answer is a function of the request and the store alone - records above `since`, still one per page")
	if !pg.Full || !pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageA || pg.NotesHorizon != 5 {
		failf("the first page after the purge: %+v", pg)
	}
	pg = pagesOf(0, false, "a `since` below the horizon, without `full`: item FI answers with a full listing from the lowest record - `full` true - and `more` true; a literal continuation from here would loop forever, which is the hole item FW closes")
	if !pg.Full || !pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageA || pg.NotesHorizon != 5 {
		failf("the first page of the post-purge full listing: %+v", pg)
	}
	pg = pagesOf(1, true, "the continuation after the purge carries `full: true` and the last revision received: the answer starts above it and does not restart from the lowest")
	if !pg.Full || !pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageB {
		failf("the continuation after the purge: %+v", pg)
	}
	pg = pagesOf(2, true, "the last page after the purge: `more` false, the tombstone is gone from the store, the listing ends")
	if !pg.Full || pg.More || len(pg.Records) != 1 || pg.Records[0].NoteID != notePageC || pg.NotesRevision != 5 {
		failf("the last page after the purge: %+v", pg)
	}

	// the streams end
	c.send(&wire.Bye{})
	c.expectClosed()
	a.send(&wire.Bye{})
	a.expectClosed()
	b.send(&wire.Bye{})
	b.expectClosed()
	return tr
}

// scenarioNotesBoxBound is the `notes-box-bound` vector of item GB (item FX, ticket L0322): a put whose
// delivered `notes_changes` frame, counters at their widest, is exactly 262144 bytes is stored; one byte
// larger is refused `capacity` and stores nothing, the boundary computed by the generator and not written
// by hand. A box at the item FH bound - a clear payload of 131072 bytes and 40 recipients - is stored, and
// it is a real box of the e2e file, a sealed case that opens to its payload.
func scenarioNotesBoxBound() (*transcript, []sealedCase) {
	r := newRig("notes-box-bound", rigOpts{sealed: true, notes: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("notes-box-bound",
		"The delivery bound of a notes record (DEVICE-EXCHANGE 1.17, item FX): a server stores a record only if the "+
			"`notes_changes` frame that would carry it alone is at most 262144 bytes, and a put above it is refused "+
			"`capacity` and stores nothing. The bound is measured with the counters at their widest, so the answer holds for "+
			"every revision to come, and on both of the frames the record can travel in - the listing and the `current` of "+
			"a `note-conflict`. A put frame itself above the limit stays an oversize frame: a stream closed with no answer, "+
			"items T and AN, and is a test of the codec, not of this vector. The boundary box here is padding, the shape of "+
			"a sender that does not follow item FH; a conforming box has about 78000 bytes to spare and never reaches the "+
			"bound. The box at the item FH bound is real.")
	a, phone := r.enrollNoteDevice(tr, "phone-control", "Vector phone", "fms-android", e2eDevices["phone"])
	r.control(a, phone.ID)
	b, tablet := r.enrollNoteDevice(tr, "tablet-control", "Vector tablet", "fms-android", e2eDevices["tablet"])
	r.control(b, tablet.ID)

	r.clock.Add(time.Minute)
	bound, boundLen := widestBoundBox(noteBound, phone.ID, "x")
	widest := wire.NoteRecord{NoteID: noteBound, Revision: math.MaxInt64, StoredAt: widestNoteTime,
		SourceDeviceID: phone.ID, Sealed: bound}
	if n := widestFrameLen(widest); n != wire.MaxFrameAuth {
		failf("the box of %d bytes delivers a frame of %d bytes, want %d", boundLen, n, wire.MaxFrameAuth)
	}
	tr.note(fmt.Sprintf("the generator finds the exact edge by search: a padding box of %d bytes, whose record delivers a `notes_changes` of exactly %d bytes at the widest counters - the figure is computed, written by no one", boundLen, widestFrameLen(widest)))

	// at the exact edge: stored, delivered whole, and it travels as the `current` of a conflict too
	a.send(&wire.NotesPut{NoteID: noteBound, BaseRevision: int64Ptr(0), Sealed: bound})
	if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 1 {
		failf("the put at the bound: revision %d", st.Revision)
	}
	b.expect(wire.TypeNotesChanged) // the other device is told of the stored record
	a.send(&wire.NotesList{Since: int64Ptr(0)})
	ans := expectNotesChanges(a)
	if len(ans.Records) != 1 || string(ans.Records[0].Sealed) != string(bound) || ans.Full {
		failf("the listing of the record at the bound: %+v", ans)
	}
	tr.note("a put whose delivered frame is exactly at the bound is stored and listed whole")
	a.send(&wire.NotesPut{NoteID: noteBound, BaseRevision: int64Ptr(0), Sealed: bound})
	if f := a.expect(wire.TypeRefused).(*wire.Refused); f.Reason != string(wire.ReasonNoteConflict) || f.Current == nil || string(f.Current.Sealed) != string(bound) {
		failf("the conflict at the exact edge: %+v", f)
	}
	tr.note("the record at the exact edge also travels as the `current` of a `note-conflict`, whole: the bound is measured on both of the frames the record can appear in, which is the exact-edge rule the fold left to this implementation")

	// one byte larger: refused capacity, nothing stored, the stream stays open
	over := padBox(boundLen+1, "x")
	if boxNoteFits(noteBound, phone.ID, over) {
		failf("a box one byte larger must not fit")
	}
	a.send(&wire.NotesPut{NoteID: noteBound, BaseRevision: int64Ptr(1), Sealed: over})
	if f := a.expect(wire.TypeRefused).(*wire.Refused); f.Reason != string(wire.ReasonCapacity) {
		failf("a box one byte larger: refused %s, want capacity", f.Reason)
	}
	tr.note("one byte more is refused `capacity`: the note is not stored, the frame the put traveled in was well formed, and the stream answers and stays open (item FX replaces the closed stream of the superseded assumption)")
	if _, _, held := r.env.Notes.State(account); held != 1 {
		failf("an oversize put changed the store: %d records", held)
	}
	a.send(&wire.NotesList{Since: int64Ptr(1)})
	expectNotesChanges(a)
	tr.note("the stream serves the next request after the refusal")

	// the box at the item FH bound: a clear payload of 131072 bytes, 40 recipients, stored whole
	r.clock.Add(time.Minute)
	created := r.clock.Now().UTC().Format("2006-01-02T15:04:05.000Z")
	payloadHead := `{"formatVersion":1,"title":"Bound","kind":"text","text":"`
	payloadTail := `","createdAt":"` + created + `","updatedAt":"` + created + `"}`
	padding := wire.MaxNotePayload - len(payloadHead) - len(payloadTail)
	if padding < 0 {
		failf("the head of the payload does not fit the bound of item FH")
	}
	payload := payloadHead + strings.Repeat("y", padding) + payloadTail
	if len(payload) != wire.MaxNotePayload {
		failf("the payload at the bound of item FH is %d bytes, want %d", len(payload), wire.MaxNotePayload)
	}
	recipients := make([]party, 0, 40)
	for i := range 40 {
		if i%2 == 0 {
			recipients = append(recipients, tablet)
		} else {
			recipients = append(recipients, phone)
		}
	}
	box, _ := sealBox("notes-box-bound/fh-bound", tablet, recipients, "note:"+noteFH, payload)
	caseFH := sealedCase{Name: "notes-box-bound/fh-bound", Transcript: tr.Name, Context: "note:" + noteFH,
		From: tablet.view(), Payload: json.RawMessage(payload), Box: box}
	for range 20 {
		caseFH.To = append(caseFH.To, tablet.view(), phone.view())
	}
	b.send(&wire.NotesPut{NoteID: noteFH, BaseRevision: int64Ptr(0), Sealed: box})
	if st := b.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 2 {
		failf("the put at the item FH bound: revision %d, want 2", st.Revision)
	}
	a.expect(wire.TypeNotesChanged)
	tr.note(fmt.Sprintf("a box at the item FH bound - a clear payload of %d bytes, 40 recipient entries, %d bytes stored - is kept: it is the largest box a conforming sender writes, about 78000 bytes inside the delivery bound", wire.MaxNotePayload, len(box)))

	// the streams end
	a.send(&wire.Bye{})
	a.expectClosed()
	b.send(&wire.Bye{})
	b.expectClosed()
	return tr, []sealedCase{caseFH}
}

// scenarioNotesConflictWithoutCurrent is the `notes-conflict-without-current` vector of item GB (item FX,
// ticket L0322): a put with `baseRevision` above 0 for a `noteId` that no record holds is refused
// `note-conflict` with no `current` - the record was purged, or the note was never stored. The client rule
// is item FJ's tombstone case: an edited note survives as a new note, an unedited one is removed as deleted
// on the server, and the product never shows this as a failure.
func scenarioNotesConflictWithoutCurrent() *transcript {
	r := newRig("notes-conflict-without-current", rigOpts{sealed: true, notes: true, logins: []string{account}})
	defer mustClose(r)
	tr := r.newTranscript("notes-conflict-without-current",
		"A `note-conflict` without `current` (DEVICE-EXCHANGE 1.17, item FX): the client rule is the tombstone case of item FJ. "+
			"An edited local note survives as a new note (new `noteId`, `baseRevision` 0) and the old `noteId` is removed "+
			"locally as deleted on the server; an unedited local note, a resealing put included, is removed locally as deleted "+
			"on the server; a local delete is complete and nothing survives. The product never shows this as a failure.")
	a, phone := r.enrollNoteDevice(tr, "phone-control", "Vector phone", "fms-android", e2eDevices["phone"])
	r.control(a, phone.ID)
	tr.note("the account holds no record: the store has never seen a note")

	// a live put whose base names a record the server never held
	box := padBox(500, "c")
	a.send(&wire.NotesPut{NoteID: noteConflict, BaseRevision: int64Ptr(5), Sealed: box})
	f := a.expect(wire.TypeRefused).(*wire.Refused)
	if f.Reason != string(wire.ReasonNoteConflict) || f.Current != nil {
		failf("a put with a base for a record the store does not hold: %+v, want note-conflict without current", f)
	}
	tr.note("baseRevision 5 names no revision the server holds: refused `note-conflict` with no `current` - the record was purged, or the note was never stored (a frame with no `current` member at all)")

	// a delete of the same shape
	a.send(&wire.NotesPut{NoteID: noteConflict, BaseRevision: int64Ptr(5), Deleted: true})
	f = a.expect(wire.TypeRefused).(*wire.Refused)
	if f.Reason != string(wire.ReasonNoteConflict) || f.Current != nil {
		failf("a delete with a base for a record the store does not hold: %+v", f)
	}
	tr.note("a local delete that meets the same answer is complete: nothing survives, the client rule of item FJ")

	// the successor: the client puts its edited note as a new note, base 0
	a.send(&wire.NotesPut{NoteID: noteSurvivor, BaseRevision: int64Ptr(0), Sealed: padBox(500, "new")})
	if st := a.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 1 {
		failf("the surviving new note: revision %d, want 1", st.Revision)
	}
	tr.note("what the client does next (an edited note): it survives as a new note with a fresh noteId and baseRevision 0 - the store takes it like any create")

	a.send(&wire.Bye{})
	a.expectClosed()
	return tr
}

// scenarioNotesCountBytesPolicy is the `notes-count-bytes-and-policy` vector of item GB (items FY and FZ,
// ticket L0322): at the count limit a create is refused `limit-reached` and an edit is accepted; at the
// byte limit a raising put is refused, a non-raising edit and a delete are accepted; `policy.set` and
// `account.limits` accept `notesPerAccount` and `notesMaxBytes`, zero is a value, and both answer
// `bad-limit` to a negative or fractional value. The administration frames travel the remote operator
// stream (REMOTE-ADMIN), the notes frames the device's control stream, one account, one server.
func scenarioNotesCountBytesPolicy() *transcript {
	adminPassword := "AdminSecretPass1"
	adminHash, err := password.HashWith(adminPassword, testkit.Cheap())
	if err != nil {
		failf("admin hash: %v", err)
	}
	adminCfg := config.Admin{On: true, PasswordRecord: adminHash, AllowedAddresses: []string{"127.0.0.1"},
		SessionLifetimeMinutes: 15, PasswordFloor: 10}
	r := newRig("notes-count-bytes-and-policy", rigOpts{
		sealed: true, notes: true, admin: &adminCfg, logins: []string{account},
	})
	defer mustClose(r)
	tr := r.newTranscript("notes-count-bytes-and-policy",
		"The notes limits and the administration wire (DEVICE-EXCHANGE 1.17, items FY and FZ): the own limits object "+
			"gains `notesPerAccount` and `notesMaxBytes`, the effective limits object `notes` and `notesBytes`; `policy.set` "+
			"sets the defaults in force, `account.limits` sets one account's own values, zero is a value and not \"back to "+
			"the default\", and a value that is not a whole number or is negative is answered `bad-limit`. On the device "+
			"side, item FL refuses only the put that would create a record when the account holds the count; item FZ "+
			"refuses only a put that raises the account's bytes above the limit, and never an edit that does not raise it "+
			"or a delete. Neither limit ever trims what exists.")
	d, phone := r.enrollNoteDevice(tr, "phone-control", "Vector phone", "fms-android", e2eDevices["phone"])
	r.control(d, phone.ID)

	// the operator's stream
	s := r.open(tr, "admin", "tls", "remote administration: the notes limits on the operator wire (items FY and FZ)")
	s.send(&wire.AdminAuth{Password: adminPassword})
	ok := s.expect(wire.TypeAdminAuthOK).(*wire.AdminAuthOK)
	s.auth()
	tr.note("`admin-auth-ok` on the operator stream: the token of the session; administration is on, the password is the one the operator set")
	var reqID int64
	ask := func(op string, req *wire.AdminRequest) *wire.AdminReply {
		reqID++
		req.ID = reqID
		req.Token = ok.Token
		req.Op = op
		s.send(req)
		return s.expect(wire.TypeAdminReply).(*wire.AdminReply)
	}
	askRaw := func(op, login, limits string) *wire.AdminReply {
		reqID++
		body := fmt.Sprintf(`{"schemaVersion":%d,"type":"admin-request","id":%d,"token":%q,"op":%q,"login":%q,"limits":%s}`,
			wire.SchemaVersion, reqID, ok.Token, op, login, limits)
		s.sendFrame(testkit.RawFrame(body))
		return s.expect(wire.TypeAdminReply).(*wire.AdminReply)
	}
	policyDefaults := func(rep *wire.AdminReply) (int, int64) {
		if !rep.OK {
			failf("the policy answer: %+v", rep)
		}
		var v struct {
			Defaults struct {
				Notes      int   `json:"notes"`
				NotesBytes int64 `json:"notesBytes"`
			} `json:"defaults"`
		}
		if err := json.Unmarshal(rep.Data, &v); err != nil {
			failf("the policy answer: %v", err)
		}
		return v.Defaults.Notes, v.Defaults.NotesBytes
	}

	// policy.set accepts both notes members (item FY, FZ); the reply shows them in defaults
	two, sevenHundred := 2, int64(700)
	if n, b := policyDefaults(ask(admin.OpPolicySet, &wire.AdminRequest{Limits: &wire.AdminLimits{
		NotesPerAccount: &two, NotesMaxBytes: &sevenHundred}})); n != 2 || b != 700 {
		failf("policy.set did not take the notes members: defaults %d / %d", n, b)
	}
	tr.note("`policy.set` carries the own limits object with `notesPerAccount` 2 and `notesMaxBytes` 700; the reply of `policy.status` shows them in `defaults.notes` and `defaults.notesBytes`")

	// the count of item FL: a create over it is refused, an edit never is
	r.clock.Add(time.Minute)
	d.send(&wire.NotesPut{NoteID: noteCountA, BaseRevision: int64Ptr(0), Sealed: padBox(200, "a")})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 1 {
		failf("the first note: revision %d", st.Revision)
	}
	tr.note("the account holds one record of a 200-byte box; the limits in force are the defaults the operator just set")
	d.send(&wire.NotesPut{NoteID: noteCountB, BaseRevision: int64Ptr(0), Sealed: padBox(200, "b")})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 2 {
		failf("the second note: revision %d", st.Revision)
	}
	d.send(&wire.NotesPut{NoteID: noteCountGone, BaseRevision: int64Ptr(0), Sealed: padBox(200, "c")})
	if f := d.expect(wire.TypeRefused).(*wire.Refused); f.Reason != string(wire.ReasonLimitReached) {
		failf("a create over the count: refused %s, want limit-reached", f.Reason)
	}
	tr.note("the account holds the count (2 tombstones included): the next create is refused `limit-reached`, the row of item FL - \"This server keeps no more notes for this account\" (\"Delete some notes, or ask whoever runs the server.\")")
	d.send(&wire.NotesPut{NoteID: noteCountA, BaseRevision: int64Ptr(1), Sealed: padBox(150, "e")})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 3 {
		failf("an edit at the count cap: revision %d", st.Revision)
	}
	tr.note("an edit at the cap is accepted: the count refuses only a create (item FL)")

	// the bytes of item FZ: only a put that raises the total above the limit is refused
	d.send(&wire.NotesPut{NoteID: noteCountB, BaseRevision: int64Ptr(2), Sealed: padBox(600, "r")})
	if f := d.expect(wire.TypeRefused).(*wire.Refused); f.Reason != string(wire.ReasonLimitReached) {
		failf("a raising put over the bytes: refused %s, want limit-reached", f.Reason)
	}
	tr.note("a raising edit that would lift the account above `notesMaxBytes` (150 + 600 over 700) is refused `limit-reached`, the reason of item FZ: it never trims what exists")
	d.send(&wire.NotesPut{NoteID: noteCountB, BaseRevision: int64Ptr(2), Sealed: padBox(100, "n")})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 4 {
		failf("a non-raising edit: revision %d", st.Revision)
	}
	tr.note("a non-raising edit is accepted regardless of the byte limit (item FZ: the refusal needs a put that raises the total)")
	d.send(&wire.NotesPut{NoteID: noteCountA, BaseRevision: int64Ptr(3), Deleted: true})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 5 {
		failf("a delete with the byte limit in force: revision %d", st.Revision)
	}
	tr.note("a delete is never refused for the bytes (item FZ)")

	// account.limits sets one account's own values; zero is a value (item FY)
	zero, fiveHundred := 0, int64(500)
	if rep := ask(admin.OpAccountLimits, &wire.AdminRequest{Login: account,
		Limits: &wire.AdminLimits{NotesPerAccount: &zero, NotesMaxBytes: &fiveHundred}}); !rep.OK {
		failf("account.limits with zero: %+v", rep)
	}
	rep := ask(admin.OpAccountList, &wire.AdminRequest{})
	if !rep.OK {
		failf("account.list: %+v", rep)
	}
	var list []struct {
		Login  string `json:"login"`
		Limits *struct {
			NotesPerAccount *int   `json:"notesPerAccount"`
			NotesMaxBytes   *int64 `json:"notesMaxBytes"`
		} `json:"limits"`
		Effective struct {
			Notes      int   `json:"notes"`
			NotesBytes int64 `json:"notesBytes"`
		} `json:"effective"`
	}
	if err := json.Unmarshal(rep.Data, &list); err != nil {
		failf("account.list: %v", err)
	}
	if len(list) != 1 || list[0].Login != account || list[0].Limits == nil ||
		list[0].Limits.NotesPerAccount == nil || *list[0].Limits.NotesPerAccount != 0 ||
		list[0].Limits.NotesMaxBytes == nil || *list[0].Limits.NotesMaxBytes != 500 ||
		list[0].Effective.Notes != 0 || list[0].Effective.NotesBytes != 500 {
		failf("the account after its own zero limits: %+v", list)
	}
	tr.note("`account.limits` sets the account's own `notesPerAccount` 0 and `notesMaxBytes` 500; zero is a value, not \"back to the default\" - `account.list` shows both in the account's `limits` and in `effective`")
	d.send(&wire.NotesPut{NoteID: noteCountLater, BaseRevision: int64Ptr(0), Sealed: padBox(50, "z")})
	if f := d.expect(wire.TypeRefused).(*wire.Refused); f.Reason != string(wire.ReasonLimitReached) {
		failf("a create with a zero count: refused %s, want limit-reached", f.Reason)
	}
	tr.note("a count of 0 means the account may hold no record: the create is refused, the rule of zero the fold wrote into item FY")
	d.send(&wire.NotesPut{NoteID: noteCountB, BaseRevision: int64Ptr(4), Sealed: padBox(50, "x")})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 6 {
		failf("an edit with a zero count: revision %d", st.Revision)
	}
	d.send(&wire.NotesPut{NoteID: noteCountB, BaseRevision: int64Ptr(6), Deleted: true})
	if st := d.expect(wire.TypeNotesStored).(*wire.NotesStored); st.Revision != 7 {
		failf("a delete with a zero count: revision %d", st.Revision)
	}
	tr.note("edits and deletes are never refused for the count, whatever it is (item FL)")

	// the bad-limit rule of item FY: negative over the typed wire, fractional over a hand-built frame
	negative := -1
	if rep := ask(admin.OpAccountLimits, &wire.AdminRequest{Login: account,
		Limits: &wire.AdminLimits{NotesPerAccount: &negative}}); rep.OK || rep.Code != admin.CodeBadLimit {
		failf("a negative notes count: %+v, want bad-limit", rep)
	}
	tr.note("a negative `notesPerAccount` is answered `bad-limit` and changes nothing - the code the binding already had")
	if rep := askRaw(admin.OpPolicySet, "", `{"notesMaxBytes":2.5}`); rep.OK || rep.Code != admin.CodeBadLimit {
		failf("a fractional notes byte limit on policy.set: %+v, want bad-limit", rep)
	}
	tr.note("a fractional value is a frame the codec would refuse to decode; the notes members ride past it whole and the operation answers `bad-limit`, the stream staying open for the next request (item FY)")
	if rep := askRaw(admin.OpAccountLimits, account, `{"notesPerAccount":1.5}`); rep.OK || rep.Code != admin.CodeBadLimit {
		failf("a fractional notes count on account.limits: %+v, want bad-limit", rep)
	}
	if n, b := policyDefaults(ask(admin.OpPolicyStatus, &wire.AdminRequest{})); n != 2 || b != 700 {
		failf("a refused limit changed the policy: %d / %d", n, b)
	}
	tr.note("the refusals changed nothing: `policy.status` still shows the defaults the operator set, and the stream serves the next request")

	// the streams end
	s.send(&wire.Bye{})
	s.expectClosed()
	d.send(&wire.Bye{})
	d.expectClosed()
	return tr
}
