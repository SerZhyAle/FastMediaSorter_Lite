package main

import (
	"encoding/json"
	"strings"
	"testing"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/password"
)

// initWithEndpoint makes the identity and sets the address clients use, so an invitation string can be built.
func initWithEndpoint(t *testing.T) string {
	t.Helper()
	p := cfgPath(t)
	if code, _, e := execute(t, []string{"inspect", "--init", "--config", p}, ""); code != 0 {
		t.Fatalf("init: %d %s", code, e)
	}
	if code, _, e := execute(t, []string{"set-endpoint", "--config", p, "203.0.113.43:22022"}, ""); code != 0 {
		t.Fatalf("set-endpoint: %d %s", code, e)
	}
	return p
}

// Ticket 224 rule 1: a created invitation is shown once - its code and the string that carries it.
func TestCLIInvitations(t *testing.T) {
	p := initWithEndpoint(t)
	code, out, errText := execute(t, []string{"invitation", "create", "--config", p, "--kind", "member", "--expect-login", "carol", "--name", "Home"}, "")
	lines := strings.Split(strings.TrimSpace(out), "\n")
	if code != 0 || errText != "" || len(lines) != 2 {
		t.Fatalf("invitation create: exit %d %q %q", code, out, errText)
	}
	invCode := lines[0]
	if len(invCode) != 22 {
		t.Fatalf("expected a 22-character code, got %q", invCode)
	}
	conn, err := connstr.Parse(lines[1])
	if err != nil || conn.Invitation != invCode || conn.Login != "carol" || conn.Name != "Home" || conn.Fingerprint != fingerprintOf(t, p) {
		t.Fatalf("the invitation string: %v %+v", err, conn)
	}

	code, outGuest, _ := execute(t, []string{"invitation", "create", "--config", p, "--kind", "guest"}, "")
	if code != 0 {
		t.Fatalf("invitation create guest: %d", code)
	}
	guestCode := strings.SplitN(strings.TrimSpace(outGuest), "\n", 2)[0]

	// the list never carries a code or the string
	code, listOut, _ := execute(t, []string{"invitation", "list", "--config", p}, "")
	if code != 0 || !strings.Contains(listOut, "member") || !strings.Contains(listOut, "guest") || !strings.Contains(listOut, "unused") ||
		!strings.Contains(listOut, "expects carol") {
		t.Fatalf("invitation list: %d %q", code, listOut)
	}
	for _, secret := range []string{invCode, guestCode, lines[1]} {
		if strings.Contains(listOut, secret) {
			t.Fatalf("invitation list leaked a secret: %q", listOut)
		}
	}

	code, jsonOut, _ := execute(t, []string{"invitation", "list", "--json", "--config", p}, "")
	var invs []admin.InvitationView
	if code != 0 || json.Unmarshal([]byte(jsonOut), &invs) != nil || len(invs) != 2 {
		t.Fatalf("invitation list json: %d %q", code, jsonOut)
	}
	if strings.Contains(jsonOut, invCode) || strings.Contains(jsonOut, guestCode) {
		t.Fatalf("the json list leaked a code: %q", jsonOut)
	}

	prefix := invs[0].CodeHash[:8]
	code, withdrawOut, _ := execute(t, []string{"invitation", "withdraw", prefix, "--config", p}, "")
	if code != 0 || !strings.Contains(withdrawOut, "withdrawn") {
		t.Fatalf("withdraw: %d %q", code, withdrawOut)
	}
	code, jsonOut, _ = execute(t, []string{"invitation", "list", "--json", "--config", p}, "")
	invs = nil
	if code != 0 || json.Unmarshal([]byte(jsonOut), &invs) != nil || len(invs) != 1 || strings.HasPrefix(invs[0].CodeHash, prefix) {
		t.Fatalf("the withdrawn invitation is still listed: %q", jsonOut)
	}
	if code, _, _ := execute(t, []string{"invitation", "withdraw", prefix, "--config", p}, ""); code == 0 {
		t.Fatal("withdrawing the same invitation twice must fail")
	}
}

// With no public endpoint the code is still shown, and the reason the string is not made goes to stderr.
func TestCLIInvitationWithNoEndpointShowsTheCodeAndSaysWhy(t *testing.T) {
	p := cfgPath(t)
	code, out, errText := execute(t, []string{"invitation", "create", "--config", p}, "")
	if code != 0 || len(strings.TrimSpace(out)) != 22 || strings.TrimSpace(errText) == "" {
		t.Fatalf("exit %d stdout %q stderr %q", code, out, errText)
	}
}

