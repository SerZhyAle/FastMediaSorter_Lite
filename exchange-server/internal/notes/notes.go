// Package notes keeps the note records of DEVICE-EXCHANGE 1.13 (items FG, FJ and FL, ticket L0321): per member
// account, the sealed notes its devices synchronise through the server, on disk, in one JSON file, readable by the
// operator. The server never opens a box (item DQ): a record's `sealed` is one opaque JSON object that is stored
// and handed back as it came, and nothing in this package reads inside it. The package imports no other package of
// the server (like internal/settings), so the wire layer and the engine can both depend on it without a cycle.
//
// Nothing here logs or prints a box. The only text it produces is an error, and an error never carries one.
package notes

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"
)

// File is the store's file in the data directory. It exists from the first accepted put on, so a server that
// never saw one keeps the directory it had before notes existed.
const File = "notes.json"

// StoreVersion is the shape of File.
const StoreVersion = 1

// TombstoneDays is the age, counted from a tombstone's storedAt, above which PurgeTombstones removes it
// (item FG: 180 days).
const TombstoneDays = 180

// ErrLimitReached is a put that would create a record above the per-account count (item FL).
var ErrLimitReached = errors.New("notes: the account holds as many records as the limit allows")

// ErrCapacity is a put whose record could not be carried in one delivery frame (DEVICE-EXCHANGE 1.17
// item FX, ticket L0322): refused, nothing stored. The handler checks it last, after the conflict and
// both limits, so a request that fails an earlier rule answers with that rule's reason.
var ErrCapacity = errors.New("notes: the record does not fit one delivery frame")

// ConflictError is a put refused by the conflict rule of item FJ; nothing was stored. Current is a copy of the
// record the store holds for the noteId, box included, or nil when it holds none (a note purged as a tombstone,
// or one this server never stored): the client reads a nil as "gone from the server".
type ConflictError struct {
	Current *Record
}

func (e *ConflictError) Error() string { return "notes: note-conflict" }

// Record is one note record of item FG. Sealed is the box of item DH, kept as the one JSON object it arrived as
// (insignificant white space removed, never a member read) and empty on a tombstone. A Record handed out by this
// package shares its Sealed bytes with the store: the store never changes them, a caller must not either.
type Record struct {
	NoteID         string          `json:"noteId"`
	Revision       int64           `json:"revision"`
	StoredAt       string          `json:"storedAt"` // RFC 3339, UTC
	SourceDeviceID string          `json:"sourceDeviceId"`
	Deleted        bool            `json:"deleted,omitempty"`
	Sealed         json.RawMessage `json:"sealed,omitempty"`
}

// accountFile is one account in File. Revision is the counter - the highest revision ever assigned to the
// account, which is not the highest revision of its records once the newest one was purged - so that a revision
// never comes back (item FG).
type accountFile struct {
	Account  string   `json:"account"`
	Revision int64    `json:"revision"`
	Horizon  int64    `json:"horizon"`
	Records  []Record `json:"records"`
}

type fileShape struct {
	StoreVersion int           `json:"storeVersion"`
	Accounts     []accountFile `json:"accounts"`
}

// ledger is the memory form of one account: its records by noteId, so the conflict rule is one lookup.
type ledger struct {
	revision int64
	horizon  int64
	records  map[string]Record
}

// Store is the notes of every account. It is safe for concurrent use.
type Store struct {
	dir string
	now func() time.Time

	mu  sync.Mutex
	led map[string]*ledger
}

