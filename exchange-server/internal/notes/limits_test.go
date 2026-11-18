package notes

import (
	"errors"
	"testing"
)

func TestNotesCountAndBytesLimits(t *testing.T) {
	s, c, dir := newStore(t)
	small, large := box("a"), box("abcd")
	put := func(id string, base int64, deleted bool, data []byte, count int, bytes int64) (Record, error) {
		return s.PutLimited("alice", "device", id, base, deleted, data, count, bytes, true)
	}
	if _, err := put(idA, 0, false, small, 0, 1000); !errors.Is(err, ErrLimitReached) {
		t.Fatalf("zero count: %v", err)
	}
	r, err := put(idA, 0, false, small, 1, int64(len(small)))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := put(idB, 0, false, small, 1, 1000); !errors.Is(err, ErrLimitReached) {
		t.Fatalf("count limit: %v", err)
	}
	if _, err := put(idA, r.Revision, false, large, 1, int64(len(small))); !errors.Is(err, ErrLimitReached) {
		t.Fatalf("raising edit: %v", err)
	}
	r, err = put(idA, r.Revision, false, small, 0, 0)
	if err != nil {
		t.Fatalf("non-raising edit above lowered limits: %v", err)
	}
	if _, err := put(idA, 0, false, large, 0, 0); err == nil {
		t.Fatal("stale revision accepted")
	} else {
		wantConflict(t, err)
	}
	r, err = put(idA, r.Revision, true, nil, 0, 0)
	if err != nil {
		t.Fatalf("delete above lowered limits: %v", err)
	}
	s = reopen(t, dir, c)
	if got := s.All("alice"); len(got) != 1 || !got[0].Deleted || got[0].Revision != r.Revision {
		t.Fatalf("reopened records: %+v", got)
	}
}

// TestNotesDeliveryBoundIsTheLastLimit (item FX, ticket L0322): a record that could not be carried in one
// delivery frame is refused ErrCapacity and nothing is stored, checked after the conflict and after both
// notes limits, so a request that fails an earlier rule answers with that rule's reason.
func TestNotesDeliveryBoundIsTheLastLimit(t *testing.T) {
	s, _, _ := newStore(t)
	small := box("a")
	if _, err := s.PutLimited("alice", "dev1", idA, 0, false, small, 10, 1000, false); !errors.Is(err, ErrCapacity) {
		t.Fatalf("an undeliverable record: %v, want ErrCapacity", err)
	}
	if got := s.All("alice"); len(got) != 0 {
		t.Fatalf("a refused capacity stored something: %+v", got)
	}
	// the conflict rule and the limits come first: the same undeliverable put with a stale base, above a
	// full account and above a byte limit, answers each of them, and stores nothing either way
	if _, err := s.PutLimited("alice", "dev1", idA, 7, false, small, 10, 1000, false); err == nil {
		t.Fatal("a stale base was accepted")
	} else {
		wantConflict(t, err)
	}
	if _, err := s.PutLimited("alice", "dev1", idA, 0, false, small, 0, 1000, false); !errors.Is(err, ErrLimitReached) {
		t.Fatalf("a full account: %v, want ErrLimitReached", err)
	}
	if _, err := s.PutLimited("alice", "dev1", idA, 0, false, small, 10, 1, false); !errors.Is(err, ErrLimitReached) {
		t.Fatalf("over the bytes: %v, want ErrLimitReached", err)
	}
	if got := s.All("alice"); len(got) != 0 {
		t.Fatalf("a refused limit stored something: %+v", got)
	}
	// a deliverable record of the same shape is stored
	if _, err := s.PutLimited("alice", "dev1", idA, 0, false, small, 10, 1000, true); err != nil {
		t.Fatalf("a deliverable record: %v", err)
	}
	// the account's counter did not move behind the refusals: the stored record takes revision 1
	if got := s.All("alice"); len(got) != 1 || got[0].Revision != 1 {
		t.Fatalf("the record after the refusals: %+v", got)
	}
}
