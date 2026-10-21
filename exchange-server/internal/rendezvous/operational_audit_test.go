package rendezvous_test

import (
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

type auditCapture struct {
	mu   sync.Mutex
	text strings.Builder
}

func (b *auditCapture) Write(p []byte) (int, error) {
	b.mu.Lock()
	defer b.mu.Unlock()
	return b.text.Write(p)
}
func (b *auditCapture) String() string { b.mu.Lock(); defer b.mu.Unlock(); return b.text.String() }

func TestOperationalAuditAuthenticatesIdentityAndRecordsDeviceLifecycle(t *testing.T) {
	var capture auditCapture
	opts := looseOpts("alice")
	opts.Audit = logx.NewAudit(&capture)
	env := testkit.StartServer(t, opts)
	p := env.NewProducer("alice", "unused")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	id, err := p.StartBroadcast("AUDIO_ONLY", nil)
	if err != nil {
		t.Fatal(err)
	}
	if err = p.EndBroadcast(id); err != nil {
		t.Fatal(err)
	}
	p.Close()
	eventually(t, "device disconnect audit", func() bool {
		return strings.Contains(capture.String(), "OP=device.disconnect") && strings.Contains(capture.String(), "OP=connection.close")
	})
	for _, want := range []string{"ACTOR=alice", "OP=device.enroll", "OP=device.connect", "OP=broadcast.start", "OP=broadcast.end", "OP=connection.open", "OP=connection.close", "TLS-stream"} {
		if !strings.Contains(capture.String(), want) {
			t.Fatalf("missing %s in %s", want, capture.String())
		}
	}
}

func TestOperationalAuditAdminSignInSuccessRefusalAndDisconnect(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", testkit.Cheap())
	if err != nil {
		t.Fatal(err)
	}
	var capture auditCapture
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash}, func(c *rendezvous.Config) { c.Ops.AuditLog = logx.NewAudit(&capture) })
	bad := env.tls(t)
	if err := bad.Send(&wire.AdminAuth{Password: "PrivateWrong1"}); err != nil {
		t.Fatal(err)
	}
	if _, err := bad.Recv(time.Second); err != nil {
		t.Fatal(err)
	}
	bad.Close()
	good := env.tls(t)
	if err := good.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	if _, err := good.Recv(time.Second); err != nil {
		t.Fatal(err)
	}
	good.Close()
	eventually(t, "admin lifecycle audit", func() bool {
		return strings.Contains(capture.String(), "OP=admin.disconnect") && strings.Contains(capture.String(), "REFUSED:bad-credentials")
	})
	for _, secret := range []string{"SecretAdmin1", "PrivateWrong1", hash} {
		if strings.Contains(capture.String(), secret) {
			t.Fatal("audit leaked credentials")
		}
	}
	if !strings.Contains(capture.String(), "ACTOR=unknown") || !strings.Contains(capture.String(), "ACTOR=OPERATOR") {
		t.Fatal(capture.String())
	}
}
