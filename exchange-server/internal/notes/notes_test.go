package notes

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"sync"
	"testing"
	"time"
)

const (
	idA = "11111111-1111-4111-8111-111111111111"
	idB = "22222222-2222-4222-8222-222222222222"
	idC = "33333333-3333-4333-8333-333333333333"
	idD = "44444444-4444-4444-8444-444444444444"
	idE = "55555555-5555-4555-8555-555555555555"
	idF = "66666666-6666-4666-8666-666666666666"
)

const day = 24 * time.Hour

// t0 is where the injected clock starts; no test waits for time to pass, it moves the clock.
var t0 = time.Date(2026, time.November, 15, 10, 0, 0, 0, time.UTC)

type clock struct{ t time.Time }

func (c *clock) now() time.Time { return c.t }

func newStore(t *testing.T) (*Store, *clock, string) {
	t.Helper()
	dir := t.TempDir()
	c := &clock{t: t0}
	s, err := Open(dir, c.now)
	if err != nil {
		t.Fatal(err)
	}
	return s, c, dir
}

func reopen(t *testing.T, dir string, c *clock) *Store {
	t.Helper()
	s, err := Open(dir, c.now)
	if err != nil {
		t.Fatal(err)
	}
	return s
}

// box is a stand-in for a sealed box: the store only ever sees one JSON object.
func box(tag string) json.RawMessage {
	return json.RawMessage(`{"alg":"test","ct":"` + tag + `"}`)
}

func mustPut(t *testing.T, s *Store, account, device, id string, base int64, deleted bool, sealed json.RawMessage, limit int) Record {
	t.Helper()
	r, err := s.Put(account, device, id, base, deleted, sealed, limit)
	if err != nil {
		t.Fatalf("Put(%s, base %d, deleted %v): %v", id, base, deleted, err)
	}
	return r
}

func wantConflict(t *testing.T, err error) *ConflictError {
	t.Helper()
	var ce *ConflictError
	if !errors.As(err, &ce) {
		t.Fatalf("want a *ConflictError, got %v", err)
	}
	return ce
}

func wantWriteError(t *testing.T, err error) {
	t.Helper()
	if err == nil {
		t.Fatal("want a write error, got none")
	}
	var ce *ConflictError
	if errors.As(err, &ce) || errors.Is(err, ErrLimitReached) {
		t.Fatalf("want a write error, got a refusal: %v", err)
	}
}

func revisions(rs []Record) []int64 {
	out := make([]int64, 0, len(rs))
	for _, r := range rs {
		out = append(out, r.Revision)
	}
	return out
}

func filePath(dir string) string { return filepath.Join(dir, File) }

func readFile(t *testing.T, dir string) []byte {
	t.Helper()
	data, err := os.ReadFile(filePath(dir))
	if err != nil {
		t.Fatal(err)
	}
	return data
}

func fileExists(dir string) bool {
	_, err := os.Stat(filePath(dir))
	return err == nil
}

func TestCreateThenList(t *testing.T) {
	s, c, _ := newStore(t)
	a := mustPut(t, s, "alice", "dev1", idA, 0, false, box("a1"), 0)
	c.t = c.t.Add(time.Minute)
	b := mustPut(t, s, "alice", "dev2", idB, 0, false, box("b1"), 0)

	if a.NoteID != idA || a.Revision != 1 || a.StoredAt != "2026-11-15T10:00:00Z" || a.SourceDeviceID != "dev1" ||
		a.Deleted || string(a.Sealed) != string(box("a1")) {
		t.Fatalf("first record: %+v", a)
	}
	if b.NoteID != idB || b.Revision != 2 || b.StoredAt != "2026-11-15T10:01:00Z" || b.SourceDeviceID != "dev2" || b.Deleted {
		t.Fatalf("second record: %+v", b)
	}
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{a, b}) {
		t.Fatalf("All = %+v", got)
	}
	if got := s.Above("alice", 0); !reflect.DeepEqual(got, []Record{a, b}) {
		t.Fatalf("Above(0) = %+v", got)
	}
	if got := s.Above("alice", 1); !reflect.DeepEqual(got, []Record{b}) {
		t.Fatalf("Above(1) = %+v", got)
	}
	if got := s.Above("alice", 2); got == nil || len(got) != 0 {
		t.Fatalf("Above(2) = %#v, want an empty non-nil slice", got)
	}
	if rev, hor, n := s.State("alice"); rev != 2 || hor != 0 || n != 2 {
		t.Fatalf("State = %d %d %d", rev, hor, n)
	}

	// Another account has its own counter and its own records, even for the same noteId.
	other := mustPut(t, s, "bob", "dev9", idA, 0, false, box("x"), 0)
	if other.Revision != 1 {
		t.Fatalf("bob's first record has revision %d", other.Revision)
	}
	if s.Count() != 3 || s.Accounts() != 2 {
		t.Fatalf("Count %d, Accounts %d", s.Count(), s.Accounts())
	}

	// An account that holds nothing.
	if rev, hor, n := s.State("nobody"); rev != 0 || hor != 0 || n != 0 {
		t.Fatalf("State(nobody) = %d %d %d", rev, hor, n)
	}
	if got := s.Above("nobody", 0); got == nil || len(got) != 0 {
		t.Fatalf("Above(nobody) = %#v, want an empty non-nil slice", got)
	}

	// A returned slice is the caller's own: changing it changes nothing in the store.
	got := s.All("alice")
	got[0].Revision = 99
	if !reflect.DeepEqual(s.All("alice"), []Record{a, b}) {
		t.Fatal("a change to a returned slice reached the store")
	}
}