// Open reads the store of a data directory; an absent file is an empty store, and nothing is written until the
// first accepted put.
func Open(dir string, now func() time.Time) (*Store, error) {
	if now == nil {
		now = time.Now
	}
	s := &Store{dir: dir, now: now, led: map[string]*ledger{}}
	data, err := os.ReadFile(filepath.Join(dir, File))
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, err
	}
	var f fileShape
	if err := json.Unmarshal(data, &f); err != nil {
		return nil, fmt.Errorf("%s is not valid JSON: %w", File, err)
	}
	if f.StoreVersion != StoreVersion {
		return nil, fmt.Errorf("%s has storeVersion %d, this server reads %d", File, f.StoreVersion, StoreVersion)
	}
	for _, af := range f.Accounts {
		if _, dup := s.led[af.Account]; dup {
			return nil, fmt.Errorf("%s holds an account twice", File)
		}
		l := &ledger{revision: af.Revision, horizon: af.Horizon, records: make(map[string]Record, len(af.Records))}
		for _, r := range af.Records {
			if _, dup := l.records[r.NoteID]; dup {
				return nil, fmt.Errorf("%s holds a note twice", File)
			}
			if r.Deleted {
				r.Sealed = nil // a tombstone has no box (item FG)
			} else if len(r.Sealed) > 0 {
				// The file is indented, so a box comes back with white space in it: take the form Put keeps.
				var buf bytes.Buffer
				if err := json.Compact(&buf, r.Sealed); err != nil {
					return nil, fmt.Errorf("%s holds a box that is not JSON: %w", File, err)
				}
				r.Sealed = buf.Bytes()
			}
			if r.Revision > l.revision {
				l.revision = r.Revision // a hand-edited counter below a record would let a revision come back
			}
			l.records[r.NoteID] = r
		}
		s.led[af.Account] = l
	}
	return s, nil
}

// Put stores one note of an account as a whole (item FJ: no field merge) and returns the record with its new
// revision. The order of the checks is item FX's (DEVICE-EXCHANGE 1.17): the conflict rule first, then the
// count limit, then the byte limit, then the delivery bound.
//
// The put is accepted only when the store holds a record for noteID and baseRevision is its revision (a tombstone
// included), or when baseRevision is 0 and it holds none; anything else is a *ConflictError and changes nothing.
// A delete stores a tombstone and no box, whatever sealed holds. A delete of a noteID the store never held, with
// baseRevision 0, creates a tombstone too.
//
// The limit applies only to a put that would create a record: no record of that noteID, not a delete, maxRecords
// above 0 and the account already holding maxRecords records, tombstones counted. A put on an existing record
// and every delete are never refused for the count; maxRecords of 0 or less means no cap.
//
// The file is written before the answer; a write that fails leaves the previous state in memory and on disk.
func (s *Store) Put(account, sourceDeviceID, noteID string, baseRevision int64, deleted bool, sealed json.RawMessage, maxRecords int) (Record, error) {
	return s.put(account, sourceDeviceID, noteID, baseRevision, deleted, sealed, maxRecords, -1, false, true)
}

// PutLimited applies both notes limits, including zero, under the store lock, and the delivery bound of
// item FX after them: deliverable is false for a record that could not be carried in one frame, refused
// without a write (nothing stored).
func (s *Store) PutLimited(account, sourceDeviceID, noteID string, baseRevision int64, deleted bool, sealed json.RawMessage, maxRecords int, maxBytes int64, deliverable bool) (Record, error) {
	return s.put(account, sourceDeviceID, noteID, baseRevision, deleted, sealed, maxRecords, maxBytes, true, deliverable)
}

