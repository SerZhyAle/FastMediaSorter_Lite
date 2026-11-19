package rendezvous_test

import (
	"encoding/json"
	"math"
	"os"
	"path/filepath"
	"slices"
	"strconv"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/notes"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket L0321: the notes of an account on the exchange server, DEVICE-EXCHANGE 1.13 (items FG to FM), over TLS on
// loopback against the real engine and the real store. One small test per behaviour. A test never prints a box: a
// failure names a type, a reason, a revision or a length.

const (
	ntA = "11111111-1111-4111-8111-111111111111"
	ntB = "22222222-2222-4222-8222-222222222222"
	ntC = "33333333-3333-4333-8333-333333333333"
	ntD = "44444444-4444-4444-8444-444444444444"
	ntE = "55555555-5555-4555-8555-555555555555"
)

// notesOpts is a server with the notes store and the `sealed` feature on, the configuration a notes client talks to.
func notesOpts(logins ...string) testkit.Options {
	o := looseOpts(logins...)
	o.Sealed = true
	o.Notes = true
	return o
}

// ntBox is a box in the form a client writes. The tag rides in its ciphertext member so that a test can look for it
// in a log; the server reads none of it.
func ntBox(tag string) json.RawMessage {
	return json.RawMessage(`{"v":1,"from":"dev-phone","n":"AAAAAAAAAAAAAAAA","ct":"` + tag + `","keys":[]}`)
}

// ntSized is a box of exactly n bytes (n of 9 and more), one JSON object, starting its ciphertext with tag.
func ntSized(n int, tag string) json.RawMessage {
	return json.RawMessage(`{"ct":"` + tag + strings.Repeat("a", n-9-len(tag)) + `"}`)
}

// ntBoxBound is the longest box, in bytes, that a put by the device can carry and still be kept: the point where
// the record, with its counters at their widest, stops fitting one frame (item T and Assumed 2).
func ntBoxBound(noteID, deviceID string) int {
	fits := func(n int) bool {
		return wire.NoteFitsFrame(wire.NoteRecord{NoteID: noteID, Revision: math.MaxInt64, StoredAt: "9999-12-31T23:59:59Z",
			SourceDeviceID: deviceID, Sealed: ntSized(n, "")})
	}
	lo, hi := 9, wire.MaxFrameAuth // the box of 9 bytes fits, a box of the frame's size cannot
	for hi-lo > 1 {
		mid := (lo + hi) / 2
		if fits(mid) {
			lo = mid
		} else {
			hi = mid
		}
	}
	return lo
}

func ntRev(n int64) *int64 { return &n }

// ntDescribe names an answer without a member of it: a type, or the reason of a refusal.
func ntDescribe(e wire.Envelope) string {
	if e == nil {
		return "nothing"
	}
	if r, ok := e.(*wire.Refused); ok {
		return "refused " + r.Reason
	}
	return e.TypeName()
}

// ntDeviceRoles enrolls a device of login that declares the roles and puts it online on a control stream.
func ntDeviceRoles(t *testing.T, env *testkit.Env, login string, roles []string) (*testkit.Device, *testkit.Control) {
	t.Helper()
	d := env.NewDevice(login)
	d.Roles = roles
	mustEnroll(t, d)
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	return d, ctl
}

// ntDevice is a device that takes part in the notes sync: it declares the role `notes`.
func ntDevice(t *testing.T, env *testkit.Env, login string) (*testkit.Device, *testkit.Control) {
	t.Helper()
	return ntDeviceRoles(t, env, login, []string{wire.RoleNotes})
}

func ntPut(t *testing.T, ctl *testkit.Control, id string, base int64, box json.RawMessage) wire.Envelope {
	t.Helper()
	ans, err := ctl.Request(&wire.NotesPut{NoteID: id, BaseRevision: ntRev(base), Sealed: box}, 5*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	return ans
}

func ntDelete(t *testing.T, ctl *testkit.Control, id string, base int64) wire.Envelope {
	t.Helper()
	ans, err := ctl.Request(&wire.NotesPut{NoteID: id, BaseRevision: ntRev(base), Deleted: true}, 5*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	return ans
}

func ntList(t *testing.T, ctl *testkit.Control, since int64) *wire.NotesChanges {
	t.Helper()
	ans, err := ctl.Request(&wire.NotesList{Since: ntRev(since)}, 5*time.Second, wire.TypeNotesChanges, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	c, ok := ans.(*wire.NotesChanges)
	if !ok {
		t.Fatalf("notes_list since %d: want notes_changes, got %s", since, ntDescribe(ans))
	}
	return c
}

func ntStored(t *testing.T, ans wire.Envelope) *wire.NotesStored {
	t.Helper()
	s, ok := ans.(*wire.NotesStored)
	if !ok {
		t.Fatalf("want notes_stored, got %s", ntDescribe(ans))
	}
	return s
}

func ntRefused(t *testing.T, ans wire.Envelope, want wire.Reason) *wire.Refused {
	t.Helper()
	r, ok := ans.(*wire.Refused)
	if !ok || r.Reason != string(want) {
		t.Fatalf("want refused %s, got %s", want, ntDescribe(ans))
	}
	return r
}

func ntConflict(t *testing.T, ans wire.Envelope) *wire.Refused {
	t.Helper()
	return ntRefused(t, ans, wire.ReasonNoteConflict)
}

// ntRevs is the revisions of the records of an answer, in the order they came.
func ntRevs(c *wire.NotesChanges) []int64 {
	out := make([]int64, 0, len(c.Records))
	for _, r := range c.Records {
		out = append(out, r.Revision)
	}
	return out
}

// ntCheckCurrent checks the `current` record of a `note-conflict`: nil box means a tombstone.
func ntCheckCurrent(t *testing.T, what string, cur *wire.NoteRecord, id string, revision int64, deleted bool, box json.RawMessage) {
	t.Helper()
	if cur == nil {
		t.Fatalf("%s: the refusal carries no current record", what)
	}
	if cur.NoteID != id || cur.Revision != revision || cur.Deleted != deleted || string(cur.Sealed) != string(box) {
		t.Fatalf("%s: current is note %s, revision %d, deleted %v, a box of %d bytes", what, cur.NoteID, cur.Revision, cur.Deleted, len(cur.Sealed))
	}
}

func ntRaw(kind, fields string) string {
	return `{"schemaVersion":2,"type":"` + kind + `"` + fields + `}`
}

// ntExpectClosed waits for the server to close the stream and checks that it said nothing before.
func ntExpectClosed(t *testing.T, ctl *testkit.Control, what string) {
	t.Helper()
	select {
	case <-ctl.Closed():
	case <-time.After(3 * time.Second):
		t.Fatalf("%s: the stream must be closed", what)
	}
	if ans, err := ctl.Await(0, wire.TypeRefused, wire.TypeNotesStored, wire.TypeNotesChanges); err == nil {
		t.Fatalf("%s: a malformed frame is closed with no answer, got %s", what, ntDescribe(ans))
	}
}

func ntLogCount(env *testkit.Env, event string) int {
	n := 0
	for _, l := range env.Log.Lines() {
		if strings.Contains(l, "\t"+event+"\t") {
			n++
		}
	}
	return n
}

func ntPushLines(env *testkit.Env) []string {
	var out []string
	for _, l := range env.Log.Lines() {
		if strings.Contains(l, "notes-changed-pushed") {
			out = append(out, l)
		}
	}
	return out
}

// ntAwaitPushed waits until the engine has logged n pushes: the barrier a test needs before it can say that a
// device was not told. The line is written after the push was queued.
func ntAwaitPushed(t *testing.T, env *testkit.Env, n int) []string {
	t.Helper()
	var lines []string
	eventually(t, "the notes-changed-pushed log line", func() bool {
		lines = ntPushLines(env)
		return len(lines) >= n
	})
	return lines
}

// ntAgeTombstones moves the injected clock past the age of item FG and runs the sweep, which purges what is old.
// The streams of the account must be closed first: a clock that jumps expires every keepalive window.
func ntAgeTombstones(env *testkit.Env, clock *fakeClock) {
	clock.Add((notes.TombstoneDays + 1) * 24 * time.Hour)
	env.Engine.Sweep(clock.Now())
}

// --- the feature ---

// TestNotesFeatureIsListedOnlyWithTheStoreAndSealed is item FI: `notes` follows `sealed` and `settings` in the
// feature list, and is there only when the server holds the store and lists `sealed`.
func TestNotesFeatureIsListedOnlyWithTheStoreAndSealed(t *testing.T) {
	for _, c := range []struct {
		name string
		set  func(*testkit.Options)
		want string
	}{
		{"the store and sealed", func(o *testkit.Options) {}, "directory,pairing,clipboard,sealed,notes"},
		{"the store, sealed and settings", func(o *testkit.Options) { o.Settings = true }, "directory,pairing,clipboard,sealed,settings,notes"},
		{"sealed without the store", func(o *testkit.Options) { o.Notes = false }, "directory,pairing,clipboard,sealed"},
		{"the store without sealed", func(o *testkit.Options) { o.Sealed = false }, "directory,pairing,clipboard"},
	} {
		t.Run(c.name, func(t *testing.T) {
			o := notesOpts("alice")
			c.set(&o)
			env := testkit.StartServer(t, o)
			d := env.NewDevice("alice")
			if got := strings.Join(mustEnroll(t, d).Features, ","); got != c.want {
				t.Fatalf("enrolled lists %q, want %q", got, c.want)
			}
			conn, r, err := d.Hello()
			if err != nil || conn == nil {
				t.Fatalf("hello: %v", err)
			}
			defer conn.Close()
			w, ok := r.(*wire.Welcome)
			if !ok {
				t.Fatalf("hello: want welcome, got %s", ntDescribe(r))
			}
			if got := strings.Join(w.Features, ","); got != c.want {
				t.Fatalf("welcome lists %q, want %q", got, c.want)
			}
		})
	}
}

// TestNotesFramesAreIgnoredWhenTheServerDoesNotListNotes is item FI and 7.1: a server that does not list the
// feature treats its frames like an unknown type - no answer, the stream stays open, nothing is stored.
func TestNotesFramesAreIgnoredWhenTheServerDoesNotListNotes(t *testing.T) {
	for _, c := range []struct {
		name string
		set  func(*testkit.Options)
	}{
		{"sealed without the store", func(o *testkit.Options) { o.Notes = false }},
		{"the store without sealed", func(o *testkit.Options) { o.Sealed = false }},
	} {
		t.Run(c.name, func(t *testing.T) {
			o := notesOpts("alice")
			c.set(&o)
			env := testkit.StartServer(t, o)
			_, ctl := ntDevice(t, env, "alice")
			for _, f := range []wire.Envelope{
				&wire.NotesList{Since: ntRev(0)},
				&wire.NotesPut{NoteID: ntA, BaseRevision: ntRev(0), Sealed: ntBox("ignored")},
			} {
				if ans, err := ctl.Request(f, 400*time.Millisecond, wire.TypeNotesChanges, wire.TypeNotesStored, wire.TypeRefused); err == nil {
					t.Fatalf("a server that does not list notes answered %s", ntDescribe(ans))
				}
			}
			if _, err := ctl.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
				t.Fatalf("the ignored frames must leave the stream open: %v", err)
			}
			if env.Notes != nil {
				if _, _, n := env.Notes.State("alice"); n != 0 {
					t.Fatalf("an ignored put stored %d records", n)
				}
			}
			if _, err := os.Stat(filepath.Join(env.DataDir, notes.File)); !os.IsNotExist(err) {
				t.Fatalf("an ignored put wrote the store file: %v", err)
			}
		})
	}
}

// --- create, edit, delete ---

// TestANoteCreatedOnOneDeviceIsReadByAnotherDevice is a create at `baseRevision` 0 read by the account's other
// device: the record as stored, and nothing of it for another account.
func TestANoteCreatedOnOneDeviceIsReadByAnotherDevice(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice", "bob"))
	file := filepath.Join(env.DataDir, notes.File)
	if _, err := os.Stat(file); !os.IsNotExist(err) {
		t.Fatalf("the store file exists before the first put: %v", err)
	}
	d1, c1 := ntDevice(t, env, "alice")
	_, c2 := ntDevice(t, env, "alice")

	box := ntBox("created")
	st := ntStored(t, ntPut(t, c1, ntA, 0, box))
	if st.NoteID != ntA || st.Revision != 1 {
		t.Fatalf("stored: note %s, revision %d", st.NoteID, st.Revision)
	}
	if _, err := wire.ParseTime(st.StoredAt); err != nil {
		t.Fatalf("storedAt is not a time: %v", err)
	}
	if _, err := os.Stat(file); err != nil {
		t.Fatalf("the store file must exist from the first accepted put: %v", err)
	}

	got := ntList(t, c2, 0)
	if got.NotesRevision != 1 || got.NotesHorizon != 0 || got.More || got.Full || len(got.Records) != 1 {
		t.Fatalf("list: revision %d, horizon %d, more %v, full %v, %d records", got.NotesRevision, got.NotesHorizon, got.More, got.Full, len(got.Records))
	}
	r := got.Records[0]
	if r.NoteID != ntA || r.Revision != 1 || r.StoredAt != st.StoredAt || r.SourceDeviceID != d1.ID || r.Deleted || string(r.Sealed) != string(box) {
		t.Fatalf("the record read: note %s, revision %d, source %s, deleted %v, a box of %d bytes", r.NoteID, r.Revision, r.SourceDeviceID, r.Deleted, len(r.Sealed))
	}

	// the notes of an account are its own
	_, cb := ntDevice(t, env, "bob")
	if b := ntList(t, cb, 0); b.NotesRevision != 0 || len(b.Records) != 0 {
		t.Fatalf("another account's list: revision %d, %d records", b.NotesRevision, len(b.Records))
	}

	// a box with white space in it is kept in the compact form (the form of the file and of every answer)
	spaced := ntRaw(wire.TypeNotesPut, `,"noteId":"`+ntB+`","baseRevision":0,"sealed":{ "ct": "x",  "n": "y" }`)
	if err := c1.Conn.SendRaw(testkit.RawFrame(spaced)); err != nil {
		t.Fatal(err)
	}
	ans, err := c1.Await(3*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	if ntStored(t, ans).Revision != 2 {
		t.Fatal("the second note must be revision 2 of the account")
	}
	two := ntList(t, c2, 1)
	if len(two.Records) != 1 {
		t.Fatalf("since 1: %d records", len(two.Records))
	}
	if string(two.Records[0].Sealed) != `{"ct":"x","n":"y"}` {
		t.Fatalf("the spaced box came back as a box of %d bytes, not in the compact form", len(two.Records[0].Sealed))
	}
}

// TestAnEditComesBackWithItsNewRevisionAndItsSourceDevice is an edit at the stored revision made on the other
// device: one record per note, the newest state, above the revision the first device has seen.
func TestAnEditComesBackWithItsNewRevisionAndItsSourceDevice(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	d1, c1 := ntDevice(t, env, "alice")
	d2, c2 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("v1")))
	ntStored(t, ntPut(t, c1, ntB, 0, ntBox("other")))
	if st := ntStored(t, ntPut(t, c2, ntA, 1, ntBox("v2"))); st.Revision != 3 {
		t.Fatalf("an edit is the next revision of the account: got %d", st.Revision)
	}

	got := ntList(t, c1, 2)
	if got.NotesRevision != 3 || !slices.Equal(ntRevs(got), []int64{3}) {
		t.Fatalf("since 2: counter %d, revisions %v", got.NotesRevision, ntRevs(got))
	}
	if r := got.Records[0]; r.NoteID != ntA || r.SourceDeviceID != d2.ID || string(r.Sealed) != string(ntBox("v2")) {
		t.Fatalf("the edit: note %s, source %s, a box of %d bytes", r.NoteID, r.SourceDeviceID, len(r.Sealed))
	}

	all := ntList(t, c1, 0)
	if !slices.Equal(ntRevs(all), []int64{2, 3}) || all.Records[0].NoteID != ntB || all.Records[0].SourceDeviceID != d1.ID {
		t.Fatalf("since 0 lists one record per note, ascending by revision: %v", ntRevs(all))
	}
	if none := ntList(t, c1, 3); len(none.Records) != 0 || none.Full || none.More {
		t.Fatalf("since the counter: %d records, full %v, more %v", len(none.Records), none.Full, none.More)
	}
}

// TestADeleteStoresATombstoneAndDropsTheBoxThatCameAlong is item FG: a delete is a record with `deleted` and no
// box, a box that came with it is dropped (and one that is no box at all is not looked at), and a delete of a note
// the server never held, at `baseRevision` 0, is a tombstone too.
func TestADeleteStoresATombstoneAndDropsTheBoxThatCameAlong(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	_, c1 := ntDevice(t, env, "alice")
	_, c2 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("gone")))
	ntStored(t, ntPut(t, c1, ntB, 0, ntBox("gone-too")))

	withBox, err := c1.Request(&wire.NotesPut{NoteID: ntA, BaseRevision: ntRev(1), Deleted: true, Sealed: ntBox("dropped")},
		5*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	if ntStored(t, withBox).Revision != 3 {
		t.Fatal("a delete is a revision of the account")
	}
	junk := ntRaw(wire.TypeNotesPut, `,"noteId":"`+ntB+`","baseRevision":2,"deleted":true,"sealed":"not a box"`)
	if err := c1.Conn.SendRaw(testkit.RawFrame(junk)); err != nil {
		t.Fatal(err)
	}
	ans, err := c1.Await(3*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	if ntStored(t, ans).Revision != 4 {
		t.Fatal("a delete that carries a junk box is a revision of the account")
	}
	if st := ntStored(t, ntDelete(t, c1, ntC, 0)); st.Revision != 5 {
		t.Fatalf("a delete of a note never held: revision %d", st.Revision)
	}

	got := ntList(t, c2, 2)
	if !slices.Equal(ntRevs(got), []int64{3, 4, 5}) {
		t.Fatalf("tombstones: revisions %v", ntRevs(got))
	}
	for i, id := range []string{ntA, ntB, ntC} {
		if r := got.Records[i]; r.NoteID != id || !r.Deleted || len(r.Sealed) != 0 {
			t.Fatalf("record %d: note %s, deleted %v, a box of %d bytes", i, r.NoteID, r.Deleted, len(r.Sealed))
		}
	}
}

// --- the conflict rule ---

// TestAPutIsRefusedByEveryBranchOfTheConflictRule is item FJ and Assumed 3: accepted only at the stored
// revision, or at 0 for an id the store holds none of; every other base is `note-conflict` that carries the
// stored record (a tombstone included) or nothing when the store holds none, and changes nothing.
func TestAPutIsRefusedByEveryBranchOfTheConflictRule(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	file := filepath.Join(env.DataDir, notes.File)
	_, c1 := ntDevice(t, env, "alice")

	// a base above 0 for an id the store never held: gone from the server, no current, no write
	if r := ntConflict(t, ntPut(t, c1, ntA, 5, ntBox("never"))); r.Current != nil {
		t.Fatal("a conflict on a note the store does not hold carries a current record")
	}
	if r := ntConflict(t, ntDelete(t, c1, ntA, 5)); r.Current != nil {
		t.Fatal("a delete conflict on a note the store does not hold carries a current record")
	}
	if _, err := os.Stat(file); !os.IsNotExist(err) {
		t.Fatalf("a refused put wrote the store file: %v", err)
	}

	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("v1")))
	// base 0 for a note the store holds: refused with the stored record, box included
	r := ntConflict(t, ntPut(t, c1, ntA, 0, ntBox("again")))
	ntCheckCurrent(t, "base 0 over a live note", r.Current, ntA, 1, false, ntBox("v1"))
	ntStored(t, ntPut(t, c1, ntA, 1, ntBox("v2")))
	// a base below the stored revision
	r = ntConflict(t, ntPut(t, c1, ntA, 1, ntBox("v3")))
	ntCheckCurrent(t, "a stale base", r.Current, ntA, 2, false, ntBox("v2"))
	// a base above the stored revision
	r = ntConflict(t, ntPut(t, c1, ntA, 9, ntBox("v3")))
	ntCheckCurrent(t, "a base above the stored revision", r.Current, ntA, 2, false, ntBox("v2"))
	// a delete at a stale base
	r = ntConflict(t, ntDelete(t, c1, ntA, 1))
	ntCheckCurrent(t, "a stale delete", r.Current, ntA, 2, false, ntBox("v2"))
	if rev, hz, n := env.Notes.State("alice"); rev != 2 || hz != 0 || n != 1 {
		t.Fatalf("the refusals changed the store: counter %d, horizon %d, %d records", rev, hz, n)
	}

	// the tombstone is a record: base 0 is refused with it, its own revision is accepted
	ntStored(t, ntDelete(t, c1, ntA, 2))
	r = ntConflict(t, ntPut(t, c1, ntA, 0, ntBox("back")))
	ntCheckCurrent(t, "base 0 over a tombstone", r.Current, ntA, 3, true, nil)
	if st := ntStored(t, ntPut(t, c1, ntA, 3, ntBox("back"))); st.Revision != 4 {
		t.Fatalf("a put at the tombstone's revision: revision %d", st.Revision)
	}
	if got := ntList(t, c1, 3); len(got.Records) != 1 || got.Records[0].Deleted || string(got.Records[0].Sealed) != string(ntBox("back")) {
		t.Fatalf("the note is live again: %d records", len(got.Records))
	}
}