func TestEditNeedsTheCurrentBase(t *testing.T) {
	s, _, _ := newStore(t)
	r1 := mustPut(t, s, "alice", "dev1", idA, 0, false, box("v1"), 0)
	r2 := mustPut(t, s, "alice", "dev2", idA, r1.Revision, false, box("v2"), 0)
	if r2.Revision != 2 || r2.SourceDeviceID != "dev2" || string(r2.Sealed) != string(box("v2")) {
		t.Fatalf("edit: %+v", r2)
	}
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{r2}) {
		t.Fatalf("an edit replaces the record as a whole: %+v", got)
	}

	// A stale base is a conflict that carries the stored record, box included.
	_, err := s.Put("alice", "dev1", idA, r1.Revision, false, box("v3"), 0)
	ce := wantConflict(t, err)
	if ce.Current == nil || !reflect.DeepEqual(*ce.Current, r2) {
		t.Fatalf("stale base: current = %+v", ce.Current)
	}

	// Base 0 on an id the store holds is a conflict too.
	_, err = s.Put("alice", "dev1", idA, 0, false, box("v4"), 0)
	ce = wantConflict(t, err)
	if ce.Current == nil || !reflect.DeepEqual(*ce.Current, r2) {
		t.Fatalf("base 0 on an existing id: current = %+v", ce.Current)
	}

	// A refused delete is a conflict as well.
	_, err = s.Put("alice", "dev1", idA, r1.Revision, true, nil, 0)
	wantConflict(t, err)

	// Nothing was stored, and the refusals took no revision.
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{r2}) {
		t.Fatalf("a refusal changed the store: %+v", got)
	}
	if rev, _, _ := s.State("alice"); rev != 2 {
		t.Fatalf("counter after the refusals: %d", rev)
	}

	// Current is a copy, not the stored record.
	ce.Current.Revision = 99
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{r2}) {
		t.Fatal("a change to Current reached the store")
	}

	r3 := mustPut(t, s, "alice", "dev1", idA, r2.Revision, false, box("v5"), 0)
	if r3.Revision != 3 {
		t.Fatalf("the next accepted put has revision %d", r3.Revision)
	}
}

func TestBaseOnAnUnknownIDIsAConflictWithNoCurrent(t *testing.T) {
	s, _, dir := newStore(t)
	for _, base := range []int64{1, 4, -1} {
		_, err := s.Put("alice", "dev1", idA, base, false, box("x"), 0)
		if ce := wantConflict(t, err); ce.Current != nil {
			t.Fatalf("base %d: current = %+v, want nil", base, ce.Current)
		}
	}
	_, err := s.Put("alice", "dev1", idA, 4, true, nil, 0)
	if ce := wantConflict(t, err); ce.Current != nil {
		t.Fatalf("delete with base 4: current = %+v, want nil", ce.Current)
	}
	if rev, hor, n := s.State("alice"); rev != 0 || hor != 0 || n != 0 {
		t.Fatalf("State = %d %d %d", rev, hor, n)
	}
	if fileExists(dir) {
		t.Fatal("a refused put wrote the file")
	}
}

func TestTombstoneAcceptsItsOwnRevisionAsBase(t *testing.T) {
	s, _, _ := newStore(t)
	r1 := mustPut(t, s, "alice", "dev1", idA, 0, false, box("v1"), 0)
	tomb := mustPut(t, s, "alice", "dev1", idA, r1.Revision, true, nil, 0)
	if !tomb.Deleted || tomb.Revision != 2 {
		t.Fatalf("tombstone: %+v", tomb)
	}

	// Base 0 against a tombstone: the id is held, so it is a conflict, and Current is the tombstone.
	_, err := s.Put("alice", "dev1", idA, 0, false, box("again"), 0)
	ce := wantConflict(t, err)
	if ce.Current == nil || !ce.Current.Deleted || ce.Current.Revision != 2 || len(ce.Current.Sealed) != 0 {
		t.Fatalf("base 0 on a tombstone: current = %+v", ce.Current)
	}

	// An older copy of the note (an older base) never brings a deleted note back.
	_, err = s.Put("alice", "dev1", idA, r1.Revision, false, box("old copy"), 0)
	wantConflict(t, err)

	// The tombstone's own revision as base is accepted: the note is back, with a box and a new revision.
	r3 := mustPut(t, s, "alice", "dev3", idA, tomb.Revision, false, box("revived"), 0)
	if r3.Revision != 3 || r3.Deleted || string(r3.Sealed) != string(box("revived")) {
		t.Fatalf("revival: %+v", r3)
	}

	// A delete on a tombstone with its revision as base is accepted too and takes a new revision.
	t4 := mustPut(t, s, "alice", "dev3", idA, r3.Revision, true, nil, 0)
	t5 := mustPut(t, s, "alice", "dev3", idA, t4.Revision, true, nil, 0)
	if t4.Revision != 4 || t5.Revision != 5 || !t5.Deleted {
		t.Fatalf("deletes: %+v %+v", t4, t5)
	}
}

func TestDeleteStoresATombstoneWithoutABox(t *testing.T) {
	s, c, dir := newStore(t)
	r1 := mustPut(t, s, "alice", "dev1", idA, 0, false, box("secretive"), 0)
	d := mustPut(t, s, "alice", "dev2", idA, r1.Revision, true, box("given and dropped"), 0)
	if !d.Deleted || d.Revision != 2 || d.SourceDeviceID != "dev2" || len(d.Sealed) != 0 {
		t.Fatalf("tombstone: %+v", d)
	}
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{d}) {
		t.Fatalf("All = %+v", got)
	}

	data := readFile(t, dir)
	if bytes.Contains(data, []byte("given and dropped")) || bytes.Contains(data, []byte("secretive")) ||
		bytes.Contains(data, []byte(`"sealed"`)) {
		t.Fatalf("the file holds a box of a tombstone:\n%s", data)
	}
	var f fileShape
	if err := json.Unmarshal(data, &f); err != nil {
		t.Fatal(err)
	}
	if len(f.Accounts) != 1 || len(f.Accounts[0].Records) != 1 || !f.Accounts[0].Records[0].Deleted {
		t.Fatalf("file: %+v", f)
	}

	s2 := reopen(t, dir, c)
	if got := s2.All("alice"); !reflect.DeepEqual(got, []Record{d}) {
		t.Fatalf("after a restart: %+v", got)
	}
}

