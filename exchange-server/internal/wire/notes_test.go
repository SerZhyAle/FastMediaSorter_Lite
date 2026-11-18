package wire

import (
	"bytes"
	"encoding/json"
	"errors"
	"math"
	"strconv"
	"strings"
	"testing"
)

// Two note ids in the form of DEVICE-EXCHANGE 1.13 item FG, for the samples and the cases below.
const (
	sampleNoteID  = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"
	sampleNoteID2 = "5f1e7c2a-9b3d-4c6e-a8f0-1d2e3f4a5b6c"
)

func int64p(n int64) *int64 { return &n }

// noteConflict is the refusal of item FJ: Refuse is the only way a `refused` is built, the stored record is
// attached after it.
func noteConflict(current NoteRecord) *Refused {
	r := Refuse(ReasonNoteConflict)
	r.Current = &current
	return r
}

// noteRec is the JSON of one record as the server writes it, with the members of tail appended.
func noteRec(id string, rev int, tail string) string {
	return `{"noteId":"` + id + `","revision":` + strconv.Itoa(rev) +
		`,"storedAt":"2026-11-14T10:00:00Z","sourceDeviceId":"` + idA + `"` + tail + `}`
}

func notesPutFrame(members string) []byte {
	return []byte(`{"schemaVersion":2,"type":"notes_put",` + members + `}`)
}

func TestTheNotesNamesAreTheOnesOfTheContract(t *testing.T) {
	// pairs, not a map: the feature and the role are the same word, and a map literal refuses a repeated key
	for _, p := range [][2]string{
		{TypeNotesList, "notes_list"},
		{TypeNotesChanges, "notes_changes"},
		{TypeNotesPut, "notes_put"},
		{TypeNotesStored, "notes_stored"},
		{TypeNotesChanged, "notes_changed"},
		{FeatureNotes, "notes"},
		{RoleNotes, "notes"},
		{string(ReasonNoteConflict), "note-conflict"},
	} {
		if p[0] != p[1] {
			t.Errorf("got %q, want %q", p[0], p[1])
		}
	}
	if MaxNotePayload != 131072 {
		t.Errorf("MaxNotePayload = %d", MaxNotePayload)
	}
	known := false
	for _, r := range KnownRoles {
		known = known || r == RoleNotes
	}
	if !known {
		t.Error("an enroll would drop the role `notes`")
	}
	for _, typ := range []string{TypeNotesList, TypeNotesChanges, TypeNotesPut, TypeNotesStored, TypeNotesChanged} {
		if New(typ) == nil {
			t.Errorf("type %q is not registered", typ)
		}
	}
}

func TestValidNoteIDIsTheLowerCaseUUIDForm(t *testing.T) {
	cases := []struct {
		name, id string
		want     bool
	}{
		{"good", sampleNoteID, true},
		{"good, only digits", "01234567-8901-2345-6789-012345678901", true},
		{"good, only letters", "abcdefab-cdef-abcd-efab-cdefabcdefab", true},
		{"empty", "", false},
		{"upper case", strings.ToUpper(sampleNoteID), false},
		{"one upper-case digit", "0A1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d", false},
		{"one character short", sampleNoteID[:35], false},
		{"one character long", sampleNoteID + "0", false},
		{"no dashes", strings.ReplaceAll(sampleNoteID, "-", ""), false},
		{"a dash missing, a digit added", "0a1b2c3d4e5f-4a6b-8c7d-9e0f1a2b3c4d0", false},
		{"a dash one place early", "0a1b2c3-d4e5f-4a6b-8c7d-9e0f1a2b3c4d", false},
		{"a non-hex letter", "0a1b2c3g-4e5f-4a6b-8c7d-9e0f1a2b3c4d", false},
		{"a space for the last digit", sampleNoteID[:35] + " ", false},
		{"a two-byte rune for the last two digits", sampleNoteID[:34] + "é", false},
		{"braces", "{" + sampleNoteID + "}", false},
		{"a urn prefix", "urn:uuid:" + sampleNoteID, false},
		{"a base64url device id", idA, false},
	}
	for _, c := range cases {
		if got := ValidNoteID(c.id); got != c.want {
			t.Errorf("%s: ValidNoteID(%q) = %v, want %v", c.name, c.id, got, c.want)
		}
	}
}