// TestTheChecksOfAPutRunInTheContractsOrder is the order of item FI: a guest, then the frame, then the conflict
// rule, then the count limit - so each earlier check hides every later one.
func TestTheChecksOfAPutRunInTheContractsOrder(t *testing.T) {
	p := testkit.TestParams()
	p.NotesPerAccount = 2
	o := notesOpts("alice")
	o.Params = &p
	env := testkit.StartServer(t, o)
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}

	// a guest is refused before the frame is measured: an oversize put is not-permitted, not a closed stream
	g, gc := ntDevice(t, env, "visitor")
	ntRefused(t, ntPut(t, gc, ntA, 0, ntSized(ntBoxBound(ntA, g.ID)+1, "")), wire.ReasonNotPermitted)
	ans, err := gc.Request(&wire.NotesList{Since: ntRev(0)}, 3*time.Second, wire.TypeNotesChanges, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	ntRefused(t, ans, wire.ReasonNotPermitted)
	if _, err := gc.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatalf("a guest's refusal must leave the stream open: %v", err)
	}

	// the conflict rule comes before the count: a put that would conflict at the cap is a conflict
	a, c1 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("a")))
	ntStored(t, ntPut(t, c1, ntB, 0, ntBox("b")))
	ntConflict(t, ntPut(t, c1, ntC, 4, ntBox("c")))
	ntRefused(t, ntPut(t, c1, ntC, 0, ntBox("c")), wire.ReasonLimitReached)

	// the delivery bound is the last limit (item FX, ticket L0322): an oversize put that would also
	// conflict answers the conflict, and an oversize put that conflicts nothing and breaks no limit is
	// refused capacity, the stream open
	over := ntSized(ntBoxBound(ntD, a.ID)+1, "")
	if _, err := wire.Frame(&wire.NotesPut{NoteID: ntD, BaseRevision: ntRev(4), Sealed: over}); err != nil {
		t.Fatalf("the oversize put must itself fit the frame limit: %v", err)
	}
	ntConflict(t, ntPut(t, c1, ntD, 4, over))
	// a create that is both oversize and over the count answers the count, and an edit that breaks no
	// limit but the delivery bound is refused capacity, the stream open
	ntRefused(t, ntPut(t, c1, ntE, 0, over), wire.ReasonLimitReached)
	ntRefused(t, ntPut(t, c1, ntA, 1, over), wire.ReasonCapacity)
	if _, err := c1.Request(&wire.NotesList{Since: ntRev(0)}, 3*time.Second, wire.TypeNotesChanges, wire.TypeRefused); err != nil {
		t.Fatalf("a capacity refusal must leave the stream open: %v", err)
	}
	if rev, _, n := env.Notes.State("alice"); rev != 2 || n != 2 {
		t.Fatalf("a refused put changed the store: counter %d, %d records", rev, n)
	}
}