func TestDeleteOfAnUnknownIDCreatesATombstone(t *testing.T) {
	s, _, _ := newStore(t)
	d := mustPut(t, s, "alice", "dev1", idA, 0, true, nil, 0)
	if !d.Deleted || d.Revision != 1 || d.NoteID != idA || len(d.Sealed) != 0 {
		t.Fatalf("tombstone: %+v", d)
	}
	if rev, hor, n := s.State("alice"); rev != 1 || hor != 0 || n != 1 {
		t.Fatalf("State = %d %d %d", rev, hor, n)
	}
	// The tombstone is a record: base 0 no longer fits, and the answer shows it.
	_, err := s.Put("alice", "dev1", idA, 0, true, nil, 0)
	ce := wantConflict(t, err)
	if ce.Current == nil || !reflect.DeepEqual(*ce.Current, d) {
		t.Fatalf("current = %+v", ce.Current)
	}
}

func TestCountLimit(t *testing.T) {
	s, _, _ := newStore(t)
	const limit = 3
	for _, id := range []string{idA, idB, idC} {
		mustPut(t, s, "alice", "dev1", id, 0, false, box(id), limit)
	}

	// The fourth record would be above the count.
	_, err := s.Put("alice", "dev1", idD, 0, false, box("d"), limit)
	if !errors.Is(err, ErrLimitReached) {
		t.Fatalf("want ErrLimitReached, got %v", err)
	}
	if rev, _, n := s.State("alice"); rev != 3 || n != 3 {
		t.Fatalf("a refused put changed the store: revision %d, count %d", rev, n)
	}

	// An edit of an existing record and a delete are never refused for the count.
	edit := mustPut(t, s, "alice", "dev1", idA, 1, false, box("a2"), limit)
	if edit.Revision != 4 {
		t.Fatalf("edit: %+v", edit)
	}
	del := mustPut(t, s, "alice", "dev1", idB, 2, true, nil, limit)
	if del.Revision != 5 || !del.Deleted {
		t.Fatalf("delete: %+v", del)
	}
	// So is the delete of an id the store never held, which creates a tombstone above the count.
	ghost := mustPut(t, s, "alice", "dev1", idE, 0, true, nil, limit)
	if ghost.Revision != 6 {
		t.Fatalf("delete of an unknown id: %+v", ghost)
	}
	if _, _, n := s.State("alice"); n != 4 {
		t.Fatalf("count = %d, want 4 (tombstones count)", n)
	}

	// Tombstones count: the account holds 4 records, three of them live.
	_, err = s.Put("alice", "dev1", idD, 0, false, box("d"), limit)
	if !errors.Is(err, ErrLimitReached) {
		t.Fatalf("a full account with tombstones: want ErrLimitReached, got %v", err)
	}

	// A put on an existing record - a tombstone with its revision as base - is not a creation.
	revived := mustPut(t, s, "alice", "dev1", idB, del.Revision, false, box("b2"), limit)
	if revived.Revision != 7 || revived.Deleted {
		t.Fatalf("revival: %+v", revived)
	}

	// The conflict is checked before the limits (item FI): a base on an unknown id is a conflict, not a limit.
	_, err = s.Put("alice", "dev1", idD, 9, false, box("d"), limit)
	if ce := wantConflict(t, err); ce.Current != nil {
		t.Fatalf("current = %+v", ce.Current)
	}

	// Another account has its own count.
	mustPut(t, s, "bob", "dev1", idD, 0, false, box("d"), limit)

	// No cap: 0 and below.
	if r := mustPut(t, s, "alice", "dev1", idD, 0, false, box("d"), 0); r.Revision != 8 {
		t.Fatalf("no cap (0): %+v", r)
	}
	if r := mustPut(t, s, "alice", "dev1", idF, 0, false, box("f"), -1); r.Revision != 9 {
		t.Fatalf("no cap (-1): %+v", r)
	}
}

func TestCounterSurvivesARestart(t *testing.T) {
	s, c, dir := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a1"), 0)
	mustPut(t, s, "alice", "dev2", idB, 0, false, box("b1"), 0)
	mustPut(t, s, "alice", "dev1", idA, 1, false, box("a2"), 0)
	before := s.All("alice")

	s2 := reopen(t, dir, c)
	if rev, hor, n := s2.State("alice"); rev != 3 || hor != 0 || n != 2 {
		t.Fatalf("State after a restart = %d %d %d", rev, hor, n)
	}
	if got := s2.All("alice"); !reflect.DeepEqual(got, before) {
		t.Fatalf("records after a restart:\n got %+v\nwant %+v", got, before)
	}
	if r := mustPut(t, s2, "alice", "dev1", idB, 2, false, box("b2"), 0); r.Revision != 4 {
		t.Fatalf("the first put after a restart has revision %d", r.Revision)
	}
}

func TestRevisionIsNeverReusedAfterThePurgeOfTheNewestRecord(t *testing.T) {
	s, c, dir := newStore(t)
	r1 := mustPut(t, s, "alice", "dev1", idA, 0, false, box("a1"), 0)
	mustPut(t, s, "alice", "dev1", idA, r1.Revision, true, nil, 0) // the newest record: revision 2, a tombstone
	c.t = t0.Add(181 * day)

	n, err := s.PurgeTombstones(c.now())
	if err != nil || n != 1 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	// The store is empty, and the counter still says 2.
	if rev, hor, cnt := s.State("alice"); rev != 2 || hor != 2 || cnt != 0 {
		t.Fatalf("State after the purge = %d %d %d", rev, hor, cnt)
	}
	if s.Accounts() != 1 || s.Count() != 0 {
		t.Fatalf("Accounts %d, Count %d", s.Accounts(), s.Count())
	}
	if !bytes.Contains(readFile(t, dir), []byte(`"records": []`)) {
		t.Fatalf("an account with no records is written as an empty array:\n%s", readFile(t, dir))
	}

	// Both survive a restart.
	s2 := reopen(t, dir, c)
	if rev, hor, cnt := s2.State("alice"); rev != 2 || hor != 2 || cnt != 0 {
		t.Fatalf("State after a restart = %d %d %d", rev, hor, cnt)
	}
	// And the next record, even for the same note, never gets revision 2 again.
	r := mustPut(t, s2, "alice", "dev1", idA, 0, false, box("a3"), 0)
	if r.Revision != 3 {
		t.Fatalf("revision after the purge of the newest record: %d", r.Revision)
	}
}

