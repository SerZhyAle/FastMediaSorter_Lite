package rendezvous

import (
	"bytes"
	"encoding/json"
	"errors"
	"io/fs"
	"math"
	"os"
	"slices"
	"sync"
	"time"

	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/notes"
	"fms/exchange-server/internal/wire"
)

// This file is ticket L0321: the notes of an account, DEVICE-EXCHANGE 1.13 (items FG to FM). Two frames of a
// device run on the control stream and are answered by exactly one frame each - `notes_list` by `notes_changes`,
// `notes_put` by `notes_stored` or `refused` - and an accepted put is told to the account's other devices that
// hold the role `notes` by `notes_changed`, queued and never awaited. The frames were checked member by member by
// the wire layer before they got here: a member out of bounds closed the stream there.
//
// The server never opens a box. A note's box is carried as the bytes it came with and is never logged, audited or
// put into an error text; the log names counts and reasons only.

// notesWidestTime is a `storedAt` of the width the store writes, for the size check of a put (the store writes
// UTC RFC 3339 with no fraction, so every time has this width).
const notesWidestTime = "9999-12-31T23:59:59Z"

// notesListed reports whether this server lists `notes` (item FI): it holds the store and `sealed` is listed,
// because a note is a box of item DH. Both the feature list and the two handlers ask this one question.
func (e *Engine) notesListed() bool { return e.cfg.Notes != nil && e.cfg.Features.Sealed }

// notesStoreWhy is the text a store failure may put into the log: a file system error names a path and an
// operation, nothing of a record. Any other error says only that the store failed, so that no member of a box
// can reach the log through an error text (a JSON error may quote the character it stopped at).
func notesStoreWhy(err error) string {
	var pe *fs.PathError
	var le *os.LinkError
	if errors.As(err, &pe) || errors.As(err, &le) {
		return err.Error()
	}
	return "store"
}

// noteRecord is a stored record in the wire's shape.
func noteRecord(r notes.Record) wire.NoteRecord {
	return wire.NoteRecord{NoteID: r.NoteID, Revision: r.Revision, StoredAt: r.StoredAt,
		SourceDeviceID: r.SourceDeviceID, Deleted: r.Deleted, Sealed: r.Sealed}
}

// --- notes_put ---

