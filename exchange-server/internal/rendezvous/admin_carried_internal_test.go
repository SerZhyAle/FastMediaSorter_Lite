package rendezvous

import (
	"sort"
	"testing"
)

// The remote wire carries exactly the operations WEB-BINDING operator-administration "Which operations" lists
// (REMOTE-ADMIN), spelled out here as literals so that adding an operation to carriedOps is a decision made in
// this list, with the contract, and never a side effect of adding it to internal/admin (audit gap G-14).
func TestCarriedOperationsAreExactlyTheEighteenOfTheContract(t *testing.T) {
	want := []string{
		"account.add", "account.passwd", "account.remove", "account.list", "account.kind", "account.limits",
		"device.list", "device.revoke", "device.pairing-code",
		"invitation.create", "invitation.list", "invitation.withdraw",
		"shared_invitation.status", "shared_invitation.set",
		"policy.status", "policy.set", "grant.list", "status",
	}
	var got []string
	for op, carried := range carriedOps {
		if carried {
			got = append(got, op)
		}
	}
	sort.Strings(got)
	sort.Strings(want)
	if len(want) != 18 {
		t.Fatalf("the contract list in this test has %d entries, want 18", len(want))
	}
	if len(got) != len(want) {
		t.Fatalf("carriedOps has %d operations, want %d: %v", len(got), len(want), got)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("carriedOps differs from the contract at %d: %q, want %q (all: %v)", i, got[i], want[i], got)
		}
	}
}