func TestPurgeRemovesTombstonesOlderThan180Days(t *testing.T) {
	s, c, dir := newStore(t)
	live := mustPut(t, s, "alice", "dev1", idD, 0, false, box("live"), 0) // revision 1, never purged
	mustPut(t, s, "alice", "dev1", idA, 0, true, nil, 0)                  // revision 2, stored at t0
	mustPut(t, s, "alice", "dev1", idC, 0, true, nil, 0)                  // revision 3, stored at t0
	c.t = t0.Add(10 * day)
	young := mustPut(t, s, "alice", "dev1", idB, 0, true, nil, 0) // revision 4, stored at t0 + 10 days

	// A purge that removes nothing writes nothing: the file is taken away and must stay away.
	if err := os.Remove(filePath(dir)); err != nil {
		t.Fatal(err)
	}
	for _, now := range []time.Time{t0, t0.Add(179 * day), t0.Add(180 * day)} { // 180 days exactly is not more than 180
		n, err := s.PurgeTombstones(now)
		if err != nil || n != 0 {
			t.Fatalf("PurgeTombstones(%v) = %d, %v", now, n, err)
		}
	}
	if fileExists(dir) {
		t.Fatal("a purge that removed nothing wrote the file")
	}
	if rev, hor, n := s.State("alice"); rev != 4 || hor != 0 || n != 4 {
		t.Fatalf("State = %d %d %d", rev, hor, n)
	}

	// One second above 180 days: the two oldest tombstones go, the horizon is the highest revision purged.
	n, err := s.PurgeTombstones(t0.Add(180*day + time.Second))
	if err != nil || n != 2 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	if !fileExists(dir) {
		t.Fatal("a purge that removed records did not write the file")
	}
	if rev, hor, cnt := s.State("alice"); rev != 4 || hor != 3 || cnt != 2 {
		t.Fatalf("State after the purge = %d %d %d", rev, hor, cnt)
	}
	if got := revisions(s.All("alice")); !reflect.DeepEqual(got, []int64{1, 4}) {
		t.Fatalf("revisions after the purge: %v", got)
	}

	// The younger tombstone goes 180 days after its own storedAt; the live record never does.
	n, err = s.PurgeTombstones(t0.Add(190*day + time.Second))
	if err != nil || n != 1 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 4 || hor != young.Revision || cnt != 1 {
		t.Fatalf("State = %d %d %d", rev, hor, cnt)
	}
	n, err = s.PurgeTombstones(t0.Add(5000 * day))
	if err != nil || n != 0 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	if got := s.All("alice"); !reflect.DeepEqual(got, []Record{live}) {
		t.Fatalf("All = %+v", got)
	}

	// The horizon is on disk.
	s2 := reopen(t, dir, c)
	if rev, hor, cnt := s2.State("alice"); rev != 4 || hor != 4 || cnt != 1 {
		t.Fatalf("State after a restart = %d %d %d", rev, hor, cnt)
	}
}

func TestHorizonNeverMovesBack(t *testing.T) {
	s, c, _ := newStore(t)
	c.t = t0.Add(100 * day)
	mustPut(t, s, "alice", "dev1", idA, 0, true, nil, 0) // revision 1, stored at day 100
	c.t = t0
	mustPut(t, s, "alice", "dev1", idB, 0, true, nil, 0) // revision 2, stored at day 0 (the clock stepped back)

	if n, err := s.PurgeTombstones(t0.Add(181 * day)); err != nil || n != 1 {
		t.Fatalf("first purge = %d, %v", n, err)
	}
	if _, hor, _ := s.State("alice"); hor != 2 {
		t.Fatalf("horizon = %d, want 2", hor)
	}
	// The record purged now has revision 1, below the horizon.
	if n, err := s.PurgeTombstones(t0.Add(281*day + time.Second)); err != nil || n != 1 {
		t.Fatalf("second purge = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 2 || hor != 2 || cnt != 0 {
		t.Fatalf("State = %d %d %d", rev, hor, cnt)
	}
}

func TestPurgeCoversEveryAccount(t *testing.T) {
	s, c, _ := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, true, nil, 0)
	mustPut(t, s, "bob", "dev1", idA, 0, true, nil, 0)
	mustPut(t, s, "bob", "dev1", idB, 0, false, box("b"), 0)
	n, err := s.PurgeTombstones(c.now().Add(181 * day))
	if err != nil || n != 2 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 1 || hor != 1 || cnt != 0 {
		t.Fatalf("alice: %d %d %d", rev, hor, cnt)
	}
	if rev, hor, cnt := s.State("bob"); rev != 2 || hor != 1 || cnt != 1 {
		t.Fatalf("bob: %d %d %d", rev, hor, cnt)
	}
}