// --- the count limit ---

// TestTheCountLimitCountsTombstonesAndRefusesOnlyACreate is item FL with a limit of 3: only a put that creates a
// live record is refused, tombstones count, an edit or a delete never is, and the count is per account.
func TestTheCountLimitCountsTombstonesAndRefusesOnlyACreate(t *testing.T) {
	p := testkit.TestParams()
	p.NotesPerAccount = 3
	o := notesOpts("alice", "bob")
	o.Params = &p
	env := testkit.StartServer(t, o)
	_, c1 := ntDevice(t, env, "alice")

	for i, id := range []string{ntA, ntB, ntC} {
		if st := ntStored(t, ntPut(t, c1, id, 0, ntBox("n"+strconv.Itoa(i)))); st.Revision != int64(i+1) {
			t.Fatalf("note %d: revision %d", i, st.Revision)
		}
	}
	ntRefused(t, ntPut(t, c1, ntD, 0, ntBox("fourth")), wire.ReasonLimitReached)
	if rev, _, n := env.Notes.State("alice"); rev != 3 || n != 3 {
		t.Fatalf("the refusal changed the store: counter %d, %d records", rev, n)
	}

	// an edit and a delete are never refused for the count
	ntStored(t, ntPut(t, c1, ntA, 1, ntBox("edit"))) // revision 4
	ntStored(t, ntDelete(t, c1, ntB, 2))             // revision 5, a tombstone: still three records
	// the tombstone counts: there is still no room for a note that is new
	ntRefused(t, ntPut(t, c1, ntD, 0, ntBox("fourth")), wire.ReasonLimitReached)
	// the tombstone's own note exists, so it comes back to life at its revision
	if st := ntStored(t, ntPut(t, c1, ntB, 5, ntBox("revived"))); st.Revision != 6 {
		t.Fatalf("a revive: revision %d", st.Revision)
	}
	// a delete of a note that is new creates no live record: stored above the cap
	if st := ntStored(t, ntDelete(t, c1, ntD, 0)); st.Revision != 7 {
		t.Fatalf("a delete above the cap: revision %d", st.Revision)
	}
	if _, _, n := env.Notes.State("alice"); n != 4 {
		t.Fatalf("the account holds %d records, want 4", n)
	}
	ntRefused(t, ntPut(t, c1, ntE, 0, ntBox("fifth")), wire.ReasonLimitReached)

	// another account counts for itself
	_, cb := ntDevice(t, env, "bob")
	if st := ntStored(t, ntPut(t, cb, ntA, 0, ntBox("bob"))); st.Revision != 1 {
		t.Fatalf("bob's first note: revision %d", st.Revision)
	}
}