func (s *Store) put(account, sourceDeviceID, noteID string, baseRevision int64, deleted bool, sealed json.RawMessage, maxRecords int, maxBytes int64, zeroCountLimit bool, deliverable bool) (Record, error) {
	// The frame members were checked by the wire layer; these two keep the file valid even if one slips by.
	var box json.RawMessage
	if !deleted {
		if len(sealed) == 0 {
			return Record{}, errors.New("notes: a live put needs a box")
		}
		// Compacted into our own bytes: the caller's frame buffer is not ours to keep, and the form on disk,
		// in memory and in a reply is then one.
		var buf bytes.Buffer
		if err := json.Compact(&buf, sealed); err != nil {
			return Record{}, fmt.Errorf("notes: the box is not valid JSON: %w", err)
		}
		box = buf.Bytes()
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	l := s.led[account]
	var cur Record
	exists := false
	held := 0
	if l != nil {
		cur, exists = l.records[noteID]
		held = len(l.records)
	}

	// The conflict rule (item FJ): the base is the stored revision, or 0 for an id the store does not hold.
	accepted := (exists && baseRevision == cur.Revision) || (!exists && baseRevision == 0)
	if !accepted {
		var current *Record
		if exists {
			c := cur
			current = &c
		}
		return Record{}, &ConflictError{Current: current}
	}
	// The limits (item FL).
	if !exists && !deleted && (zeroCountLimit || maxRecords > 0) && held >= maxRecords {
		return Record{}, ErrLimitReached
	}
	if maxBytes >= 0 && !deleted {
		var total int64
		if l != nil {
			for _, r := range l.records {
				total += int64(len(r.Sealed))
			}
		}
		delta := int64(len(box)) - int64(len(cur.Sealed))
		if delta > 0 && (delta > maxBytes || total > maxBytes-delta) {
			return Record{}, ErrLimitReached
		}
	}
	// The delivery bound of item FX, the last limit (DEVICE-EXCHANGE 1.17, ticket L0322): a record the
	// server could not deliver is never stored, whatever the count and the bytes allow.
	if !deliverable {
		return Record{}, ErrCapacity
	}

	created := l == nil
	if created {
		l = &ledger{records: map[string]Record{}}
		s.led[account] = l
	}
	prevRevision := l.revision
	rec := Record{
		NoteID:         noteID,
		Revision:       l.revision + 1,
		StoredAt:       s.now().UTC().Format(time.RFC3339),
		SourceDeviceID: sourceDeviceID,
		Deleted:        deleted,
		Sealed:         box, // nil for a delete: a tombstone has no box
	}
	l.revision = rec.Revision
	l.records[noteID] = rec
	if err := s.writeLocked(); err != nil {
		l.revision = prevRevision
		if exists {
			l.records[noteID] = cur
		} else {
			delete(l.records, noteID)
		}
		if created {
			delete(s.led, account)
		}
		return Record{}, err
	}
	return rec, nil
}

// State is what item FL's `notes_changes` answer reports besides the records: the account's counter (its
// `notesRevision`: the highest revision ever assigned, 0 for an account that never held a record), its horizon,
// and the number of records it holds now, tombstones included.
func (s *Store) State(account string) (revision, horizon int64, count int) {
	s.mu.Lock()
	defer s.mu.Unlock()
	l := s.led[account]
	if l == nil {
		return 0, 0, 0
	}
	return l.revision, l.horizon, len(l.records)
}

// Above returns the records of an account whose revision is above since, ascending by revision. The slice is
// never nil and is the caller's own; the boxes in it are shared (see Record).
func (s *Store) Above(account string, since int64) []Record {
	s.mu.Lock()
	defer s.mu.Unlock()
	l := s.led[account]
	if l == nil {
		return []Record{}
	}
	out := make([]Record, 0, len(l.records))
	for _, r := range l.records {
		if r.Revision > since {
			out = append(out, r)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Revision < out[j].Revision })
	return out
}

// All returns every record of an account, ascending by revision: what a `full` answer lists. Revisions start
// at 1, so it is Above(account, 0).
func (s *Store) All(account string) []Record {
	return s.Above(account, 0)
}

// EraseRecords removes every record of an account that CONTINUES to exist (item AO: its kind becomes guest, and
// it may become a member again), with one write, and reports how many records it removed. The entry stays: the
// counter is kept, because a revision never comes back (item FG) and a device that synchronised up to revision 50
// must not meet new records numbered 1, 2, 3 later; and the horizon is set to the counter, so a device whose
// `since` is older is answered `full` with an empty list and treats what it holds as gone. An account the store
// does not hold, one with counter 0, or one already in that state is a no-op: 0, nil and nothing written. A write
// that fails leaves the account as it was. Erase is the whole-entry removal of an account that is gone.
func (s *Store) EraseRecords(account string) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	l := s.led[account]
	if l == nil || l.revision == 0 || (len(l.records) == 0 && l.horizon >= l.revision) {
		return 0, nil
	}
	prevRecords, prevHorizon := l.records, l.horizon
	l.records = map[string]Record{}
	if l.horizon < l.revision {
		l.horizon = l.revision
	}
	if err := s.writeLocked(); err != nil {
		l.records, l.horizon = prevRecords, prevHorizon
		return 0, err
	}
	return len(prevRecords), nil
}