// Rule 2: the shared invitation password is off by default, never shown again and not in the status.
func TestCLISharedInvitation(t *testing.T) {
	p := cfgPath(t)
	code, out, _ := execute(t, []string{"shared-invitation", "status", "--config", p}, "")
	if code != 0 || strings.TrimSpace(out) != "off" {
		t.Fatalf("status initial: %d %q", code, out)
	}

	code, genPW, _ := execute(t, []string{"shared-invitation", "set", "--generate", "--kind", "guest", "--config", p}, "")
	if code != 0 {
		t.Fatalf("set shared invitation: %d", code)
	}
	pw := strings.TrimSpace(genPW)
	if password.Check(pw) != nil {
		t.Fatalf("generated an invalid password: %q", pw)
	}

	code, out, _ = execute(t, []string{"shared-invitation", "status", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "on\tguest") || strings.Contains(out, pw) {
		t.Fatalf("status on: %d %q", code, out)
	}
	code, out, _ = execute(t, []string{"shared-invitation", "status", "--json", "--config", p}, "")
	var v admin.SharedInvitationView
	if code != 0 || json.Unmarshal([]byte(out), &v) != nil || !v.Enabled || v.Kind != "guest" || strings.Contains(out, pw) {
		t.Fatalf("status json: %d %q", code, out)
	}

	code, out, _ = execute(t, []string{"shared-invitation", "off", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "off") {
		t.Fatalf("turn off: %d %q", code, out)
	}
	code, out, _ = execute(t, []string{"shared-invitation", "status", "--config", p}, "")
	if code != 0 || strings.TrimSpace(out) != "off" {
		t.Fatalf("status after off: %d %q", code, out)
	}
}

// Rule 4: limits of one account, and the operator is told - never refused - when the cap is passed.
func TestCLIAccountLimitsAndWarning(t *testing.T) {
	p := cfgPath(t)
	if code, _, _ := execute(t, []string{"account", "add", "alice", "--generate", "--config", p}, ""); code != 0 {
		t.Fatalf("account add: %d", code)
	}
	code, out, _ := execute(t, []string{"account", "limits", "alice", "--devices", "5", "--resources", "20", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "updated") {
		t.Fatalf("limits: %d %q", code, out)
	}
	code, jsonOut, _ := execute(t, []string{"account", "list", "--json", "--config", p}, "")
	var accts []admin.AccountView
	if code != 0 || json.Unmarshal([]byte(jsonOut), &accts) != nil || len(accts) != 1 {
		t.Fatalf("account list json: %d %q", code, jsonOut)
	}
	if accts[0].Limits == nil || *accts[0].Limits.DevicesPerAccount != 5 || *accts[0].Limits.ResourcesPerAccount != 20 {
		t.Fatalf("limits not set: %+v", accts[0].Limits)
	}

	cfg, _ := config.Load(p)
	cfg.Limits.MaxAccounts = 1
	_ = config.Save(p, cfg)
	code, _, stderr := execute(t, []string{"account", "add", "bobby", "--generate", "--config", p}, "")
	if code != 0 {
		t.Fatalf("the operator may add past the cap: %d %s", code, stderr)
	}
	if !strings.Contains(stderr, "warning:") || !strings.Contains(stderr, "cap is 1") {
		t.Fatalf("expected the warning on stderr, got: %q", stderr)
	}
}

// Rule 3: a grant names something the member holds RIGHT NOW; with the server stopped nothing is held,
// so the command refuses by name and a listing is empty.
func TestCLIGrants(t *testing.T) {
	p := cfgPath(t)
	execute(t, []string{"account", "add", "host1", "--generate", "--config", p}, "")
	execute(t, []string{"account", "add", "guest1", "--generate", "--kind", "guest", "--config", p}, "")

	code, _, errText := execute(t, []string{"grant", "add", "host1", "guest1", "AAAAAAAAAAAAAAAAAAAAAA", "--config", p}, "")
	if code == 0 || strings.TrimSpace(errText) == "" {
		t.Fatalf("a grant of what is not held must be refused: %d %q", code, errText)
	}
	code, _, _ = execute(t, []string{"grant", "add", "host1", "nobody", "AAAAAAAAAAAAAAAAAAAAAA", "--config", p}, "")
	if code == 0 {
		t.Fatal("a grant to an unknown account must be refused")
	}
	code, out, _ := execute(t, []string{"grant", "list", "guest1", "--json", "--config", p}, "")
	var got map[string][]admin.GrantView
	if code != 0 || json.Unmarshal([]byte(out), &got) != nil || len(got["received"]) != 0 || len(got["given"]) != 0 {
		t.Fatalf("grant list: %d %q", code, out)
	}
}
