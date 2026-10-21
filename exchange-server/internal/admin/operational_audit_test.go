package admin

import (
	"strings"
	"testing"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/logx"
)

func TestOperationalAuditRecordsSafeDeltasRefusalsAndNoSecrets(t *testing.T) {
	o, _ := newOps(t)
	var lines strings.Builder
	o.AuditLog = logx.NewAudit(&lines)
	ctx := logx.AuditEvent{Remote: "127.0.0.1:54321", Transport: "TLS-stream"}
	if !o.HandleAudited(Request{Op: OpAccountAdd, Login: "alice", Password: pw1}, ctx).OK {
		t.Fatal("add failed")
	}
	kind := o.HandleAudited(Request{Op: OpAccountKind, Login: "alice", Kind: accounts.KindGuest}, ctx)
	if !kind.OK {
		t.Fatal(kind.Error)
	}
	if !o.HandleAudited(Request{Op: OpAccountPasswd, Login: "alice", Password: "OtherPass1"}, ctx).OK {
		t.Fatal("password failed")
	}
	if o.HandleAudited(Request{Op: OpAccountPasswd, Login: "missing", Password: "SecretNotStored1"}, ctx).OK {
		t.Fatal("missing account accepted")
	}
	for _, want := range []string{"ACTOR=OPERATOR", "127.0.0.1:54321", "kind=member -> kind=guest", "password changed", "STATUS=REFUSED:"} {
		if !strings.Contains(lines.String(), want) {
			t.Fatalf("missing %q in %s", want, lines.String())
		}
	}
	for _, secret := range []string{pw1, "OtherPass1", "SecretNotStored1"} {
		if strings.Contains(lines.String(), secret) {
			t.Fatal("audit leaked a secret")
		}
	}
}

func TestEveryCarriedMutationHasOperationalAudit(t *testing.T) {
	o, _ := newOps(t)
	var lines strings.Builder
	o.AuditLog = logx.NewAudit(&lines)
	for _, op := range []string{OpAccountAdd, OpAccountPasswd, OpAccountKind, OpAccountRemove, OpAccountLimits,
		OpDeviceRevoke, OpDevicePairingCode, OpInvitationCreate, OpInvitationWithdraw, OpSharedInvitationSet,
		OpPolicySet, OpBroadcastEnd, OpResourceUnpublish, OpClipboardErase, OpHostSettings} {
		lines.Reset()
		o.HandleAudited(Request{Op: op}, logx.AuditEvent{Transport: "NamedPipe"})
		if !strings.Contains(lines.String(), " OP="+op+" ") {
			t.Fatalf("missing mutation %s", op)
		}
		if strings.Count(lines.String(), "\n") != 1 {
			t.Fatalf("not exactly one record for %s", op)
		}
	}
}