// --- malformed frames and the frame bound ---

// TestAMalformedNotesFrameClosesTheStreamWithNoAnswer is item AN for the two frames of a device: a member out of
// bounds, missing or of the wrong type closes the stream and nothing is stored.
func TestAMalformedNotesFrameClosesTheStreamWithNoAnswer(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	id := `"noteId":"` + ntA + `"`
	for _, c := range []struct{ what, body string }{
		{"an upper-case note id", ntRaw(wire.TypeNotesPut, `,"noteId":"`+strings.ToUpper("abcdefab-abcd-4abc-8abc-abcdefabcdef")+`","baseRevision":0,"sealed":{"ct":"x"}`)},
		{"a note id that is not a UUID", ntRaw(wire.TypeNotesPut, `,"noteId":"note-1","baseRevision":0,"sealed":{"ct":"x"}`)},
		{"no note id", ntRaw(wire.TypeNotesPut, `,"baseRevision":0,"sealed":{"ct":"x"}`)},
		{"no baseRevision", ntRaw(wire.TypeNotesPut, `,`+id+`,"sealed":{"ct":"x"}`)},
		{"a negative baseRevision", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":-1,"sealed":{"ct":"x"}`)},
		{"a baseRevision that is a string", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":"0","sealed":{"ct":"x"}`)},
		{"a live put with no box", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0`)},
		{"a box that is a string", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0,"sealed":"text"`)},
		{"a box that is an array", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0,"sealed":[1]`)},
		{"a list with no since", ntRaw(wire.TypeNotesList, ``)},
		{"a negative since", ntRaw(wire.TypeNotesList, `,"since":-1`)},
		{"a since that is a string", ntRaw(wire.TypeNotesList, `,"since":"0"`)},
		// the members of Assumed 4 that a lenient decoder would let through: a letter that is not hex, a hyphen one
		// place off, a null or a number for the box, a flag that is a string, a counter that is not a whole number
		{"a note id with a letter that is not hex", ntRaw(wire.TypeNotesPut, `,"noteId":"abcdefab-abcd-4abc-8abc-abcdefabcdeg","baseRevision":0,"sealed":{"ct":"x"}`)},
		{"a note id with a hyphen in the wrong place", ntRaw(wire.TypeNotesPut, `,"noteId":"abcdefa-babcd-4abc-8abc-abcdefabcdef","baseRevision":0,"sealed":{"ct":"x"}`)},
		{"a live put with a null box", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0,"sealed":null`)},
		{"a box that is a number", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0,"sealed":5`)},
		{"a deleted that is a string", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":0,"deleted":"yes","sealed":{"ct":"x"}`)},
		{"a baseRevision that is not a whole number", ntRaw(wire.TypeNotesPut, `,`+id+`,"baseRevision":1.5,"sealed":{"ct":"x"}`)},
		{"a since that is not a whole number", ntRaw(wire.TypeNotesList, `,"since":1.5`)},
	} {
		ctl, err := d.OnlineQuietly()
		if err != nil {
			t.Fatal(err)
		}
		if err := ctl.Conn.SendRaw(testkit.RawFrame(c.body)); err != nil {
			t.Fatal(err)
		}
		ntExpectClosed(t, ctl, c.what)
	}
	if rev, _, n := env.Notes.State("alice"); rev != 0 || n != 0 {
		t.Fatalf("a malformed frame changed the store: counter %d, %d records", rev, n)
	}
}