func TestCheckNoteBoxIsOneJSONObjectAndNoSizeBound(t *testing.T) {
	good := map[string]string{
		"an empty object":   `{}`,
		"a box":             sampleBox,
		"a nested object":   `{"a":{"b":[1,2,{"c":null}]}}`,
		"above MaxRecord":   `{"x":"` + strings.Repeat("a", MaxRecord*2) + `"}`,
		"near the frame":    `{"x":"` + strings.Repeat("a", MaxFrameAuth-100) + `"}`,
		"a string with {":   `{"x":"{"}`,
		"unusual but valid": `{"a":1.5e3,"b":"\u00e9"}`,
	}
	for name, raw := range good {
		if err := CheckNoteBox(json.RawMessage(raw)); err != nil {
			t.Errorf("%s: %v", name, err)
		}
	}
	bad := map[string]json.RawMessage{
		"nil":                 nil,
		"empty":               json.RawMessage(``),
		"an array":            json.RawMessage(`[]`),
		"a string":            json.RawMessage(`"x"`),
		"null":                json.RawMessage(`null`),
		"a number":            json.RawMessage(`1`),
		"leading space":       json.RawMessage(` {}`),
		"unclosed":            json.RawMessage(`{`),
		"a member without it": json.RawMessage(`{"a":}`),
		"two objects":         json.RawMessage(`{} {}`),
		"trailing bytes":      json.RawMessage(`{"a":1}x`),
		"a bare key":          json.RawMessage(`{"a"}`),
	}
	for name, raw := range bad {
		if err := CheckNoteBox(raw); err == nil {
			t.Errorf("%s: accepted %q", name, raw)
		}
	}
}

func TestNotesListNeedsASinceOfZeroOrMore(t *testing.T) {
	head := `{"schemaVersion":2,"type":"notes_list"`
	cases := []struct {
		name, in string
		ok       bool
	}{
		{"zero", head + `,"since":0}`, true},
		{"a revision", head + `,"since":12}`, true},
		{"full continuation", head + `,"since":12,"full":true}`, true},
		{"invalid full", head + `,"since":12,"full":"true"}`, false},
		{"the widest", head + `,"since":9223372036854775807}`, true},
		{"absent closes the stream", head + `}`, false},
		{"null is absent", head + `,"since":null}`, false},
		{"negative", head + `,"since":-1}`, false},
		{"a string", head + `,"since":"1"}`, false},
		{"a fraction", head + `,"since":1.5}`, false},
		{"beyond int64", head + `,"since":9223372036854775808}`, false},
	}
	for _, c := range cases {
		err := decodeErr([]byte(c.in))
		if c.ok != (err == nil) || (err != nil && !errors.Is(err, ErrMalformed)) {
			t.Errorf("%s: got %v, want ok=%v", c.name, err, c.ok)
		}
	}
	e, err := Decode([]byte(head + `,"since":0}`))
	if err != nil || e.(*NotesList).Since == nil || *e.(*NotesList).Since != 0 {
		t.Fatalf("a since of 0 must be present, not absent: %v %+v", err, e)
	}
}

