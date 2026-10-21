package directory

import (
	"sync"
	"testing"
)

// Ticket 244 (devex-delta G3): the operator's broadcast list carries the listeners of each broadcast, asked
// of the host that owns the relay and the tunnels, never invented and never asked under the directory's lock.

func TestBroadcastListCountsListenersFromTheHostAndZeroWithoutOne(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil); err != nil {
		t.Fatal(err)
	}
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB2, ""), nil); err != nil {
		t.Fatal(err)
	}

	// no host function installed: nothing is known, so every count is 0 and nothing fails
	list := e.d.ListBroadcasts(alice, "")
	if len(list) != 2 || list[0].ListenersCount != 0 || list[1].ListenersCount != 0 {
		t.Fatalf("without a host the count is 0: %+v", list)
	}

	var mu sync.Mutex
	counts := map[string]int{broB1: 3}
	e.d.SetListeners(func(id string) int {
		// a call under the directory's lock would deadlock here: the directory is asked again
		_ = e.d.Counts()
		mu.Lock()
		defer mu.Unlock()
		return counts[id]
	})
	list = e.d.ListBroadcasts(alice, "")
	if len(list) != 2 || list[0].BroadcastID != broB1 || list[0].ListenersCount != 3 || list[1].ListenersCount != 0 {
		t.Fatalf("three listeners on the first, none on the second: %+v", list)
	}
	if got := e.d.ListBroadcasts(alice, devA2); len(got) != 0 {
		t.Fatalf("the device filter still applies: %+v", got)
	}

	// the count follows the host from one call to the next
	mu.Lock()
	counts[broB1] = 0
	counts[broB2] = 7
	mu.Unlock()
	list = e.d.ListBroadcasts(alice, devA1)
	if list[0].ListenersCount != 0 || list[1].ListenersCount != 7 {
		t.Fatalf("the count is read afresh: %+v", list)
	}

	// a host that reports a nonsense negative number never reaches the operator as one
	e.d.SetListeners(func(string) int { return -4 })
	if got := e.d.ListBroadcasts(alice, ""); got[0].ListenersCount != 0 {
		t.Fatalf("a negative count is not shown: %+v", got)
	}

	// an ended broadcast is gone from the list; the host is never asked about it and nothing panics
	asked := false
	e.d.SetListeners(func(string) int { asked = true; return 5 })
	if err := e.d.EndBroadcast(alice, broB1); err != nil {
		t.Fatal(err)
	}
	if err := e.d.EndBroadcast(alice, broB2); err != nil {
		t.Fatal(err)
	}
	if got := e.d.ListBroadcasts(alice, ""); len(got) != 0 || asked {
		t.Fatalf("no broadcast, no question to the host: %+v asked=%v", got, asked)
	}

	// the field is a plain member and the bitrate stays absent: the relay measures no rate
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil); err != nil {
		t.Fatal(err)
	}
	if got := e.d.ListBroadcasts(alice, ""); len(got) != 1 || got[0].BitrateEstimateKbps != 0 || got[0].ListenersCount != 5 {
		t.Fatalf("%+v", got)
	}
}