// TestAGuestsMalformedNotesFrameClosesTheStreamAndIsNotRefused is the first consequence of Assumed 17: the members
// are checked when the frame is decoded, before the engine looks at the kind of the account, so a guest that sends a
// malformed put has its stream closed with no answer instead of being told `not-permitted`.
func TestAGuestsMalformedNotesFrameClosesTheStreamAndIsNotRefused(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	_, gc := ntDevice(t, env, "visitor")
	body := ntRaw(wire.TypeNotesPut, `,"noteId":"`+ntA+`","sealed":{"ct":"x"}`) // no baseRevision
	if err := gc.Conn.SendRaw(testkit.RawFrame(body)); err != nil {
		t.Fatal(err)
	}
	ntExpectClosed(t, gc, "a guest's put with no baseRevision")
}

// TestAMalformedNotesFrameClosesTheStreamEvenWhenTheServerDoesNotListNotes is the second consequence of Assumed 17:
// the frames are decoded before the engine asks whether the server lists `notes`, so a malformed put or list closes
// the stream on a server without the store, where a well formed one is ignored like an unknown type.
func TestAMalformedNotesFrameClosesTheStreamEvenWhenTheServerDoesNotListNotes(t *testing.T) {
	for _, c := range []struct {
		name string
		set  func(*testkit.Options)
	}{
		{"sealed without the store", func(o *testkit.Options) { o.Notes = false }},
		{"the store without sealed", func(o *testkit.Options) { o.Sealed = false }},
	} {
		t.Run(c.name, func(t *testing.T) {
			o := notesOpts("alice")
			c.set(&o)
			env := testkit.StartServer(t, o)
			d := env.NewDevice("alice")
			mustEnroll(t, d)
			for _, f := range []struct{ what, body string }{
				{"a put with no baseRevision", ntRaw(wire.TypeNotesPut, `,"noteId":"`+ntA+`","sealed":{"ct":"x"}`)},
				{"a list with no since", ntRaw(wire.TypeNotesList, ``)},
			} {
				ctl, err := d.OnlineQuietly()
				if err != nil {
					t.Fatal(err)
				}
				if err := ctl.Conn.SendRaw(testkit.RawFrame(f.body)); err != nil {
					t.Fatal(err)
				}
				ntExpectClosed(t, ctl, f.what)
			}
		})
	}
}

// TestAPayloadAtTheFrameBoundIsKeptAndOneBytePastIsRefusedCapacity is item FX (ticket L0322): a box is kept
// while the frame that carries its record alone still fits 262144 bytes, whichever way the record travels - the
// listing and the `current` of a conflict both; one byte more is refused `capacity` and stores nothing, although
// the put's own frame still fits the limit, and the stream answers and stays open.
func TestAPayloadAtTheFrameBoundIsKeptAndOneBytePastIsRefusedCapacity(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	d, c1 := ntDevice(t, env, "alice")
	bound := ntBoxBound(ntA, d.ID)
	if bound < 250000 || bound >= wire.MaxFrameAuth {
		t.Fatalf("the bound %d is not near the frame limit %d", bound, wire.MaxFrameAuth)
	}
	over := ntSized(bound+1, "")
	if _, err := wire.Frame(&wire.NotesPut{NoteID: ntA, BaseRevision: ntRev(0), Sealed: over}); err != nil {
		t.Fatalf("the put one byte past the bound must itself fit the frame limit, so that the record is what is refused: %v", err)
	}

	if st := ntStored(t, ntPut(t, c1, ntA, 0, ntSized(bound, ""))); st.Revision != 1 {
		t.Fatalf("a box at the bound: revision %d", st.Revision)
	}
	if got := ntList(t, c1, 0); len(got.Records) != 1 || len(got.Records[0].Sealed) != bound {
		t.Fatalf("a box at the bound is read back whole: %d records", len(got.Records))
	}
	// a record at the exact bound also travels as the `current` of a note-conflict, whole
	r := ntConflict(t, ntPut(t, c1, ntA, 0, ntBox("second copy")))
	if r.Current == nil || len(r.Current.Sealed) != bound {
		t.Fatalf("the exact-edge conflict answer: current %+v", r.Current)
	}

	// one byte past: a new note and an edit of the stored one are refused capacity, nothing is stored
	ntRefused(t, ntPut(t, c1, ntB, 0, over), wire.ReasonCapacity)
	ntRefused(t, ntPut(t, c1, ntA, 1, over), wire.ReasonCapacity)
	if _, err := c1.Request(&wire.NotesList{Since: ntRev(0)}, 3*time.Second, wire.TypeNotesChanges, wire.TypeRefused); err != nil {
		t.Fatalf("a capacity refusal must leave the stream open: %v", err)
	}
	if rev, _, n := env.Notes.State("alice"); rev != 1 || n != 1 {
		t.Fatalf("an oversize put changed the store: counter %d, %d records", rev, n)
	}
}