func TestErase(t *testing.T) {
	s, c, dir := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)
	mustPut(t, s, "alice", "dev1", idB, 0, false, box("b"), 0)
	mustPut(t, s, "alice", "dev1", idC, 0, true, nil, 0)
	bob := mustPut(t, s, "bob", "dev1", idA, 0, false, box("a"), 0)

	n, err := s.Erase("alice")
	if err != nil || n != 3 {
		t.Fatalf("Erase = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 0 || hor != 0 || cnt != 0 {
		t.Fatalf("State(alice) = %d %d %d", rev, hor, cnt)
	}
	if got := s.Above("alice", 0); len(got) != 0 {
		t.Fatalf("Above(alice) = %+v", got)
	}
	if got := s.All("bob"); !reflect.DeepEqual(got, []Record{bob}) {
		t.Fatalf("bob = %+v", got)
	}
	if s.Count() != 1 || s.Accounts() != 1 {
		t.Fatalf("Count %d, Accounts %d", s.Count(), s.Accounts())
	}
	if n, err := s.Erase("alice"); err != nil || n != 0 {
		t.Fatalf("second Erase = %d, %v", n, err)
	}

	// The entry went with its counter, on disk as well.
	s2 := reopen(t, dir, c)
	if s2.Accounts() != 1 || s2.Count() != 1 {
		t.Fatalf("after a restart: Accounts %d, Count %d", s2.Accounts(), s2.Count())
	}
	if r := mustPut(t, s2, "alice", "dev1", idA, 0, false, box("a"), 0); r.Revision != 1 {
		t.Fatalf("a new account starts at revision %d", r.Revision)
	}
}

// EraseRecords is what an account that goes on needs (its kind became guest, item AO): the records go, the counter
// stays (a revision never comes back, item FG) and the horizon is raised to it, so a device with any older `since`
// is answered `full` with nothing to list.
func TestEraseRecordsKeepsTheCounterAndMovesTheHorizonToIt(t *testing.T) {
	s, _, _ := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)  // revision 1
	mustPut(t, s, "alice", "dev1", idB, 0, false, box("b"), 0)  // 2
	mustPut(t, s, "alice", "dev1", idC, 0, true, nil, 0)        // 3, a tombstone
	mustPut(t, s, "alice", "dev1", idA, 1, false, box("a2"), 0) // 4, note A moves up
	bob := mustPut(t, s, "bob", "dev1", idA, 0, false, box("a"), 0)

	n, err := s.EraseRecords("alice")
	if err != nil || n != 3 {
		t.Fatalf("EraseRecords = %d, %v; want the 3 records (a tombstone counted)", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 4 || hor != 4 || cnt != 0 {
		t.Fatalf("State(alice) = %d %d %d, want counter 4, horizon 4, no records", rev, hor, cnt)
	}
	if got := s.Above("alice", 0); len(got) != 0 {
		t.Fatalf("Above(alice) = %+v", got)
	}
	if got := s.All("alice"); len(got) != 0 {
		t.Fatalf("All(alice) = %+v", got)
	}
	if got := s.All("bob"); !reflect.DeepEqual(got, []Record{bob}) {
		t.Fatalf("bob = %+v", got)
	}
	if rev, hor, cnt := s.State("bob"); rev != 1 || hor != 0 || cnt != 1 {
		t.Fatalf("State(bob) = %d %d %d", rev, hor, cnt)
	}
	if s.Count() != 1 || s.Accounts() != 2 {
		t.Fatalf("Count %d, Accounts %d: the entry of the account stays with its counter", s.Count(), s.Accounts())
	}

	// The next put is numbered above the old counter, and the horizon stays where the erasure put it.
	r := mustPut(t, s, "alice", "dev1", idD, 0, false, box("d"), 0)
	if r.Revision != 5 {
		t.Fatalf("the first put after the erasure has revision %d, want 5 (above the old counter 4)", r.Revision)
	}
	if rev, hor, cnt := s.State("alice"); rev != 5 || hor != 4 || cnt != 1 {
		t.Fatalf("State(alice) after the put = %d %d %d, want 5 4 1", rev, hor, cnt)
	}
	if got := s.Above("alice", 4); !reflect.DeepEqual(got, []Record{r}) {
		t.Fatalf("Above(alice, 4) = %+v", got)
	}

	// Erase is still the removal of the whole entry, counter included.
	if n, err := s.Erase("alice"); err != nil || n != 1 {
		t.Fatalf("Erase = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 0 || hor != 0 || cnt != 0 {
		t.Fatalf("State(alice) after Erase = %d %d %d", rev, hor, cnt)
	}
}

func TestEraseRecordsSurvivesARestart(t *testing.T) {
	s, c, dir := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)
	mustPut(t, s, "alice", "dev1", idB, 0, false, box("b"), 0)
	mustPut(t, s, "alice", "dev1", idA, 1, false, box("a2"), 0) // counter 3
	mustPut(t, s, "bob", "dev1", idA, 0, false, box("a"), 0)
	if n, err := s.EraseRecords("alice"); err != nil || n != 2 {
		t.Fatalf("EraseRecords = %d, %v", n, err)
	}

	// The file holds the entry: the counter and the horizon, and an empty list of records (never null).
	var f fileShape
	if err := json.Unmarshal(readFile(t, dir), &f); err != nil {
		t.Fatal(err)
	}
	var alice *accountFile
	for i := range f.Accounts {
		if f.Accounts[i].Account == "alice" {
			alice = &f.Accounts[i]
		}
	}
	if alice == nil {
		t.Fatalf("the entry of the account is gone from the file: %+v", f)
	}
	if alice.Revision != 3 || alice.Horizon != 3 || alice.Records == nil || len(alice.Records) != 0 {
		t.Fatalf("the entry in the file = %+v, want counter 3, horizon 3, records []", *alice)
	}

	s2 := reopen(t, dir, c)
	if rev, hor, cnt := s2.State("alice"); rev != 3 || hor != 3 || cnt != 0 {
		t.Fatalf("State(alice) after a restart = %d %d %d, want 3 3 0", rev, hor, cnt)
	}
	if s2.Accounts() != 2 || s2.Count() != 1 {
		t.Fatalf("after a restart: Accounts %d, Count %d", s2.Accounts(), s2.Count())
	}
	// A sweep leaves an entry without records alone, and a later put goes on from the counter.
	if n, err := s2.PurgeTombstones(c.now().Add(1000 * day)); err != nil || n != 0 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
	if rev, hor, _ := s2.State("alice"); rev != 3 || hor != 3 {
		t.Fatalf("State(alice) after the sweep = %d %d", rev, hor)
	}
	if r := mustPut(t, s2, "alice", "dev1", idC, 0, false, box("c"), 0); r.Revision != 4 {
		t.Fatalf("revision after the restart = %d, want 4", r.Revision)
	}
}

func TestEraseRecordsIsANoOpWhereThereIsNothingToErase(t *testing.T) {
	// an account the store does not hold: nothing is written, not even the directory
	dir := filepath.Join(t.TempDir(), "data") // not created yet
	c := &clock{t: t0}
	s, err := Open(dir, c.now)
	if err != nil {
		t.Fatal(err)
	}
	if n, err := s.EraseRecords("alice"); n != 0 || err != nil {
		t.Fatalf("EraseRecords of an unknown account = %d, %v", n, err)
	}
	if _, err := os.Stat(dir); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("the data directory exists after nothing was erased: %v", err)
	}
	if s.Accounts() != 0 {
		t.Fatalf("Accounts = %d: an entry was made for an unknown account", s.Accounts())
	}

	// an entry with counter 0 (a hand-made file): the file is left as it is
	empty := t.TempDir()
	hand := []byte(`{"storeVersion":1,"accounts":[{"account":"zed","revision":0,"horizon":0,"records":[]}]}`)
	if err := os.WriteFile(filePath(empty), hand, 0o600); err != nil {
		t.Fatal(err)
	}
	s0 := reopen(t, empty, c)
	if n, err := s0.EraseRecords("zed"); n != 0 || err != nil {
		t.Fatalf("EraseRecords of counter 0 = %d, %v", n, err)
	}
	if got := readFile(t, empty); !bytes.Equal(got, hand) {
		t.Fatalf("the file was rewritten:\n%s", got)
	}

	// an account already erased: the second call writes nothing (the file is removed between the two to see it)
	s1, _, dir1 := newStore(t)
	mustPut(t, s1, "alice", "dev1", idA, 0, false, box("a"), 0)
	if n, err := s1.EraseRecords("alice"); n != 1 || err != nil {
		t.Fatalf("EraseRecords = %d, %v", n, err)
	}
	if err := os.Remove(filePath(dir1)); err != nil {
		t.Fatal(err)
	}
	if n, err := s1.EraseRecords("alice"); n != 0 || err != nil {
		t.Fatalf("second EraseRecords = %d, %v", n, err)
	}
	if fileExists(dir1) {
		t.Fatal("the second EraseRecords wrote the file again")
	}
	if rev, hor, cnt := s1.State("alice"); rev != 1 || hor != 1 || cnt != 0 {
		t.Fatalf("State(alice) = %d %d %d", rev, hor, cnt)
	}
}