// Erase removes the whole entry of an account - its records, its counter and its horizon - with one write
// (item AU: erased with the account), and reports how many records it removed. A write that fails leaves the
// account as it was. An account that goes on (its kind becomes guest) uses EraseRecords instead, which keeps the
// counter.
func (s *Store) Erase(account string) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	l := s.led[account]
	if l == nil {
		return 0, nil
	}
	delete(s.led, account)
	if err := s.writeLocked(); err != nil {
		s.led[account] = l
		return 0, err
	}
	return len(l.records), nil
}

// Count is the number of records of every account together, for a log line (a count, never a list).
func (s *Store) Count() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	n := 0
	for _, l := range s.led {
		n += len(l.records)
	}
	return n
}

// Accounts is the number of accounts that have an entry in the store.
func (s *Store) Accounts() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.led)
}

// PurgeTombstones is the sweep of item FG: it removes every tombstone whose storedAt is more than TombstoneDays
// before now, raises each account's horizon to the highest revision it purged there, and returns how many
// tombstones it removed. A live record is never purged, the counter never moves, and the file is written only
// when something was purged; a write that fails leaves every account as it was.
func (s *Store) PurgeTombstones(now time.Time) (int, error) {
	cutoff := now.UTC().Add(-TombstoneDays * 24 * time.Hour)

	s.mu.Lock()
	defer s.mu.Unlock()

	type undo struct {
		l       *ledger
		horizon int64
		removed []Record
	}
	var undos []undo
	purged := 0
	for _, l := range s.led {
		var removed []Record
		for id, r := range l.records {
			if !r.Deleted {
				continue
			}
			at, err := time.Parse(time.RFC3339, r.StoredAt)
			if err != nil || !at.Before(cutoff) {
				continue // an age that cannot be read is never a reason to drop a record
			}
			removed = append(removed, r)
			delete(l.records, id)
		}
		if len(removed) == 0 {
			continue
		}
		u := undo{l: l, horizon: l.horizon, removed: removed}
		for _, r := range removed {
			if r.Revision > l.horizon {
				l.horizon = r.Revision
			}
		}
		undos = append(undos, u)
		purged += len(removed)
	}
	if purged == 0 {
		return 0, nil
	}
	if err := s.writeLocked(); err != nil {
		for _, u := range undos {
			u.l.horizon = u.horizon
			for _, r := range u.removed {
				u.l.records[r.NoteID] = r
			}
		}
		return 0, err
	}
	return purged, nil
}

// writeLocked writes the whole store through a temp file and a rename, so a kill leaves the previous file whole.
// The output is deterministic: accounts by login, records by revision.
func (s *Store) writeLocked() error {
	f := fileShape{StoreVersion: StoreVersion, Accounts: make([]accountFile, 0, len(s.led))}
	for login, l := range s.led {
		recs := make([]Record, 0, len(l.records)) // never nil: an empty account writes [] and not null
		for _, r := range l.records {
			recs = append(recs, r)
		}
		sort.Slice(recs, func(i, j int) bool { return recs[i].Revision < recs[j].Revision })
		f.Accounts = append(f.Accounts, accountFile{Account: login, Revision: l.revision, Horizon: l.horizon, Records: recs})
	}
	sort.Slice(f.Accounts, func(i, j int) bool { return f.Accounts[i].Account < f.Accounts[j].Account })

	// Not json.MarshalIndent: its default escapes < > & inside a box, and a box is kept as it came.
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	enc.SetEscapeHTML(false)
	enc.SetIndent("", "  ")
	if err := enc.Encode(f); err != nil {
		return err
	}
	if err := os.MkdirAll(s.dir, 0o700); err != nil {
		return err
	}
	path := filepath.Join(s.dir, File)
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, buf.Bytes(), 0o600); err != nil {
		// A partial file (disk full) must not stay behind for a backup tool to copy; only a regular file is ours
		// to remove, never a directory of that name.
		if fi, serr := os.Lstat(tmp); serr == nil && fi.Mode().IsRegular() {
			_ = os.Remove(tmp)
		}
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}