// --- listing: paging, full, the cursor ---

// TestAListingTooBigForOneFrameIsPagedWithMore is the `more` of item FI: the answer is cut where the next record
// would not fit one frame, the client asks again from the revision of the last record it got, and the pages put
// together are the whole listing.
func TestAListingTooBigForOneFrameIsPagedWithMore(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	_, c1 := ntDevice(t, env, "alice")
	_, c2 := ntDevice(t, env, "alice")
	for _, id := range []string{ntA, ntB, ntC, ntD, ntE} {
		ntStored(t, ntPut(t, c1, id, 0, ntSized(90000, "")))
	}
	want := [][]int64{{1, 2}, {3, 4}, {5}} // two records of 90000 bytes fit a frame of 256 KiB, three do not
	var since int64
	for i, w := range want {
		pg := ntList(t, c2, since)
		if !slices.Equal(ntRevs(pg), w) {
			t.Fatalf("page %d since %d: revisions %v, want %v", i+1, since, ntRevs(pg), w)
		}
		if pg.More != (i < len(want)-1) || pg.Full || pg.NotesRevision != 5 || pg.NotesHorizon != 0 {
			t.Fatalf("page %d: more %v, full %v, counter %d, horizon %d", i+1, pg.More, pg.Full, pg.NotesRevision, pg.NotesHorizon)
		}
		since = pg.Records[len(pg.Records)-1].Revision
	}
}

// TestAListingBelowTheHorizonIsFullAndAFullPageIsContinued covers item FW's stateless continuation.
func TestAListingBelowTheHorizonIsFullAndAFullPageIsContinued(t *testing.T) {
	clock := newClock()
	o := notesOpts("alice")
	o.Now = clock.Now
	env := testkit.StartServer(t, o)
	d, c1 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntE, 0, ntBox("short")))
	ntStored(t, ntDelete(t, c1, ntE, 1)) // the tombstone, revision 2, that the purge takes
	for _, id := range []string{ntA, ntB, ntC, ntD} {
		ntStored(t, ntPut(t, c1, id, 0, ntSized(90000, ""))) // revisions 3 to 6
	}
	c1.Close()
	ntAgeTombstones(env, clock)
	if rev, hz, n := env.Notes.State("alice"); rev != 6 || hz != 2 || n != 4 {
		t.Fatalf("after the purge: counter %d, horizon %d, %d records", rev, hz, n)
	}

	c, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	check := func(what string, since int64, resume bool, revs []int64, full, more bool) {
		t.Helper()
		ans, err := c.Request(&wire.NotesList{Since: ntRev(since), Full: resume}, 5*time.Second, wire.TypeNotesChanges, wire.TypeRefused)
		if err != nil {
			t.Fatal(err)
		}
		got, ok := ans.(*wire.NotesChanges)
		if !ok {
			t.Fatalf("%s: got %s", what, ntDescribe(ans))
		}
		if !slices.Equal(ntRevs(got), revs) || got.Full != full || got.More != more || got.NotesRevision != 6 || got.NotesHorizon != 2 {
			t.Fatalf("%s: revisions %v, full %v, more %v, counter %d, horizon %d", what, ntRevs(got), got.Full, got.More, got.NotesRevision, got.NotesHorizon)
		}
	}
	check("since 1 is below the horizon", 1, false, []int64{3, 4}, true, true)
	check("since 2 is the horizon", 2, false, []int64{3, 4}, false, true)
	check("since 0 starts a full listing", 0, false, []int64{3, 4}, true, true)
	check("the last revision continues the full listing", 4, true, []int64{5, 6}, true, false)
	check("a request without full is independent", 4, false, []int64{5, 6}, false, false)
	check("a since above the counter", 9, false, nil, false, false)

	check("a new full listing", 0, false, []int64{3, 4}, true, true)
	c.Close()
	c, err = d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	check("a new stream can continue", 4, true, []int64{5, 6}, true, false)
}

// TestTheCounterNeverComesBackAfterAPurgeOrARestart is item FG: the counter is the highest revision ever assigned,
// not the highest of the records that are left, so a revision is never given twice - not after the newest record was
// purged, and not after the server was started again on the same directory.
func TestTheCounterNeverComesBackAfterAPurgeOrARestart(t *testing.T) {
	clock := newClock()
	o := notesOpts("alice")
	o.Now = clock.Now
	env := testkit.StartServer(t, o)
	d, c1 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("short")))
	ntStored(t, ntDelete(t, c1, ntA, 1)) // revision 2: the newest record is the tombstone
	c1.Close()
	ntAgeTombstones(env, clock)
	if rev, hz, n := env.Notes.State("alice"); rev != 2 || hz != 2 || n != 0 {
		t.Fatalf("after the purge: counter %d, horizon %d, %d records", rev, hz, n)
	}
	env.Stop()

	o2 := notesOpts("alice")
	o2.Now = clock.Now
	o2.DataDir = env.DataDir
	env2 := testkit.StartServer(t, o2)
	d.Rebind(env2)
	c2, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	got := ntList(t, c2, 0)
	if got.NotesRevision != 2 || got.NotesHorizon != 2 || !got.Full || len(got.Records) != 0 {
		t.Fatalf("after the restart: counter %d, horizon %d, full %v, %d records", got.NotesRevision, got.NotesHorizon, got.Full, len(got.Records))
	}
	if st := ntStored(t, ntPut(t, c2, ntB, 0, ntBox("after"))); st.Revision != 3 {
		t.Fatalf("the first put after the purge and the restart: revision %d, want 3", st.Revision)
	}
	// the purged note is gone from the server: a client's copy at revision 1 finds no current record (Assumed 3)
	if r := ntConflict(t, ntPut(t, c2, ntA, 1, ntBox("old copy"))); r.Current != nil {
		t.Fatal("a purged note has no current record")
	}
}

