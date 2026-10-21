package rendezvous_test

import (
	"bufio"
	"crypto/tls"
	"encoding/binary"
	"encoding/json"
	"io"
	"net/http"
	"sync/atomic"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/wire"
)

func setupAdminEnv(t *testing.T, adminCfg config.Admin, mutate ...func(*rendezvous.Config)) (*layerEnv, *admin.Ops) {
	t.Helper()
	dir := t.TempDir()
	store, err := accounts.Open(accounts.Options{
		Dir:    dir,
		Params: password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32},
	})
	if err != nil {
		t.Fatal(err)
	}
	d := directory.New(directory.Options{Dir: dir})
	ops := &admin.Ops{
		Accounts: store,
		Dir:      d,
		Live:     true,
	}
	env := startLayer(t, func(cfg *rendezvous.Config) {
		cfg.AdminConfig = adminCfg
		cfg.PublicEndpoint = "exchange.example.com:22022"
		cfg.LeafFingerprint = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU"
		cfg.Ops = ops
		cfg.Verifier = limits.NewVerifier(nil, 2, 2*time.Second)
		cfg.AdminPacer = store.Pacer()
		for _, f := range mutate {
			f(cfg)
		}
	})
	return env, ops
}

func TestAdminSessionExpiresAtTheInactivityBoundary(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32})
	if err != nil {
		t.Fatal(err)
	}
	var elapsed atomic.Int64
	start := time.Now()
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash, SessionLifetimeMinutes: 1}, func(cfg *rendezvous.Config) {
		cfg.Now = func() time.Time {
			return start.Add(time.Duration(elapsed.Load()))
		}
	})
	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	var token string
	if reply, err := c.Recv(time.Second); err != nil {
		t.Fatal(err)
	} else if ok, valid := reply.(*wire.AdminAuthOK); valid {
		token = ok.Token
	} else {
		t.Fatalf("expected authentication: %T", reply)
	}
	elapsed.Store(int64(time.Minute))
	if err := c.Send(&wire.AdminRequest{ID: 1, Token: token, Op: "status"}); err != nil {
		t.Fatal(err)
	}
	if reply, err := c.Recv(time.Second); err != nil {
		t.Fatal(err)
	} else if refusal, ok := reply.(*wire.Refused); !ok || refusal.Reason != string(wire.ReasonBadCredentials) {
		t.Fatalf("expected expired credentials: %T", reply)
	}
	_, err = c.Recv(time.Second)
	if err == nil {
		t.Fatal("expired stream remained open")
	}
	if timeout, ok := err.(interface{ Timeout() bool }); ok && timeout.Timeout() {
		t.Fatal("client timed out instead of observing server close")
	}
}

func TestAdminStreamOffByDefault(t *testing.T) {
	env, _ := setupAdminEnv(t, config.Admin{On: false})
	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "AnyPassword1"}); err != nil {
		t.Fatal(err)
	}
	envl, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	ref, ok := envl.(*wire.Refused)
	if !ok || ref.Reason != string(wire.ReasonAdminDisabled) {
		t.Fatalf("expected admin-disabled, got %+v", envl)
	}
}

func TestAdminStreamTLSRequired(t *testing.T) {
	hash, _ := password.Hash("SecretAdmin1")
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash})
	c := env.plain(t)
	if err := c.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	envl, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	ref, ok := envl.(*wire.Refused)
	if !ok || ref.Reason != string(wire.ReasonTLSRequired) {
		t.Fatalf("expected tls-required, got %+v", envl)
	}
}

func TestAdminStreamAllowedAddressesExcludes(t *testing.T) {
	hash, _ := password.Hash("SecretAdmin1")
	env, _ := setupAdminEnv(t, config.Admin{
		On:               true,
		PasswordRecord:   hash,
		AllowedAddresses: []string{"10.99.99.99"},
	})
	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	_, err := c.Recv(1 * time.Second)
	if err == nil {
		t.Fatal("expected closed connection with no reply for excluded address")
	}
}

func TestAdminStreamAuthBadCredentials(t *testing.T) {
	hash, _ := password.Hash("SecretAdmin1")
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash})
	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "WrongPassword1"}); err != nil {
		t.Fatal(err)
	}
	envl, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	ref, ok := envl.(*wire.Refused)
	if !ok || ref.Reason != string(wire.ReasonBadCredentials) {
		t.Fatalf("expected bad-credentials, got %+v", envl)
	}
}