func TestAFailedEraseRecordsRestoresMemory(t *testing.T) {
	s, c, good := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a1"), 0)
	mustPut(t, s, "alice", "dev1", idB, 0, true, nil, 0)
	before := s.All("alice")

	// A data directory that sits under a regular file: MkdirAll fails on every platform.
	blocker := filepath.Join(t.TempDir(), "blocker")
	if err := os.WriteFile(blocker, []byte("x"), 0o600); err != nil {
		t.Fatal(err)
	}
	s.dir = filepath.Join(blocker, "sub")

	n, err := s.EraseRecords("alice")
	wantWriteError(t, err)
	if n != 0 {
		t.Fatalf("a failed erasure reports %d", n)
	}
	if got := s.All("alice"); !reflect.DeepEqual(got, before) {
		t.Fatalf("memory after the failure:\n got %+v\nwant %+v", got, before)
	}
	if rev, hor, cnt := s.State("alice"); rev != 2 || hor != 0 || cnt != 2 {
		t.Fatalf("State(alice) = %d %d %d, want 2 0 2: records, counter and horizon as they were", rev, hor, cnt)
	}
	if got := reopen(t, good, c).All("alice"); !reflect.DeepEqual(got, before) {
		t.Fatalf("the file on disk changed: %+v", got)
	}

	// With the directory back, the same call works.
	s.dir = good
	if n, err := s.EraseRecords("alice"); err != nil || n != 2 {
		t.Fatalf("EraseRecords = %d, %v", n, err)
	}
	if rev, hor, cnt := s.State("alice"); rev != 2 || hor != 2 || cnt != 0 {
		t.Fatalf("State(alice) = %d %d %d, want 2 2 0", rev, hor, cnt)
	}
}

// A write that fails midway removes its own temp file (a partial file must not stay for a backup tool to copy),
// but only a regular file: a directory of that name is not ours to remove, and a good write leaves no temp file.
func TestAFailedWriteRemovesOnlyItsOwnTempFile(t *testing.T) {
	s, _, dir := newStore(t)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)

	// A directory of the temp file's name makes the write fail; it is not ours to remove.
	tmp := filePath(dir) + ".tmp"
	if err := os.Mkdir(tmp, 0o700); err != nil {
		t.Fatal(err)
	}
	_, err := s.Put("alice", "dev1", idB, 0, false, box("b"), 0)
	wantWriteError(t, err)
	if fi, err := os.Stat(tmp); err != nil || !fi.IsDir() {
		t.Fatalf("the directory in the way of the temp file was removed: %v", err)
	}
	if err := os.Remove(tmp); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Put("alice", "dev1", idB, 0, false, box("b"), 0); err != nil {
		t.Fatalf("Put after the way was cleared: %v", err)
	}
	if _, err := os.Stat(tmp); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("a temp file stayed after a good write: %v", err)
	}
}