// TestASweepWithNothingToPurgeLeavesTheStoreFileAlone is Assumed 10: the sweep writes the store file only when it
// purged something. The file is deterministic, so a needless rewrite would give the same bytes; the modification time
// is what tells it apart, and the pause makes sure that a rewrite could not fall on the same clock tick.
func TestASweepWithNothingToPurgeLeavesTheStoreFileAlone(t *testing.T) {
	clock := newClock()
	o := notesOpts("alice")
	o.Now = clock.Now
	env := testkit.StartServer(t, o)
	_, c1 := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, c1, ntA, 0, ntBox("old")))
	ntStored(t, ntDelete(t, c1, ntA, 1)) // revision 2: a tombstone that will be old enough
	ntStored(t, ntPut(t, c1, ntB, 0, ntBox("kept")))
	c1.Close()
	ntAgeTombstones(env, clock) // the first sweep purges the tombstone and writes the file
	if rev, hz, n := env.Notes.State("alice"); rev != 3 || hz != 2 || n != 1 {
		t.Fatalf("after the first sweep: counter %d, horizon %d, %d records", rev, hz, n)
	}

	file := filepath.Join(env.DataDir, notes.File)
	before, err := os.ReadFile(file)
	if err != nil {
		t.Fatal(err)
	}
	infoBefore, err := os.Stat(file)
	if err != nil {
		t.Fatal(err)
	}
	time.Sleep(50 * time.Millisecond)
	env.Engine.Sweep(clock.Now()) // nothing is left to purge
	after, err := os.ReadFile(file)
	if err != nil {
		t.Fatal(err)
	}
	infoAfter, err := os.Stat(file)
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatalf("a sweep with nothing to purge changed the store file: %d bytes before, %d after", len(before), len(after))
	}
	if !infoBefore.ModTime().Equal(infoAfter.ModTime()) {
		t.Fatal("a sweep with nothing to purge wrote the store file again")
	}
	if rev, hz, n := env.Notes.State("alice"); rev != 3 || hz != 2 || n != 1 {
		t.Fatalf("after the second sweep: counter %d, horizon %d, %d records", rev, hz, n)
	}
}

// --- the role ---

// TestAMemberDeviceWithoutTheRoleNotesStillPutsAndLists is Assumed 13: the role `notes` is information, not a gate.
// A device that declares none of it (the kit's default roles) is answered like any other device of a member; the
// role only chooses who is sent `notes_changed`.
func TestAMemberDeviceWithoutTheRoleNotesStillPutsAndLists(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice"))
	d, ctl := ntDeviceRoles(t, env, "alice", nil) // nil: the default roles, and `notes` is not one of them
	box := ntBox("no role")
	st := ntStored(t, ntPut(t, ctl, ntA, 0, box))
	if st.NoteID != ntA || st.Revision != 1 {
		t.Fatalf("stored: note %s, revision %d", st.NoteID, st.Revision)
	}
	got := ntList(t, ctl, 0)
	if got.NotesRevision != 1 || len(got.Records) != 1 {
		t.Fatalf("list: revision %d, %d records", got.NotesRevision, len(got.Records))
	}
	r := got.Records[0]
	if r.NoteID != ntA || r.Revision != 1 || r.SourceDeviceID != d.ID || r.Deleted || string(r.Sealed) != string(box) {
		t.Fatalf("the record read: note %s, revision %d, source %s, deleted %v, a box of %d bytes", r.NoteID, r.Revision, r.SourceDeviceID, r.Deleted, len(r.Sealed))
	}
}

// --- push ---

// TestNotesChangedGoesOnlyToTheOtherNotesDevicesOfTheAccount is the audience of item FI: after an accepted put
// (a delete included) the account's other online devices that declared the role `notes` are told, and nobody else
// is - not the device that put, a device without the role, another account - and a refusal tells nobody.
func TestNotesChangedGoesOnlyToTheOtherNotesDevicesOfTheAccount(t *testing.T) {
	env := testkit.StartServer(t, notesOpts("alice", "bob"))
	_, cs := ntDevice(t, env, "alice") // the device that puts
	_, cn := ntDevice(t, env, "alice") // another notes device
	_, cq := ntDeviceRoles(t, env, "alice", []string{"resource-consumer"})
	_, cb := ntDevice(t, env, "bob")

	ntStored(t, ntPut(t, cs, ntA, 0, ntBox("p1")))
	lines := ntAwaitPushed(t, env, 1)
	if !strings.Contains(lines[0], "queued=1") || !strings.Contains(lines[0], "skipped=0") {
		t.Fatalf("the push of the first put: %s", lines[0])
	}
	ans, err := cn.Await(3*time.Second, wire.TypeNotesChanged)
	if err != nil {
		t.Fatal(err)
	}
	if ch, ok := ans.(*wire.NotesChanged); !ok || ch.NotesRevision != 1 {
		t.Fatalf("the other notes device was told %s", ntDescribe(ans))
	}
	for name, c := range map[string]*testkit.Control{"the device that made the put": cs, "a device without the role": cq, "another account": cb} {
		if got, err := c.Await(400*time.Millisecond, wire.TypeNotesChanged); err == nil {
			t.Fatalf("%s was told: %s", name, ntDescribe(got))
		}
	}

	// a refusal changes nothing and tells nobody
	ntConflict(t, ntPut(t, cs, ntA, 0, ntBox("p2")))
	if got, err := cn.Await(400*time.Millisecond, wire.TypeNotesChanged); err == nil {
		t.Fatalf("a refused put was announced: %s", ntDescribe(got))
	}
	if n := len(ntPushLines(env)); n != 1 {
		t.Fatalf("%d pushes were logged for one accepted put", n)
	}

	// a delete is announced like a put, with its own revision
	ntStored(t, ntDelete(t, cs, ntA, 1))
	ntAwaitPushed(t, env, 2)
	ans, err = cn.Await(3*time.Second, wire.TypeNotesChanged)
	if err != nil {
		t.Fatal(err)
	}
	if ch, ok := ans.(*wire.NotesChanged); !ok || ch.NotesRevision != 2 {
		t.Fatalf("the delete was announced as %s", ntDescribe(ans))
	}
}

// --- erasure ---

const (
	ntAliceTag = "ALICEBOXTAG0001"
	ntBobTag   = "BOBBOXTAG0001"
)

// ntSeedTwoAccounts starts a server whose alice holds two notes and whose bob holds one, and returns alice's stream.
func ntSeedTwoAccounts(t *testing.T) (*testkit.Env, *testkit.Control) {
	t.Helper()
	env := testkit.StartServer(t, notesOpts("alice", "bob"))
	env.AddSecret(ntAliceTag)
	env.AddSecret(ntBobTag)
	_, ac := ntDevice(t, env, "alice")
	ntStored(t, ntPut(t, ac, ntA, 0, ntBox(ntAliceTag)))
	ntStored(t, ntPut(t, ac, ntB, 0, ntBox(ntAliceTag+"x")))
	_, bc := ntDevice(t, env, "bob")
	ntStored(t, ntPut(t, bc, ntA, 0, ntBox(ntBobTag)))
	return env, ac
}

// ntAssertAliceRecordsGoneBobKept waits for alice's records to be gone, in memory and in the file, and checks that
// bob's note is still there. What is left of alice's counter and horizon is the caller's to check: the entry goes
// whole with the account and the counter stays when only the kind changes.
func ntAssertAliceRecordsGoneBobKept(t *testing.T, env *testkit.Env) {
	t.Helper()
	eventually(t, "alice's records to be erased", func() bool {
		_, _, n := env.Notes.State("alice")
		return n == 0
	})
	raw, err := os.ReadFile(filepath.Join(env.DataDir, notes.File))
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(raw), ntAliceTag) {
		t.Fatal("alice's boxes are still in the store file")
	}
	if !strings.Contains(string(raw), ntBobTag) {
		t.Fatal("bob's box went with alice's")
	}
	if rev, _, n := env.Notes.State("bob"); rev != 1 || n != 1 {
		t.Fatalf("bob's notes: counter %d, %d records", rev, n)
	}
}