func TestNotesPutValidate(t *testing.T) {
	id := `"noteId":"` + sampleNoteID + `"`
	huge := `{"x":"` + strings.Repeat("a", MaxRecord*2) + `"}`
	cases := []struct {
		name, members string
		ok            bool
	}{
		{"a create", id + `,"baseRevision":0,"sealed":` + sampleBox, true},
		{"an edit", id + `,"baseRevision":7,"sealed":` + sampleBox, true},
		{"a box above MaxRecord", id + `,"baseRevision":7,"sealed":` + huge, true},
		{"an empty box object", id + `,"baseRevision":0,"sealed":{}`, true},
		{"deleted false is a live note", id + `,"baseRevision":0,"deleted":false,"sealed":` + sampleBox, true},
		{"a delete", id + `,"baseRevision":8,"deleted":true`, true},
		{"a delete of a note never stored", id + `,"baseRevision":0,"deleted":true`, true},
		{"a delete with a box is tolerated", id + `,"baseRevision":8,"deleted":true,"sealed":` + sampleBox, true},
		{"a delete with a null box", id + `,"baseRevision":8,"deleted":true,"sealed":null`, true},
		{"a delete with a box that is no object", id + `,"baseRevision":8,"deleted":true,"sealed":"x"`, true},

		{"no noteId", `"baseRevision":0,"sealed":` + sampleBox, false},
		{"an upper-case noteId", `"noteId":"` + strings.ToUpper(sampleNoteID) + `","baseRevision":0,"sealed":` + sampleBox, false},
		{"a short noteId", `"noteId":"abc","baseRevision":0,"sealed":` + sampleBox, false},
		{"a device-id-shaped noteId", `"noteId":"` + idA + `","baseRevision":0,"sealed":` + sampleBox, false},
		{"no baseRevision", id + `,"sealed":` + sampleBox, false},
		{"a null baseRevision", id + `,"baseRevision":null,"sealed":` + sampleBox, false},
		{"a negative baseRevision", id + `,"baseRevision":-1,"sealed":` + sampleBox, false},
		{"a string baseRevision", id + `,"baseRevision":"0","sealed":` + sampleBox, false},
		{"a delete with no baseRevision", id + `,"deleted":true`, false},
		{"a live put with no box", id + `,"baseRevision":0`, false},
		{"a live put with a null box", id + `,"baseRevision":0,"sealed":null`, false},
		{"a live put with a string box", id + `,"baseRevision":0,"sealed":"x"`, false},
		{"a live put with an array box", id + `,"baseRevision":0,"sealed":[]`, false},
		{"deleted as a string", id + `,"baseRevision":0,"deleted":"true","sealed":` + sampleBox, false},
	}
	for _, c := range cases {
		err := decodeErr(notesPutFrame(c.members))
		if c.ok != (err == nil) || (err != nil && !errors.Is(err, ErrMalformed)) {
			t.Errorf("%s: got %v, want ok=%v", c.name, err, c.ok)
		}
	}
	e, err := Decode(notesPutFrame(id + `,"baseRevision":0,"sealed":` + sampleBox))
	if err != nil {
		t.Fatal(err)
	}
	p := e.(*NotesPut)
	if p.NoteID != sampleNoteID || p.BaseRevision == nil || *p.BaseRevision != 0 || p.Deleted || string(p.Sealed) != sampleBox {
		t.Fatalf("members read wrong: %+v", p)
	}
}