func TestAdminStreamSuccessAndCarriedOperations(t *testing.T) {
	hash, _ := password.Hash("SecretAdmin1")
	env, ops := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash, SessionLifetimeMinutes: 15})
	_, _ = ops.AddAccount("alice", "AlicePass123")

	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	envl, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	authOK, ok := envl.(*wire.AdminAuthOK)
	if !ok || authOK.Token == "" || authOK.ExpiresSeconds != 900 {
		t.Fatalf("unexpected admin-auth-ok: %+v", envl)
	}
	c.Authenticated()

	// 1. Carried operation: status
	if err := c.Send(&wire.AdminRequest{ID: 101, Token: authOK.Token, Op: "status"}); err != nil {
		t.Fatal(err)
	}
	rep1, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	adminRep1, ok := rep1.(*wire.AdminReply)
	if !ok || !adminRep1.OK || adminRep1.ID != 101 || len(adminRep1.Data) == 0 {
		t.Fatalf("expected good admin-reply for status: %+v", rep1)
	}

	// 2. Carried operation: invitation.create includes connectionString
	if err := c.Send(&wire.AdminRequest{ID: 102, Token: authOK.Token, Op: "invitation.create", Kind: "member", ExpectedLogin: "bob"}); err != nil {
		t.Fatal(err)
	}
	rep2, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	adminRep2, ok := rep2.(*wire.AdminReply)
	if !ok || !adminRep2.OK || adminRep2.ID != 102 {
		t.Fatalf("expected good admin-reply for invitation.create: %+v", rep2)
	}
	var invRes struct {
		Code             string `json:"code"`
		ConnectionString string `json:"connectionString"`
	}
	if err := json.Unmarshal(adminRep2.Data, &invRes); err != nil {
		t.Fatal(err)
	}
	if invRes.Code == "" || invRes.ConnectionString == "" {
		t.Fatalf("expected code and connectionString in invitation.create reply: %+v", invRes)
	}

	// 3. Uncarried operation: host.settings returns bad-request without closing stream
	if err := c.Send(&wire.AdminRequest{ID: 103, Token: authOK.Token, Op: "host.settings"}); err != nil {
		t.Fatal(err)
	}
	rep3, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	adminRep3, ok := rep3.(*wire.AdminReply)
	if !ok || adminRep3.OK || adminRep3.Code != "bad-request" || adminRep3.ID != 103 {
		t.Fatalf("expected bad-request for host.settings: %+v", rep3)
	}

	// 4. Bad token: refuses and closes stream
	if err := c.Send(&wire.AdminRequest{ID: 104, Token: "wrong-token", Op: "status"}); err != nil {
		t.Fatal(err)
	}
	envl4, err := c.Recv(2 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	ref, ok := envl4.(*wire.Refused)
	if !ok || ref.Reason != string(wire.ReasonBadCredentials) {
		t.Fatalf("expected bad-credentials on bad token, got %+v", envl4)
	}
}

func TestAdminWebSocketOpening(t *testing.T) {
	hash, _ := password.Hash("SecretAdmin1")
	env, _ := setupAdminEnv(t, config.Admin{
		On:             true,
		PasswordRecord: hash,
		Origins:        []string{"https://admin.example.com"},
	})

	conn, err := tls.Dial("tcp", env.addr, &tls.Config{InsecureSkipVerify: true})
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()

	req := "GET /v2/ws HTTP/1.1\r\n" +
		"Host: " + env.addr + "\r\n" +
		"Upgrade: websocket\r\n" +
		"Connection: Upgrade\r\n" +
		"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
		"Sec-WebSocket-Version: 13\r\n" +
		"Sec-WebSocket-Protocol: fms-exchange-v2\r\n" +
		"Origin: https://" + env.addr + "\r\n\r\n"

	if _, err := conn.Write([]byte(req)); err != nil {
		t.Fatal(err)
	}

	resp, err := http.ReadResponse(bufio.NewReader(conn), nil)
	if err != nil {
		t.Fatal(err)
	}
	if resp.StatusCode != 101 {
		t.Fatalf("expected status 101, got %d", resp.StatusCode)
	}
	if resp.Header.Get("Sec-WebSocket-Protocol") != "fms-exchange-v2" {
		t.Fatalf("protocol mismatch: %s", resp.Header.Get("Sec-WebSocket-Protocol"))
	}

	authEnv := &wire.AdminAuth{Password: "SecretAdmin1"}
	frame, err := wire.EncodeFrame(authEnv, wire.MaxFrame)
	if err != nil {
		t.Fatal(err)
	}

	wsFrame := makeClientWSFrame(frame)
	if _, err := conn.Write(wsFrame); err != nil {
		t.Fatal(err)
	}

	replyFrame, err := readServerWSFrame(conn)
	if err != nil {
		t.Fatal(err)
	}
	if len(replyFrame) < 4 {
		t.Fatalf("short reply frame: %d", len(replyFrame))
	}
	decoded, err := wire.Decode(replyFrame[4:])
	if err != nil {
		t.Fatal(err)
	}
	authOK, ok := decoded.(*wire.AdminAuthOK)
	if !ok || authOK.Token == "" {
		t.Fatalf("expected AdminAuthOK over websocket, got %+v", decoded)
	}
}

func makeClientWSFrame(payload []byte) []byte {
	var out []byte
	out = append(out, 0x82) // FIN | Binary
	l := len(payload)
	maskKey := [4]byte{0x12, 0x34, 0x56, 0x78}
	if l < 126 {
		out = append(out, 0x80|byte(l))
	} else if l <= 65535 {
		out = append(out, 0x80|126)
		b := make([]byte, 2)
		binary.BigEndian.PutUint16(b, uint16(l))
		out = append(out, b...)
	} else {
		out = append(out, 0x80|127)
		b := make([]byte, 8)
		binary.BigEndian.PutUint64(b, uint64(l))
		out = append(out, b...)
	}
	out = append(out, maskKey[:]...)
	masked := make([]byte, l)
	for i := 0; i < l; i++ {
		masked[i] = payload[i] ^ maskKey[i%4]
	}
	out = append(out, masked...)
	return out
}

func readServerWSFrame(r io.Reader) ([]byte, error) {
	header := make([]byte, 2)
	if _, err := io.ReadFull(r, header); err != nil {
		return nil, err
	}
	lenByte := int(header[1] & 0x7f)
	var payloadLen int64
	switch lenByte {
	case 126:
		var l uint16
		if err := binary.Read(r, binary.BigEndian, &l); err != nil {
			return nil, err
		}
		payloadLen = int64(l)
	case 127:
		var l uint64
		if err := binary.Read(r, binary.BigEndian, &l); err != nil {
			return nil, err
		}
		payloadLen = int64(l)
	default:
		payloadLen = int64(lenByte)
	}
	payload := make([]byte, payloadLen)
	if _, err := io.ReadFull(r, payload); err != nil {
		return nil, err
	}
	return payload, nil
}