func TestNothingIsWrittenUntilTheFirstAcceptedPut(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "data") // not created yet
	c := &clock{t: t0}
	s, err := Open(dir, c.now)
	if err != nil {
		t.Fatal(err)
	}

	s.State("alice")
	s.Above("alice", 0)
	s.All("alice")
	if s.Count() != 0 || s.Accounts() != 0 {
		t.Fatalf("Count %d, Accounts %d", s.Count(), s.Accounts())
	}
	if n, err := s.Erase("alice"); n != 0 || err != nil {
		t.Fatalf("Erase = %d, %v", n, err)
	}
	if n, err := s.PurgeTombstones(c.now().Add(1000 * day)); n != 0 || err != nil {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}

	// Refused or malformed puts store nothing and write nothing.
	if _, err := s.Put("alice", "dev1", idA, 3, false, box("x"), 0); err == nil {
		t.Fatal("a put with a base on an unknown id was accepted")
	}
	for name, sealed := range map[string]json.RawMessage{
		"no box":            nil,
		"a box that is cut": json.RawMessage(`{"ct":`),
		"two values":        json.RawMessage(`{} {}`),
	} {
		_, err := s.Put("alice", "dev1", idA, 0, false, sealed, 0)
		if err == nil {
			t.Fatalf("%s: the put was accepted", name)
		}
		var ce *ConflictError
		if errors.As(err, &ce) || errors.Is(err, ErrLimitReached) {
			t.Fatalf("%s: a refusal where a plain error belongs: %v", name, err)
		}
	}
	if _, err := os.Stat(dir); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("the data directory exists after nothing was accepted: %v", err)
	}
	if s.Accounts() != 0 {
		t.Fatalf("Accounts = %d", s.Accounts())
	}

	// The first accepted put creates the directory and the file, and leaves no temp file behind.
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)
	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 || entries[0].Name() != File {
		t.Fatalf("directory after the first put: %v", entries)
	}
}

func TestAFailedWriteRestoresMemory(t *testing.T) {
	s, c, good := newStore(t)
	a := mustPut(t, s, "alice", "dev1", idA, 0, false, box("a1"), 0)
	mustPut(t, s, "alice", "dev1", idB, 0, true, nil, 0) // an old tombstone for the purge
	c.t = t0.Add(181 * day)
	before := s.All("alice")

	// A data directory that sits under a regular file: MkdirAll fails on every platform.
	blocker := filepath.Join(t.TempDir(), "blocker")
	if err := os.WriteFile(blocker, []byte("x"), 0o600); err != nil {
		t.Fatal(err)
	}
	s.dir = filepath.Join(blocker, "sub")

	_, err := s.Put("alice", "dev1", idC, 0, false, box("c"), 0) // creates a record
	wantWriteError(t, err)
	_, err = s.Put("alice", "dev1", idA, a.Revision, false, box("a2"), 0) // replaces one
	wantWriteError(t, err)
	_, err = s.Put("carol", "dev1", idA, 0, false, box("x"), 0) // creates an account
	wantWriteError(t, err)
	n, err := s.PurgeTombstones(c.now())
	wantWriteError(t, err)
	if n != 0 {
		t.Fatalf("a failed purge reports %d", n)
	}
	n, err = s.Erase("alice")
	wantWriteError(t, err)
	if n != 0 {
		t.Fatalf("a failed erase reports %d", n)
	}

	if got := s.All("alice"); !reflect.DeepEqual(got, before) {
		t.Fatalf("memory after the failures:\n got %+v\nwant %+v", got, before)
	}
	if rev, hor, cnt := s.State("alice"); rev != 2 || hor != 0 || cnt != 2 {
		t.Fatalf("State(alice) = %d %d %d", rev, hor, cnt)
	}
	if s.Accounts() != 1 || s.Count() != 2 {
		t.Fatalf("Accounts %d, Count %d: an account of a failed put stayed", s.Accounts(), s.Count())
	}
	if got := reopen(t, good, c).All("alice"); !reflect.DeepEqual(got, before) {
		t.Fatalf("the file on disk changed: %+v", got)
	}

	// With the directory back, everything works and the failures took no revision.
	s.dir = good
	if r := mustPut(t, s, "alice", "dev1", idC, 0, false, box("c"), 0); r.Revision != 3 {
		t.Fatalf("revision after the failures: %d", r.Revision)
	}
	if n, err := s.PurgeTombstones(c.now()); err != nil || n != 1 {
		t.Fatalf("PurgeTombstones = %d, %v", n, err)
	}
}