func TestNotesStoredAndChangedValidate(t *testing.T) {
	stored := func(members string) []byte {
		return []byte(`{"schemaVersion":2,"type":"notes_stored"` + members + `}`)
	}
	id := `,"noteId":"` + sampleNoteID + `"`
	for name, c := range map[string]struct {
		in []byte
		ok bool
	}{
		"stored":                    {stored(id + `,"revision":10,"storedAt":"2026-11-14T10:10:00Z"`), true},
		"stored, fractional time":   {stored(id + `,"revision":10,"storedAt":"2026-11-14T10:10:00.5Z"`), true},
		"stored, revision 0":        {stored(id + `,"revision":0,"storedAt":"2026-11-14T10:10:00Z"`), false},
		"stored, no revision":       {stored(id + `,"storedAt":"2026-11-14T10:10:00Z"`), false},
		"stored, negative revision": {stored(id + `,"revision":-2,"storedAt":"2026-11-14T10:10:00Z"`), false},
		"stored, no storedAt":       {stored(id + `,"revision":10`), false},
		"stored, storedAt no time":  {stored(id + `,"revision":10,"storedAt":"yesterday"`), false},
		"stored, bad noteId":        {stored(`,"noteId":"x","revision":10,"storedAt":"2026-11-14T10:10:00Z"`), false},
		"stored, no noteId":         {stored(`,"revision":10,"storedAt":"2026-11-14T10:10:00Z"`), false},
		"changed":                   {[]byte(`{"schemaVersion":2,"type":"notes_changed","notesRevision":10}`), true},
		"changed, revision 1":       {[]byte(`{"schemaVersion":2,"type":"notes_changed","notesRevision":1}`), true},
		"changed, revision 0":       {[]byte(`{"schemaVersion":2,"type":"notes_changed","notesRevision":0}`), false},
		"changed, absent":           {[]byte(`{"schemaVersion":2,"type":"notes_changed"}`), false},
		"changed, negative":         {[]byte(`{"schemaVersion":2,"type":"notes_changed","notesRevision":-3}`), false},
	} {
		err := decodeErr(c.in)
		if c.ok != (err == nil) || (err != nil && !errors.Is(err, ErrMalformed)) {
			t.Errorf("%s: got %v, want ok=%v", name, err, c.ok)
		}
	}
}

func TestNotesChangesValidate(t *testing.T) {
	live := func(id string, rev int) string { return noteRec(id, rev, `,"sealed":`+sampleBox) }
	tomb := func(id string, rev int) string { return noteRec(id, rev, `,"deleted":true`) }
	changes := func(counters, records string) []byte {
		return []byte(`{"schemaVersion":2,"type":"notes_changes",` + counters + `,"records":[` + records + `],"more":false,"full":false}`)
	}
	ok := `"notesRevision":9,"notesHorizon":3`
	cases := []struct {
		name string
		in   []byte
		ok   bool
	}{
		{"two records", changes(ok, live(sampleNoteID, 8)+","+tomb(sampleNoteID2, 9)), true},
		{"no record", changes(ok, ``), true},
		{"a live record with an explicit deleted false", changes(ok, noteRec(sampleNoteID, 4, `,"deleted":false,"sealed":`+sampleBox)), true},
		{"records absent", []byte(`{"schemaVersion":2,"type":"notes_changes","notesRevision":0,"notesHorizon":0,"more":false,"full":false}`), true},
		{"a box above MaxRecord", changes(ok, noteRec(sampleNoteID, 5, `,"sealed":{"x":"`+strings.Repeat("a", MaxRecord*2)+`"}`)), true},
		{"a negative notesRevision", changes(`"notesRevision":-1,"notesHorizon":0`, ``), false},
		{"a negative notesHorizon", changes(`"notesRevision":1,"notesHorizon":-1`, ``), false},
		{"a bad noteId", changes(ok, live("nope", 8)), false},
		{"revision 0", changes(ok, live(sampleNoteID, 0)), false},
		{"a record with no time", changes(ok, `{"noteId":"`+sampleNoteID+`","revision":8,"sourceDeviceId":"`+idA+`","sealed":`+sampleBox+`}`), false},
		{"a record with no source device", changes(ok, `{"noteId":"`+sampleNoteID+`","revision":8,"storedAt":"2026-11-14T10:00:00Z","sealed":`+sampleBox+`}`), false},
		{"a live record with no box", changes(ok, noteRec(sampleNoteID, 8, ``)), false},
		{"a live record with a string box", changes(ok, noteRec(sampleNoteID, 8, `,"sealed":"x"`)), false},
		{"a tombstone with a box", changes(ok, noteRec(sampleNoteID, 8, `,"deleted":true,"sealed":`+sampleBox)), false},
		{"the same revision twice", changes(ok, live(sampleNoteID, 8)+","+live(sampleNoteID2, 8)), false},
		{"descending revisions", changes(ok, live(sampleNoteID, 9)+","+live(sampleNoteID2, 8)), false},
	}
	for _, c := range cases {
		err := decodeErr(c.in)
		if c.ok != (err == nil) || (err != nil && !errors.Is(err, ErrMalformed)) {
			t.Errorf("%s: got %v, want ok=%v", c.name, err, c.ok)
		}
	}
}