// ntAssertAliceEntryGone checks that the whole entry of alice is gone: records, counter and horizon.
func ntAssertAliceEntryGone(t *testing.T, env *testkit.Env) {
	t.Helper()
	ntAssertAliceRecordsGoneBobKept(t, env)
	if rev, hz, n := env.Notes.State("alice"); rev != 0 || hz != 0 || n != 0 {
		t.Fatalf("the account's entry is not gone: counter %d, horizon %d, %d records", rev, hz, n)
	}
}

// TestAnAccountsNotesAreErasedWithTheAccountAndWhenItBecomesAGuest is items AU and AO: the account's notes go when
// the holder deletes the account, when the operator removes it (the whole entry, counter and horizon with it), and
// when the operator makes it a guest (the records go but the counter stays and the horizon is raised to it, so a
// copy a device kept is told to start from the full listing and no revision is given twice) - and no other account
// loses a record.
func TestAnAccountsNotesAreErasedWithTheAccountAndWhenItBecomesAGuest(t *testing.T) {
	t.Run("the holder deletes the account", func(t *testing.T) {
		env, ac := ntSeedTwoAccounts(t)
		ans, err := ac.Request(&wire.AccountDelete{Password: testkit.TestPassword}, 5*time.Second, wire.TypeAccountDeleted, wire.TypeRefused)
		if err != nil || ans.TypeName() != wire.TypeAccountDeleted {
			t.Fatalf("account-delete: %v %s", err, ntDescribe(ans))
		}
		ntAssertAliceEntryGone(t, env)
	})
	t.Run("the operator removes the account", func(t *testing.T) {
		env, _ := ntSeedTwoAccounts(t)
		if err := env.Ops.RemoveAccount("alice"); err != nil {
			t.Fatal(err)
		}
		ntAssertAliceEntryGone(t, env)
	})
	t.Run("the operator makes the account a guest", func(t *testing.T) {
		env, _ := ntSeedTwoAccounts(t)
		if changed, err := env.Ops.SetKind("alice", "guest"); err != nil || !changed {
			t.Fatalf("set kind: changed %v, %v", changed, err)
		}
		ntAssertAliceRecordsGoneBobKept(t, env)
		// the records are gone and the counter is kept, with the horizon at it: neither is zero for an account
		// that held records
		rev, hz, n := env.Notes.State("alice")
		if n != 0 || rev == 0 || hz == 0 || hz != rev {
			t.Fatalf("a guest keeps its counter and its horizon: counter %d, horizon %d, %d records", rev, hz, n)
		}
		// made a member again, the account lists no record, is told to take a full listing, and its next revision
		// is above the old counter
		if changed, err := env.Ops.SetKind("alice", "member"); err != nil || !changed {
			t.Fatalf("set kind back: changed %v, %v", changed, err)
		}
		_, nc := ntDevice(t, env, "alice")
		got := ntList(t, nc, 0)
		if len(got.Records) != 0 || !got.Full || got.NotesRevision != rev || got.NotesHorizon != hz {
			t.Fatalf("the member again: %d records, full %v, counter %d, horizon %d", len(got.Records), got.Full, got.NotesRevision, got.NotesHorizon)
		}
		if st := ntStored(t, ntPut(t, nc, ntC, 0, ntBox("after"))); st.Revision <= rev {
			t.Fatalf("a put after the kind change: revision %d, not above the old counter %d", st.Revision, rev)
		}
	})
}

// --- the log ---

// TestNoBoxOfANoteReachesALog drives every path of the two frames that writes a log line - a put, a conflict that
// returns the stored box, a refusal for the count and for a guest, an oversize frame, a delete that carries a box, a
// list, the push, the purge and the erasure - and checks that no ciphertext of any box is in a line.
func TestNoBoxOfANoteReachesALog(t *testing.T) {
	clock := newClock()
	p := testkit.TestParams()
	p.NotesPerAccount = 2
	o := notesOpts("alice")
	o.Now = clock.Now
	o.Params = &p
	env := testkit.StartServer(t, o)
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	tags := []string{"LOGTAGAAAA0001", "LOGTAGBBBB0002", "LOGTAGCCCC0003", "LOGTAGDDDD0004", "LOGTAGEEEE0005", "LOGTAGFFFF0006", "LOGTAGGGGG0007"}
	for _, tag := range tags {
		env.AddSecret(tag)
	}

	a1, c1 := ntDevice(t, env, "alice")
	_, c2 := ntDevice(t, env, "alice")
	_, gc := ntDevice(t, env, "visitor")

	ntStored(t, ntPut(t, c1, ntA, 0, ntBox(tags[0])))                           // a put, and its push
	ntStored(t, ntPut(t, c1, ntB, 0, ntBox(tags[1])))                           // the second of two
	ntConflict(t, ntPut(t, c1, ntA, 0, ntBox(tags[2])))                         // a conflict: the reply carries the stored box
	ntRefused(t, ntPut(t, c1, ntC, 0, ntBox(tags[3])), wire.ReasonLimitReached) // the count
	ntRefused(t, ntPut(t, gc, ntA, 0, ntBox(tags[4])), wire.ReasonNotPermitted) // a guest
	if got := ntList(t, c2, 0); len(got.Records) != 2 {                         // the boxes go out over the wire
		t.Fatalf("list: %d records", len(got.Records))
	}
	del, err := c1.Request(&wire.NotesPut{NoteID: ntB, BaseRevision: ntRev(2), Deleted: true, Sealed: ntBox(tags[5])},
		5*time.Second, wire.TypeNotesStored, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	ntStored(t, del) // a delete that carries a box
	// the frame that cannot be delivered is refused for capacity and nothing is stored; the stream stays open (item FX).
	// It rewrites note A (revision 1), not a new note: the account holds two records of two, and the count limit
	// is checked before the capacity.
	ntRefused(t, ntPut(t, c1, ntA, 1, ntSized(ntBoxBound(ntA, a1.ID)+1, tags[6])), wire.ReasonCapacity)

	c2.Close()
	ntAgeTombstones(env, clock) // the purge of the tombstone
	if err := env.Ops.RemoveAccount("alice"); err != nil {
		t.Fatal(err)
	}

	// the run was not vacuous: every path wrote its line
	for _, event := range []string{"notes-stored", "notes-put-refused", "notes-changed-pushed", "notes-tombstones-purged", "notes-erased"} {
		if ntLogCount(env, event) == 0 {
			t.Fatalf("no %s line was logged", event)
		}
	}
	capacityLines := 0
	for _, l := range env.Log.Lines() {
		if strings.Contains(l, "\tnotes-put-refused\t") && strings.Contains(l, "capacity") {
			capacityLines++
		}
	}
	if capacityLines == 0 {
		t.Fatal("the oversize put wrote no notes-put-refused line for capacity")
	}
	env.Log.AssertNone(t, tags...)
	for _, l := range env.Log.Lines() {
		if strings.Contains(l, `"ct"`) || strings.Contains(l, `"keys"`) {
			t.Fatal("a log line carries a member of a box")
		}
	}
}