// handleNotesPut is `notes_put` (items FI, FJ, FL and FX). The order of the checks is the contract's, so two
// servers answer one request alike: a guest, then the frame, then the store - the conflict rule, the count
// limit, the byte limit, the delivery bound, the write (item FX, ticket L0322). A refusal changes nothing;
// a put whose delivered record would not fit one frame is refused `capacity` and never closes the stream
// (the assumption this replaces closed the stream here).
func (e *Engine) handleNotesPut(cs *ControlStream, sess *Session, m *wire.NotesPut) bool {
	acct, dev := sess.Account, sess.DeviceID
	status := logx.AuditRefusal("incomplete")
	defer func() {
		ev := logx.AuditEvent{Op: "notes.put", Target: acct, Remote: cs.st.raw.RemoteAddr().String(), Transport: cs.st.Transport(), Status: status, Details: "notes store"}
		auditSession(&ev, sess)
		e.audit(ev)
	}()
	refuse := func(r wire.Reason, why string) bool {
		status = logx.AuditRefusal(string(r))
		e.cfg.Log.Info("notes-put-refused", logx.Device(dev), logx.Login(acct), logx.Text("reason", string(r)), logx.Text("why", why))
		return cs.Send(wire.Refuse(r)) == nil
	}
	if e.cfg.Accounts.IsGuest(acct) {
		return refuse(wire.ReasonNotPermitted, "guest")
	}

	// A tombstone has no box: one that came along is dropped. A box is kept as compact JSON, the form the store
	// holds and the answers carry, so it is measured in that form (items FG and T: a server never accepts what it
	// could not deliver).
	var box json.RawMessage
	if !m.Deleted {
		var buf bytes.Buffer
		if err := json.Compact(&buf, m.Sealed); err != nil {
			e.cfg.Log.Info("notes-put-malformed", logx.Device(dev), logx.Login(acct), logx.Text("why", "box"))
			return false
		}
		box = buf.Bytes()
	}
	// the widest counters, so the answer holds for the revisions to come. The bound is measured on the
	// frame that carries this record alone, in both of its forms - the `notes_changes` of a listing and
	// the `current` of a `note-conflict` (wire.NoteFitsFrame) - so a record stored at the exact edge is
	// delivered whole in every answer it can produce (item FX and its exact-edge rule, ticket L0322).
	widest := wire.NoteRecord{NoteID: m.NoteID, Revision: math.MaxInt64, StoredAt: notesWidestTime,
		SourceDeviceID: dev, Deleted: m.Deleted, Sealed: box}
	deliverable := wire.NoteFitsFrame(widest)

	limits := e.cfg.Accounts.LimitsFor(acct)
	rec, err := e.cfg.Notes.PutLimited(acct, dev, m.NoteID, *m.BaseRevision, m.Deleted, box, limits.Notes, limits.NotesBytes, deliverable)
	var conflict *notes.ConflictError
	switch {
	case errors.As(err, &conflict):
		// `current` is the stored record, box included; none when the store holds no record of the id (a copy
		// older than a purge, or a name the server never stored), which the client reads as "gone from the
		// server" (item FX)
		r := wire.Refuse(wire.ReasonNoteConflict)
		if conflict.Current != nil {
			cur := noteRecord(*conflict.Current)
			r.Current = &cur
		}
		status = logx.AuditRefusal(string(wire.ReasonNoteConflict))
		e.cfg.Log.Info("notes-put-refused", logx.Device(dev), logx.Login(acct), logx.Text("reason", string(wire.ReasonNoteConflict)))
		return cs.Send(r) == nil
	case errors.Is(err, notes.ErrLimitReached):
		return refuse(wire.ReasonLimitReached, "limits")
	case errors.Is(err, notes.ErrCapacity):
		// the delivery bound of item FX: the record could not be carried in one frame, so it is not
		// stored; the stream answers and stays open
		return refuse(wire.ReasonCapacity, "capacity")
	case err != nil:
		// the previous state stays in memory and on disk (Assumed 11)
		e.cfg.Log.Error("notes-put-failed", logx.Device(dev), logx.Login(acct), logx.Text("error", notesStoreWhy(err)))
		return refuse(wire.ReasonUnavailable, "store")
	}

	// The account may have been removed, or made a guest, while this put ran: its notes were erased BEFORE the
	// record above was stored, and a put that outlives the erasure would leave a ledger behind (for a removed login
	// a later account of that name would inherit it; a guest would keep a record). Erase what was just written and
	// answer as for a guest: no `notes_stored` and no push, so no device is told of a record that is gone.
	if !e.cfg.Accounts.Exists(acct) || e.cfg.Accounts.IsGuest(acct) {
		var eerr error
		if !e.cfg.Accounts.Exists(acct) {
			_, eerr = e.cfg.Notes.Erase(acct) // the account is gone: the whole entry, counter included
		} else {
			_, eerr = e.cfg.Notes.EraseRecords(acct) // a guest that may become a member again keeps its counter
		}
		if eerr != nil {
			e.cfg.Log.Error("notes-erase-failed", logx.Device(dev), logx.Login(acct))
		}
		return refuse(wire.ReasonNotPermitted, "account-changed")
	}

	status = logx.AuditStatusOK
	op := "put"
	if rec.Deleted {
		op = "delete"
	}
	_, _, held := e.cfg.Notes.State(acct)
	e.cfg.Log.Info("notes-stored", logx.Device(dev), logx.Login(acct), logx.Text("op", op),
		logx.Int("revision", int(rec.Revision)), logx.Int("records", held))
	answered := cs.Send(&wire.NotesStored{NoteID: rec.NoteID, Revision: rec.Revision, StoredAt: rec.StoredAt}) == nil
	// the push goes out whether or not the answer could be queued: the record is stored either way
	queued, skipped := e.pushNotesChanged(acct, dev, rec.Revision)
	e.cfg.Log.Info("notes-changed-pushed", logx.Device(dev), logx.Login(acct), logx.Int("queued", queued), logx.Int("skipped", skipped))
	return answered
}

// pushNotesChanged queues `notes_changed` to every other online device of the account that declared the role
// `notes` (item FI), and reports how many streams took it and how many could not (Assumed 12). The frame is a
// hint: it is queued and never awaited, a full or closed queue skips that device, and the device that made the put
// is not told. The role is read from the device's record; it chooses who is told and gates nothing else
// (Assumed 13).
func (e *Engine) pushNotesChanged(account, source string, revision int64) (queued, skipped int) {
	frame, err := wire.EncodeFrame(&wire.NotesChanged{NotesRevision: revision}, wire.MaxFrameAuth)
	if err != nil {
		return 0, 0
	}
	var to []*ControlStream
	e.mu.Lock()
	for id, en := range e.ctl {
		if id != source && en.cs.sess.Account == account {
			to = append(to, en.cs)
		}
	}
	e.mu.Unlock()
	for _, cs := range to {
		d, ok := e.cfg.Accounts.Device(account, cs.sess.DeviceID)
		if !ok || !slices.Contains(d.Roles, wire.RoleNotes) {
			continue
		}
		if cs.TrySendFrame(frame) == nil {
			queued++
		} else {
			skipped++
		}
	}
	return queued, skipped
}

