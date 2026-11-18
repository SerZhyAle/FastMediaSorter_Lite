package wire

import (
	"encoding/json"
	"errors"
	"math"
)

// This file is ticket L0321: the notes of an account on the exchange server, DEVICE-EXCHANGE 1.13 items FG to FM.
// Five frames on the control stream: a member's device reads the changes of its account's notes store, puts one
// note as a whole record (or deletes it), and the server tells the account's other devices that a put was
// accepted. The names are snake case like the settings frames of 1.12 and are wire values from 1.13 on. The server
// never opens a box: `sealed` is carried as the bytes it came with and is checked only to be one JSON object.
const (
	TypeNotesList    = "notes_list"
	TypeNotesChanges = "notes_changes"
	TypeNotesPut     = "notes_put"
	TypeNotesStored  = "notes_stored"
	TypeNotesChanged = "notes_changed"

	// FeatureNotes is the `welcome`/`enrolled` feature of item FI: listed only together with FeatureSealed and
	// only when the server holds the notes store.
	FeatureNotes = "notes"
	// RoleNotes is the device role of item FI: a device that takes part in the notes sync declares it. It is
	// information, not a gate - it only chooses who receives `notes_changed` and who is a recipient of a box.
	RoleNotes = "notes"

	// MaxNotePayload is the bound of item FH on the clear payload inside a box, in bytes of UTF-8. It is the
	// clients' limit and is informational here: the server never reads a box, so the only bound it can hold a
	// box to is the frame (item T), which MaxRecord (a limit of the directory records) cannot stand for.
	MaxNotePayload = 131072
)

// NoteRecord is one note of an account's store (item FG): what `notes_changes` lists and what a `refused`
// carries as `current`. Sealed is the box of item DH with the context `note:<noteId>`, absent on a tombstone;
// the server holds it as an opaque object and returns it byte for byte.
type NoteRecord struct {
	NoteID         string          `json:"noteId"`
	Revision       int64           `json:"revision"`
	StoredAt       string          `json:"storedAt"`
	SourceDeviceID string          `json:"sourceDeviceId"`
	Deleted        bool            `json:"deleted,omitempty"`
	Sealed         json.RawMessage `json:"sealed,omitempty"`
}

// NotesList asks for the records above `since` (item FI). Since is a pointer so that an absent member closes
// the stream (item AN) instead of reading as 0, which would ask for the whole store.
type NotesList struct {
	Head
	Since *int64 `json:"since"`
	Full  bool   `json:"full,omitempty"`
}

// TypeName is "notes_list".
func (*NotesList) TypeName() string { return TypeNotesList }

// NotesChanges answers a `notes_list`. More and Full are always written: a client reads an absent boolean as
// a reason to ask again, and Records is an empty list, never null.
type NotesChanges struct {
	Head
	NotesRevision int64        `json:"notesRevision"`
	NotesHorizon  int64        `json:"notesHorizon"`
	Records       []NoteRecord `json:"records"`
	More          bool         `json:"more"`
	Full          bool         `json:"full"`
}

// TypeName is "notes_changes".
func (*NotesChanges) TypeName() string { return TypeNotesChanges }

// NotesPut stores one note as a whole record or deletes it (items FI and FJ). BaseRevision is a pointer so
// that an absent member closes the stream: 0 means "the server has never stored this note", and a missing
// member must not be read as that.
type NotesPut struct {
	Head
	NoteID       string          `json:"noteId"`
	BaseRevision *int64          `json:"baseRevision"`
	Deleted      bool            `json:"deleted,omitempty"`
	Sealed       json.RawMessage `json:"sealed,omitempty"`
}

// TypeName is "notes_put".
func (*NotesPut) TypeName() string { return TypeNotesPut }

// NotesStored answers an accepted put: the record's new revision and the server's clock at the store.
type NotesStored struct {
	Head
	NoteID   string `json:"noteId"`
	Revision int64  `json:"revision"`
	StoredAt string `json:"storedAt"`
}

// TypeName is "notes_stored".
func (*NotesStored) TypeName() string { return TypeNotesStored }

// NotesChanged is pushed, unsolicited, to the other devices of the account that hold the role `notes` after an
// accepted put. A client may sync sooner on it and never relies on it to sync at all.
type NotesChanged struct {
	Head
	NotesRevision int64 `json:"notesRevision"`
}

// TypeName is "notes_changed".
func (*NotesChanged) TypeName() string { return TypeNotesChanged }

