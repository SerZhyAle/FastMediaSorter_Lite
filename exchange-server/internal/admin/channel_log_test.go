package admin

import (
	"strings"
	"testing"
)

// The local channel's log rule: a read that answers writes no line (the Share Manager polls reads
// every five seconds while its panel is unfolded, and an idle day must not fill the log with
// identical poll entries), a failed read writes one with the answer's code, and a mutation is
// logged even when the server refuses it.
func TestTheChannelLogsReadsByFailureOnly(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)

	if r, _ := call(Request{Op: OpStatus}); !r.OK {
		t.Fatalf("%+v", r)
	}
	if r, _ := call(Request{Op: OpAccountList}); !r.OK {
		t.Fatalf("%+v", r)
	}
	if r, _ := call(Request{Op: OpAdminAuditList}); !r.OK {
		t.Fatalf("%+v", r)
	}
	if r, _ := call(Request{Op: OpDeviceList, Login: "nobody"}); r.OK || r.Code != CodeNoAccount {
		t.Fatalf("a read of a missing account: %+v", r)
	}
	if r, _ := call(Request{Op: OpAccountAdd, Login: "alice", Password: "weak"}); r.OK || r.Code != CodeWeak {
		t.Fatalf("a refused mutation: %+v", r)
	}

	requests, failedRead, refusedMutation := 0, false, false
	for _, line := range log.Lines() {
		if !strings.Contains(line, "admin-request") {
			continue
		}
		requests++
		switch {
		case strings.Contains(line, "op=device.list") && strings.Contains(line, "code=no-account"):
			failedRead = true
		case strings.Contains(line, "op=account.add"):
			refusedMutation = true
		}
	}
	if requests != 2 || !failedRead || !refusedMutation {
		t.Fatalf("log lines: %d (failedRead=%v refusedMutation=%v): %q", requests, failedRead, refusedMutation, log.Lines())
	}
}