// --- notes_list ---

// handleNotesList is `notes_list` answered by `notes_changes` (item FI): the records above `since`, ascending by
// revision, the account's counter and horizon, `full` and `more`. A `since` below the horizon cannot be answered
// as changes - tombstones it never saw may have been purged - so the answer is `full`: every record the store
// holds. A listing too big for one frame is paged: `more` says records were left out and the client asks again
// with the revision of the last record it got.
//
// A continuation carries `full: true` and the last revision, independently of the stream (item FW).
func (e *Engine) handleNotesList(cs *ControlStream, sess *Session, m *wire.NotesList) bool {
	acct, dev := sess.Account, sess.DeviceID
	if e.cfg.Accounts.IsGuest(acct) {
		e.cfg.Log.Info("notes-list-refused", logx.Device(dev), logx.Login(acct),
			logx.Text("reason", string(wire.ReasonNotPermitted)), logx.Text("why", "guest"))
		return cs.Send(wire.Refuse(wire.ReasonNotPermitted)) == nil
	}
	since := *m.Since
	resume := m.Full

	// The counter is read before the records: a put in between shows up as a record above `notesRevision`, which
	// the client asks for again, never as a counter above a record the client was not given. The horizon is read
	// again after the records, and the answer is made again when a sweep moved it in between: a purged tombstone
	// the client has not seen would otherwise be missing from an answer that is not `full`.
	var revision, horizon int64
	var recs []notes.Record
	var full bool
	// After the third pass the loop stops with what it holds even if the horizon moved again (it takes three sweeps
	// inside one request): the reported horizon can then be one sweep behind, which only costs a `full` answer one
	// request later.
	for range 3 {
		revision, horizon, _ = e.cfg.Notes.State(acct)
		full = resume || since < horizon
		if full && !resume {
			recs = e.cfg.Notes.All(acct)
		} else {
			recs = e.cfg.Notes.Above(acct, since)
		}
		if _, again, _ := e.cfg.Notes.State(acct); again == horizon {
			break
		}
	}

	page, more, err := notesPage(recs)
	if err != nil {
		e.cfg.Log.Error("notes-list-failed", logx.Device(dev), logx.Login(acct), logx.Text("why", "encode"))
		return false
	}
	return cs.Send(&wire.NotesChanges{NotesRevision: revision, NotesHorizon: horizon, Records: page, More: more, Full: full}) == nil
}

// notesFrameBase is the encoded length of a `notes_changes` that lists no record, with the counters at their
// widest. A record adds the length of its own encoding and one comma after the first.
var notesFrameBase = sync.OnceValues(func() (int, error) { return notesFrameLen(nil) })

func notesFrameLen(recs []wire.NoteRecord) (int, error) {
	body, err := wire.Encode(&wire.NotesChanges{NotesRevision: math.MaxInt64, NotesHorizon: math.MaxInt64, Records: recs})
	return len(body), err
}

// notesPage cuts the longest run from the start of recs that still encodes into one `notes_changes` within
// MaxFrameAuth, and says whether records were left out. At least one record always goes: the put check of item
// FG (a server never accepts what it could not deliver) makes one record fit alone. The size of each record is
// measured with the codec itself, so the page cannot disagree with the frame it is sent in.
func notesPage(recs []notes.Record) (page []wire.NoteRecord, more bool, err error) {
	base, err := notesFrameBase()
	if err != nil {
		return nil, false, err
	}
	page = make([]wire.NoteRecord, 0, min(len(recs), 64))
	used := base
	for i, r := range recs {
		nr := noteRecord(r)
		one, err := notesFrameLen([]wire.NoteRecord{nr})
		if err != nil {
			return nil, false, err
		}
		if i > 0 && used+1+(one-base) > wire.MaxFrameAuth {
			return page, true, nil
		}
		page = append(page, nr)
		used += one - base
		if i > 0 {
			used++ // the comma between records
		}
	}
	return page, false, nil
}

// --- the sweep ---

// sweepNotes forgets the tombstones older than the contract's age (item FG), at most once per sweep, and writes
// the store only when it purged something. A write that fails is logged without a word of any record and does
// not stop the sweep; the store keeps what it held.
func (e *Engine) sweepNotes(now time.Time) {
	if e.cfg.Notes == nil {
		return
	}
	n, err := e.cfg.Notes.PurgeTombstones(now)
	if err != nil {
		e.cfg.Log.Error("store-write-failed", logx.Text("store", "notes"), logx.Text("error", notesStoreWhy(err)))
		return
	}
	if n > 0 {
		e.cfg.Log.State("notes-tombstones-purged", logx.Int("count", n))
	}
}
