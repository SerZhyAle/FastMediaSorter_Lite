package admin

import (
	"strings"
	"sync"
	"testing"

	"fms/exchange-server/internal/logx"
)

// Ticket 244 (devex-delta G4): the session list and the audit ring of the administration channel name the
// PATH an administration came by, in one place, and the file audit says the same word.

type auditText struct {
	mu sync.Mutex
	b  strings.Builder
}

func (a *auditText) Write(p []byte) (int, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	return a.b.Write(p)
}

func (a *auditText) String() string { a.mu.Lock(); defer a.mu.Unlock(); return a.b.String() }

func TestClientProductWordsAreTheOnesTheShareManagerReads(t *testing.T) {
	// the Share Manager marks a session whose product is not "local" as an external administration and shows
	// the product as it is, so the two words are part of that reading and do not move quietly
	if ClientProductLocal != "local" || ClientProductRemote != "remote" {
		t.Fatalf("local=%q remote=%q", ClientProductLocal, ClientProductRemote)
	}
}

func TestLocalChannelRecordsLocalInTheRingAndInTheFileAudit(t *testing.T) {
	o, log := newOps(t)
	o.Live = true
	o.Audit = NewAuditTracker()
	var capture auditText
	o.AuditLog = logx.NewAudit(&capture)
	ask := serve(t, o, log.Logger)

	if resp, _ := ask(Request{Op: OpAccountAdd, Login: "carol", Password: pw1}); !resp.OK {
		t.Fatalf("account.add: %s", resp.Error)
	}
	var found bool
	for _, r := range o.Audit.View().Records {
		if r.Op == OpAccountAdd && r.Target == "carol" {
			found = true
			if r.ClientProduct != ClientProductLocal {
				t.Fatalf("the ring says %q for the local channel", r.ClientProduct)
			}
		}
	}
	if !found {
		t.Fatalf("account.add is not in the ring: %+v", o.Audit.View().Records)
	}
	line := capture.String()
	if !strings.Contains(line, "["+ClientProductLocal+" @ ") || !strings.Contains(line, "NamedPipe") || strings.Contains(line, "[unknown") {
		t.Fatalf("the file audit of a local change: %q", line)
	}
}
