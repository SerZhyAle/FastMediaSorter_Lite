package rendezvous_test

import (
	"slices"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

func TestFullContinuationBelowHorizon(t *testing.T) {
	clock := newClock()
	o := notesOpts("alice")
	o.Now = clock.Now
	env := testkit.StartServer(t, o)
	d, c := ntDevice(t, env, "alice")
	for _, id := range []string{ntA, ntB, ntC} {
		ntStored(t, ntPut(t, c, id, 0, ntSized(150000, "")))
	}
	ntStored(t, ntPut(t, c, ntD, 0, ntBox("purged")))
	ntStored(t, ntDelete(t, c, ntD, 4))
	c.Close() // a clock that jumps expires every keepalive window: the account's streams are closed before it moves
	ntAgeTombstones(env, clock)
	c, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	first := ntList(t, c, 0)
	if !first.Full || !first.More || !slices.Equal(ntRevs(first), []int64{1}) || first.NotesHorizon != 5 {
		t.Fatalf("first page: %+v", first)
	}
	c.Close()
	c, err = d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	for _, want := range []int64{2, 3} {
		ans, err := c.Request(&wire.NotesList{Since: ntRev(want - 1), Full: true}, 5*time.Second, wire.TypeNotesChanges, wire.TypeRefused)
		if err != nil {
			t.Fatal(err)
		}
		page, ok := ans.(*wire.NotesChanges)
		if !ok || !page.Full || page.More != (want == 2) || !slices.Equal(ntRevs(page), []int64{want}) {
			t.Fatalf("continuation %d: %+v", want, ans)
		}
	}
}