func TestTheBoxIsKeptAsReceived(t *testing.T) {
	s, c, dir := newStore(t)
	raw := []byte(`{ "alg": "x", "ct": "<a&b> ", "keys": [ {"to":"d1"} , {"to":"d2"} ] }`)
	want := `{"alg":"x","ct":"<a&b> ","keys":[{"to":"d1"},{"to":"d2"}]}`

	r := mustPut(t, s, "alice", "dev1", idA, 0, false, json.RawMessage(raw), 0)
	if string(r.Sealed) != want {
		t.Fatalf("stored box = %s", r.Sealed)
	}
	// The caller's buffer is its own: the store holds a copy.
	for i := range raw {
		raw[i] = 'X'
	}
	if got := s.All("alice")[0]; string(got.Sealed) != want {
		t.Fatalf("box after the caller reused its buffer = %s", got.Sealed)
	}

	// The file does not escape what it was given, and a restart gives back the same bytes.
	data := readFile(t, dir)
	if !bytes.Contains(data, []byte("<a&b>")) || bytes.Contains(data, []byte(`\`+`u003c`)) {
		t.Fatalf("the box was rewritten in the file:\n%s", data)
	}
	s2 := reopen(t, dir, c)
	if got := s2.All("alice")[0]; string(got.Sealed) != want {
		t.Fatalf("box after a restart = %s", got.Sealed)
	}
}

func TestFileShape(t *testing.T) {
	s, c, dir := newStore(t)
	mustPut(t, s, "bob", "dev1", idA, 0, false, json.RawMessage(`{ "ct": "b" }`), 0)
	mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)
	mustPut(t, s, "alice", "dev2", idB, 0, false, box("b"), 0)
	mustPut(t, s, "alice", "dev1", idA, 1, false, box("a2"), 0) // moves note A behind note B

	data := readFile(t, dir)
	var f fileShape
	if err := json.Unmarshal(data, &f); err != nil {
		t.Fatal(err)
	}
	if f.StoreVersion != 1 || len(f.Accounts) != 2 || f.Accounts[0].Account != "alice" || f.Accounts[1].Account != "bob" {
		t.Fatalf("file: %+v", f)
	}
	alice := f.Accounts[0]
	if alice.Revision != 3 || alice.Horizon != 0 || len(alice.Records) != 2 ||
		alice.Records[0].NoteID != idB || alice.Records[1].NoteID != idA {
		t.Fatalf("alice in the file: %+v", alice)
	}
	if bob := f.Accounts[1]; bob.Revision != 1 || len(bob.Records) != 1 {
		t.Fatalf("bob in the file: %+v", bob)
	}

	// Open and write again: the bytes do not move (boxes included, whatever white space they came in).
	s2 := reopen(t, dir, c)
	s2.mu.Lock()
	err := s2.writeLocked()
	s2.mu.Unlock()
	if err != nil {
		t.Fatal(err)
	}
	if again := readFile(t, dir); !bytes.Equal(again, data) {
		t.Fatalf("a rewrite changed the file:\n%s\n---\n%s", data, again)
	}
}

func TestOpenRefusesWhatItCannotRead(t *testing.T) {
	const rec = `{"noteId":"n1","revision":1,"storedAt":"2026-11-15T10:00:00Z","sourceDeviceId":"d","sealed":{"ct":"a"}}`
	cases := map[string]string{
		"not json":      `{`,
		"wrong version": `{"storeVersion":2,"accounts":[]}`,
		"no version":    `{"accounts":[]}`,
		"account twice": `{"storeVersion":1,"accounts":[{"account":"a","revision":0,"horizon":0,"records":[]},{"account":"a","revision":0,"horizon":0,"records":[]}]}`,
		"note twice":    `{"storeVersion":1,"accounts":[{"account":"a","revision":1,"horizon":0,"records":[` + rec + `,` + rec + `]}]}`,
	}
	for name, content := range cases {
		t.Run(name, func(t *testing.T) {
			dir := t.TempDir()
			if err := os.WriteFile(filePath(dir), []byte(content), 0o600); err != nil {
				t.Fatal(err)
			}
			if _, err := Open(dir, nil); err == nil {
				t.Fatal("Open accepted the file")
			}
		})
	}
}

func TestOpenKeepsTheCounterAboveEveryRecord(t *testing.T) {
	dir := t.TempDir()
	// A hand-edited file: a counter below its newest record, and a tombstone that carries a box.
	content := `{"storeVersion":1,"accounts":[{"account":"a","revision":1,"horizon":0,"records":[` +
		`{"noteId":"n1","revision":5,"storedAt":"2026-11-15T10:00:00Z","sourceDeviceId":"d","sealed":{ "ct": "a" }},` +
		`{"noteId":"n2","revision":4,"storedAt":"2026-11-15T10:00:00Z","sourceDeviceId":"d","deleted":true,"sealed":{"ct":"b"}}]}]}`
	if err := os.WriteFile(filePath(dir), []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
	s, err := Open(dir, (&clock{t: t0}).now)
	if err != nil {
		t.Fatal(err)
	}
	if rev, _, n := s.State("a"); rev != 5 || n != 2 {
		t.Fatalf("State = %d _ %d, want the counter raised to 5", rev, n)
	}
	all := s.All("a")
	if got := revisions(all); !reflect.DeepEqual(got, []int64{4, 5}) {
		t.Fatalf("revisions = %v", got)
	}
	if len(all[0].Sealed) != 0 || string(all[1].Sealed) != `{"ct":"a"}` {
		t.Fatalf("boxes: %q and %q", all[0].Sealed, all[1].Sealed)
	}
	if r := mustPut(t, s, "a", "d", "n3", 0, false, box("c"), 0); r.Revision != 6 {
		t.Fatalf("next revision = %d", r.Revision)
	}

	// An empty store of the current version is a store.
	empty := t.TempDir()
	if err := os.WriteFile(filePath(empty), []byte(`{"storeVersion":1,"accounts":[]}`), 0o600); err != nil {
		t.Fatal(err)
	}
	s2, err := Open(empty, nil)
	if err != nil || s2.Accounts() != 0 {
		t.Fatalf("empty store: %v", err)
	}
}

func TestNilClockMeansTheWallClock(t *testing.T) {
	s, err := Open(t.TempDir(), nil)
	if err != nil {
		t.Fatal(err)
	}
	r := mustPut(t, s, "alice", "dev1", idA, 0, false, box("a"), 0)
	at, err := time.Parse(time.RFC3339, r.StoredAt)
	if err != nil {
		t.Fatal(err)
	}
	if r.StoredAt[len(r.StoredAt)-1] != 'Z' {
		t.Fatalf("storedAt %q is not UTC", r.StoredAt)
	}
	if d := time.Since(at); d < -time.Minute || d > time.Minute {
		t.Fatalf("storedAt %q is %v from now", r.StoredAt, d)
	}
}

func TestConcurrentUse(t *testing.T) {
	s, _, _ := newStore(t)
	const workers, each = 4, 5
	var wg sync.WaitGroup
	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < each; i++ {
				id := fmt.Sprintf("00000000-0000-4000-8000-%04d%08d", w, i)
				if _, err := s.Put("alice", "dev", id, 0, false, box(id), 0); err != nil {
					t.Errorf("worker %d, put %d: %v", w, i, err)
					return
				}
				s.State("alice")
				s.Above("alice", 0)
				s.Count()
			}
		}()
	}
	wg.Wait()

	all := s.All("alice")
	if len(all) != workers*each {
		t.Fatalf("%d records, want %d", len(all), workers*each)
	}
	for i, r := range all {
		if r.Revision != int64(i+1) {
			t.Fatalf("record %d has revision %d: revisions are not one run", i, r.Revision)
		}
	}
}