func TestARefusedValidatesItsCurrentRecordWhenItHasOne(t *testing.T) {
	refused := func(tail string) []byte {
		return []byte(`{"schemaVersion":2,"type":"refused","reason":"note-conflict"` + tail + `}`)
	}
	for name, c := range map[string]struct {
		in []byte
		ok bool
	}{
		"a conflict with the stored record": {refused(`,"current":` + noteRec(sampleNoteID, 9, `,"sealed":`+sampleBox)), true},
		"a conflict with a tombstone":       {refused(`,"current":` + noteRec(sampleNoteID, 9, `,"deleted":true`)), true},
		"a conflict with no record":         {refused(``), true},
		"a null current":                    {refused(`,"current":null`), true},
		"a record of revision 0":            {refused(`,"current":` + noteRec(sampleNoteID, 0, `,"sealed":`+sampleBox)), false},
		"a record with a bad id":            {refused(`,"current":` + noteRec("nope", 9, `,"sealed":`+sampleBox)), false},
		"a live record with no box":         {refused(`,"current":` + noteRec(sampleNoteID, 9, ``)), false},
		"a current that is no object":       {refused(`,"current":"x"`), false},
	} {
		err := decodeErr(c.in)
		if c.ok != (err == nil) || (err != nil && !errors.Is(err, ErrMalformed)) {
			t.Errorf("%s: got %v, want ok=%v", name, err, c.ok)
		}
	}
	e, err := Decode(refused(`,"current":` + noteRec(sampleNoteID, 9, `,"sealed":`+sampleBox)))
	if err != nil {
		t.Fatal(err)
	}
	cur := e.(*Refused).Current
	if cur == nil || cur.NoteID != sampleNoteID || cur.Revision != 9 || cur.SourceDeviceID != idA || cur.Deleted || string(cur.Sealed) != sampleBox {
		t.Fatalf("current read wrong: %+v", cur)
	}
}

// The bytes of a refusal that predates 1.13 do not move, and the new member appears only when it is set.
func TestRefusedCarriesCurrentOnlyWhenSet(t *testing.T) {
	for _, r := range AllReasons {
		body, err := Encode(Refuse(r))
		if want := `{"schemaVersion":2,"type":"refused","reason":"` + string(r) + `"}`; err != nil || string(body) != want {
			t.Errorf("%s: %s %v, want %s", r, body, err, want)
		}
	}
	cur := NoteRecord{NoteID: sampleNoteID, Revision: 9, StoredAt: "2026-11-14T10:05:00Z", SourceDeviceID: idB, Deleted: true}
	body, err := Encode(noteConflict(cur))
	want := `{"schemaVersion":2,"type":"refused","reason":"note-conflict","current":{"noteId":"` + sampleNoteID +
		`","revision":9,"storedAt":"2026-11-14T10:05:00Z","sourceDeviceId":"` + idB + `","deleted":true}}`
	if err != nil || string(body) != want {
		t.Fatalf("%s %v\nwant %s", body, err, want)
	}
}