// ValidNoteID reports whether s is a note id of item FG: a UUID in the lower-case 8-4-4-4-12 form and nothing
// else. It is strict because the id is the key of the record and part of the context of its box, so two
// spellings of one id would be two notes; the version and variant digits are not looked at.
func ValidNoteID(s string) bool {
	if len(s) != 36 {
		return false
	}
	for i := 0; i < len(s); i++ {
		switch i {
		case 8, 13, 18, 23:
			if s[i] != '-' {
				return false
			}
		default:
			if !lowerHex(s[i]) {
				return false
			}
		}
	}
	return true
}

func lowerHex(c byte) bool { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') }

// CheckNoteBox is the whole of what the server asks of a notes box: it is one JSON object. MaxRecord is not
// applied - a box of a note at the payload limit is about 185 KiB - and the frame limit already bounds it; the
// server never looks at a member of the box.
func CheckNoteBox(raw json.RawMessage) error {
	if len(raw) == 0 || raw[0] != '{' {
		return errors.New("a note box is one JSON object")
	}
	if !json.Valid(raw) {
		return errors.New("a note box is not valid JSON")
	}
	return nil
}

// NoteFitsFrame reports whether rec, alone, still encodes within MaxFrameAuth both as the only record of a
// `notes_changes` and as the `current` of a `refused` (item T: a server never accepts what it could not
// deliver). The counters are taken at their widest, so the answer holds for the next revisions too.
func NoteFitsFrame(rec NoteRecord) bool {
	list := &NotesChanges{NotesRevision: math.MaxInt64, NotesHorizon: math.MaxInt64, Records: []NoteRecord{rec}}
	refusal := Refuse(ReasonNoteConflict)
	refusal.Current = &rec
	for _, e := range []Envelope{list, refusal} {
		body, err := Encode(e)
		if err != nil || len(body) > MaxFrameAuth {
			return false
		}
	}
	return true
}

// Validate holds the shape of a record the server sends (item FG): a note id, a revision from 1, a time, the
// device that stored it, a box on a live record and none on a tombstone.
func (r *NoteRecord) Validate() error {
	if !ValidNoteID(r.NoteID) {
		return errors.New("noteId is not a note id")
	}
	if r.Revision < 1 {
		return errors.New("revision is at least 1")
	}
	if _, err := ParseTime(r.StoredAt); err != nil {
		return errors.New("storedAt is not a time")
	}
	if err := required("sourceDeviceId", r.SourceDeviceID, maxTunnelID); err != nil {
		return err
	}
	if r.Deleted {
		if len(r.Sealed) != 0 {
			return errors.New("a tombstone carries no box")
		}
		return nil
	}
	if err := CheckNoteBox(r.Sealed); err != nil {
		return errors.New("a live record needs a box")
	}
	return nil
}

func (e *NotesList) validate() error {
	if e.Since == nil {
		return errors.New("since is required")
	}
	if *e.Since < 0 {
		return errors.New("since is at least 0")
	}
	return nil
}

func (e *NotesPut) validate() error {
	if !ValidNoteID(e.NoteID) {
		return errors.New("noteId is not a note id")
	}
	if e.BaseRevision == nil {
		return errors.New("baseRevision is required")
	}
	if *e.BaseRevision < 0 {
		return errors.New("baseRevision is at least 0")
	}
	if e.Deleted {
		// a tombstone has no box, so one that came along is not checked: the engine drops it (item FG)
		return nil
	}
	if err := CheckNoteBox(e.Sealed); err != nil {
		return errors.New("a live note needs a box")
	}
	return nil
}

func (e *NotesChanges) validate() error {
	if e.NotesRevision < 0 {
		return errors.New("notesRevision is at least 0")
	}
	if e.NotesHorizon < 0 {
		return errors.New("notesHorizon is at least 0")
	}
	var last int64
	for i := range e.Records {
		r := &e.Records[i]
		if err := r.Validate(); err != nil {
			return err
		}
		if r.Revision <= last {
			return errors.New("records are not ascending by revision")
		}
		last = r.Revision
	}
	return nil
}

func (e *NotesStored) validate() error {
	if !ValidNoteID(e.NoteID) {
		return errors.New("noteId is not a note id")
	}
	if e.Revision < 1 {
		return errors.New("revision is at least 1")
	}
	if _, err := ParseTime(e.StoredAt); err != nil {
		return errors.New("storedAt is not a time")
	}
	return nil
}

func (e *NotesChanged) validate() error {
	if e.NotesRevision < 1 {
		return errors.New("notesRevision is at least 1")
	}
	return nil
}
