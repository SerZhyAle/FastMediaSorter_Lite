package rendezvous

import (
	"testing"
	"time"
)

// Ticket 271: the memory of ended ids stays bounded, expiring entries before evicting the oldest.
func TestRelayEndedMemoryEvictsExpiredThenOldest(t *testing.T) {
	clk := &checkClock{t: time.Unix(1000, 0)}
	r := NewRelay(RelayConfig{Now: clk.Now, EndedMemory: time.Hour, EndedMax: 2})
	r.End("first")
	clk.Add(time.Minute)
	r.End("second")
	clk.Add(time.Minute)
	r.End("third")
	if r.EndedIDs() != 2 || r.endedRecently("first") || !r.endedRecently("second") || !r.endedRecently("third") {
		t.Fatal("at the cap only the oldest id must be forgotten")
	}
	clk.Add(59 * time.Minute) // second expires exactly now; third still has one minute left
	r.End("fourth")
	if r.EndedIDs() != 2 || r.endedRecently("second") || !r.endedRecently("third") || !r.endedRecently("fourth") {
		t.Fatal("an expired id must be forgotten before a newer, unexpired id")
	}
	clk.Add(time.Minute)
	if r.endedRecently("third") || r.EndedIDs() != 1 {
		t.Fatal("an id must expire exactly at the memory boundary")
	}
	// A new process has no ended history.
	fresh := NewRelay(RelayConfig{Now: clk.Now})
	if fresh.endedRecently("fourth") || fresh.EndedIDs() != 0 {
		t.Fatal("ended ids must stay in memory only")
	}
}