func TestNotesFramesEncodeTheirOptionalMembersOnlyWhenSet(t *testing.T) {
	cases := []struct {
		name string
		e    Envelope
		want string
	}{
		{"an empty changes answer lists no record and still says more and full",
			&NotesChanges{},
			`{"schemaVersion":2,"type":"notes_changes","notesRevision":0,"notesHorizon":0,"records":[],"more":false,"full":false}`},
		{"a since of zero is written",
			&NotesList{Since: int64p(0)},
			`{"schemaVersion":2,"type":"notes_list","since":0}`},
		{"a live put has no deleted member",
			&NotesPut{NoteID: sampleNoteID, BaseRevision: int64p(0), Sealed: json.RawMessage(`{ "v": 1 }`)},
			`{"schemaVersion":2,"type":"notes_put","noteId":"` + sampleNoteID + `","baseRevision":0,"sealed":{"v":1}}`},
		{"a delete has no box member",
			&NotesPut{NoteID: sampleNoteID, BaseRevision: int64p(8), Deleted: true},
			`{"schemaVersion":2,"type":"notes_put","noteId":"` + sampleNoteID + `","baseRevision":8,"deleted":true}`},
		{"a stored answer",
			&NotesStored{NoteID: sampleNoteID, Revision: 10, StoredAt: "2026-11-14T10:10:00Z"},
			`{"schemaVersion":2,"type":"notes_stored","noteId":"` + sampleNoteID + `","revision":10,"storedAt":"2026-11-14T10:10:00Z"}`},
		{"a push",
			&NotesChanged{NotesRevision: 10},
			`{"schemaVersion":2,"type":"notes_changed","notesRevision":10}`},
	}
	for _, c := range cases {
		body, err := Encode(c.e)
		if err != nil || string(body) != c.want {
			t.Errorf("%s:\n got  %s (%v)\n want %s", c.name, body, err, c.want)
		}
	}
}

// A box is carried as the bytes it came with: a put that is read and a record that is written back keep it.
func TestANotesBoxSurvivesDecodeAndEncodeUnchanged(t *testing.T) {
	frame := notesPutFrame(`"noteId":"` + sampleNoteID + `","baseRevision":3,"sealed":` + sampleBox)
	e, err := Decode(frame)
	if err != nil {
		t.Fatal(err)
	}
	rec := NoteRecord{NoteID: sampleNoteID, Revision: 4, StoredAt: "2026-11-14T10:10:00Z", SourceDeviceID: idA, Sealed: e.(*NotesPut).Sealed}
	body, err := Encode(&NotesChanges{NotesRevision: 4, Records: []NoteRecord{rec}})
	if err != nil || !bytes.Contains(body, []byte(`"sealed":`+sampleBox)) {
		t.Fatalf("the box changed on its way through: %s %v", body, err)
	}
}

func TestNoteFitsFrameIsExactAtTheFrameLimit(t *testing.T) {
	rec := func(fill int) NoteRecord {
		return NoteRecord{NoteID: sampleNoteID, Revision: 1 << 40, StoredAt: "2026-11-14T10:10:00Z", SourceDeviceID: idA,
			Sealed: json.RawMessage(`{"x":"` + strings.Repeat("a", fill) + `"}`)}
	}
	if !NoteFitsFrame(rec(0)) || !NoteFitsFrame(rec(MaxNotePayload)) {
		t.Fatal("a small box and a box of the payload limit fit")
	}
	// the notes_changes frame is the wider of the two: counters at their widest, the record alone
	probe, err := Encode(&NotesChanges{NotesRevision: math.MaxInt64, NotesHorizon: math.MaxInt64, Records: []NoteRecord{rec(0)}})
	if err != nil {
		t.Fatal(err)
	}
	fill := MaxFrameAuth - len(probe)
	if !NoteFitsFrame(rec(fill)) {
		t.Errorf("a record that makes a frame of exactly %d bytes must fit", MaxFrameAuth)
	}
	if NoteFitsFrame(rec(fill + 1)) {
		t.Errorf("a record that makes a frame of %d bytes must not fit", MaxFrameAuth+1)
	}
	if NoteFitsFrame(rec(MaxFrameAuth)) {
		t.Error("a box as long as the frame must not fit")
	}
}
