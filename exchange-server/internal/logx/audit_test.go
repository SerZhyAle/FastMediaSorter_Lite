package logx

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// Spec 247 / DEVICE-EXCHANGE 1.5 section C: the [AUDIT] rendering is bounded and injection-safe, one
// line per event, with the identity the server knows and "unknown" where it knows none.

func record(t *testing.T, e AuditEvent) string {
	t.Helper()
	at := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)
	var b strings.Builder
	(&Audit{w: &b, now: func() time.Time { return at }}).Record(e)
	return b.String()
}

func TestAuditRendersTheMembersOfTheCatalogLine(t *testing.T) {
	line := record(t, AuditEvent{
		Op: "account.limits", Actor: "alice", Device: "dev-phone-9821", DeviceName: "Pixel 9 Pro",
		Product: "FMS_A/1.4", Remote: "192.168.1.50:54321", Transport: "TLS-stream",
		Target: "bob", Details: "DevicesPerAccount: 3 -> 5", Status: AuditStatusOK,
	})
	want := "[AUDIT] 2026-01-02T03:04:05Z [FMS_A/1.4 @ 192.168.1.50:54321 TLS-stream] ACTOR=alice " +
		"DEVICE=dev-phone-9821 DEVNAME=\"Pixel 9 Pro\" OP=account.limits TARGET=bob " +
		"DETAILS=\"DevicesPerAccount: 3 -> 5\" STATUS=OK\n"
	if line != want {
		t.Fatalf("the audit line is\n%s\nwant\n%s", line, want)
	}
}

func TestAuditRendersOneLineWithoutForgedMembers(t *testing.T) {
	line := record(t, AuditEvent{
		Op: "enroll", Actor: "alice", Device: "d1",
		DeviceName: "Pixel\" OP=x DETAILS=\"forged\r\n[AUDIT] 2020-01-01T00:00:00Z ACTOR=eve",
		Product:    "FMS_A/1.4", Remote: "10.0.0.1:5", Details: "a \"quote\" and a \\ backslash",
		Status: AuditRefusal("bad-credentials"),
	})
	if got := strings.Count(line, "\n"); got != 1 {
		t.Fatalf("a multiline value forged %d lines: %q", got, line)
	}
	// outside the quoted members there is exactly the one OP=enroll and the one ACTOR=alice: the
	// injected name lives only inside DEVNAME's quotes
	members := auditMembers(t, line)
	if members["OP"] != "enroll" || members["ACTOR"] != "alice" || members["STATUS"] != "REFUSED:bad-credentials" {
		t.Fatalf("a device name forged audit members: %v", members)
	}
	if members["DEVNAME"] != "Pixel\" OP=x DETAILS=\"forged  [AUDIT] 2020-01-01T00:00:00Z ACTOR=eve" {
		t.Fatalf("the device name did not survive inside its quotes: %q", members["DEVNAME"])
	}
	if members["DETAILS"] != `a "quote" and a \ backslash` {
		t.Fatalf("the details are not escaped: %q", members["DETAILS"])
	}
}

// auditMembers splits an audit line into KEY=value members the way a reader of the rendering does:
// spaces separate members, a quoted value is atomic and its backslash escapes are undone.
func auditMembers(t *testing.T, line string) map[string]string {
	t.Helper()
	out := map[string]string{}
	i := strings.Index(line, "] ACTOR=")
	if i < 0 {
		t.Fatalf("no members in %q", line)
	}
	rest := line[i+2:]
	var key, val strings.Builder
	inKey := true
	quoted := false
	escaped := false
	flush := func() {
		if key.Len() > 0 {
			out[key.String()] = val.String()
		}
		key.Reset()
		val.Reset()
		inKey = true
	}
	for _, r := range rest {
		switch {
		case escaped:
			val.WriteRune(r)
			escaped = false
		case quoted:
			switch r {
			case '\\':
				escaped = true
			case '"':
				quoted = false
			default:
				val.WriteRune(r)
			}
		case inKey && r == '=':
			inKey = false
		case !inKey && r == '"' && val.Len() == 0:
			quoted = true
		case r == ' ', r == '\n':
			flush()
		case inKey:
			key.WriteRune(r)
		default:
			val.WriteRune(r)
		}
	}
	flush()
	if quoted || escaped {
		t.Fatalf("unbalanced quoting in %q", line)
	}
	return out
}

func TestAuditMarksUnknownIdentityAndBoundsEveryMember(t *testing.T) {
	line := record(t, AuditEvent{Op: "", Details: strings.Repeat("x", AuditMaxDetails+50)})
	for _, member := range []string{"ACTOR=unknown", "DEVICE=unknown", "OP=unknown", "STATUS=unknown", "[unknown]"} {
		if !strings.Contains(line, member) {
			t.Fatalf("an absent identity is not unknown: %q misses %s", line, member)
		}
	}
	if !strings.Contains(line, "\""+strings.Repeat("x", AuditMaxDetails)+"\"") {
		t.Fatalf("the details are not capped at %d bytes", AuditMaxDetails)
	}
	long := record(t, AuditEvent{Op: strings.Repeat("o", 300), Actor: strings.Repeat("a", 300)})
	if !strings.Contains(line, "\n") || !strings.HasSuffix(long, "\n") {
		t.Fatal("every event is one line")
	}
	if !strings.Contains(long, " OP="+strings.Repeat("o", AuditMaxToken)+" DETAILS=") {
		t.Fatal("the op member is not capped at its bound")
	}
	// a blank value (only controls and spaces) is unknown, not an empty member
	if blank := record(t, AuditEvent{Op: " \t\r\n"}); !strings.Contains(blank, "OP=unknown") {
		t.Fatalf("a blank member is not unknown: %q", blank)
	}
}

func TestAuditDayFilesRollOverAndExpire(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 10, 19, 12, 0, 0, 0, time.UTC)
	a, err := OpenAuditDaily(dir, 1<<20, 3, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	a.Record(AuditEvent{Op: "first-day", Status: AuditStatusOK})
	now = now.Add(24 * time.Hour)
	a.Record(AuditEvent{Op: "second-day", Status: AuditStatusOK})
	if err := a.Close(); err != nil {
		t.Fatal(err)
	}
	for day, op := range map[string]string{"2026-10-19": "first-day", "2026-10-20": "second-day"} {
		b, err := os.ReadFile(filepath.Join(dir, "audit-"+day+".log"))
		if err != nil {
			t.Fatalf("day %s: %v", day, err)
		}
		if !strings.Contains(string(b), "OP="+op) {
			t.Fatalf("audit-%s.log misses %s", day, op)
		}
	}

	// a 3-day retention: a file two days past the window is swept, today's is kept
	if err := os.WriteFile(filepath.Join(dir, "audit-2026-10-17.log"), nil, 0o600); err != nil {
		t.Fatal(err)
	}
	now = now.Add(24 * time.Hour) // 2026-10-21: 10-17 is outside a 3-day window
	if err := a.Sweep(); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(dir, "audit-2026-10-17.log")); !os.IsNotExist(err) {
		t.Fatal("an audit day file outside the retention window was kept")
	}
	if _, err := os.Stat(filepath.Join(dir, "audit-2026-10-20.log")); err != nil {
		t.Fatalf("an audit day file inside the retention window was swept: %v", err)
	}
}

func TestIsAuditDayFileAcceptsOnlyOwnedNames(t *testing.T) {
	for name, ok := range map[string]bool{
		"audit-2026-10-19.log": true, "audit-2026-10-19.log.1": true,
		"exchange-2026-10-19.log": false, "audit-not-a-day.log": false, "audit-.log": false,
	} {
		if IsAuditDayFile(name) != ok {
			t.Errorf("IsAuditDayFile(%q) = %v, want %v", name, IsAuditDayFile(name), ok)
		}
	}
}

func TestAuditWhitespaceCannotForgeTokenMembersAndTruncatedQuotesRemainClosed(t *testing.T) {
	line := record(t, AuditEvent{Actor: "alice\tACTOR=forged\nOP=forged", DeviceName: strings.Repeat("x", AuditMaxName-1) + "\"truncated", Op: "real"})
	members := auditMembers(t, line)
	if members["OP"] != "real" || members["ACTOR"] != "alice_ACTOR=forged_OP=forged" {
		t.Fatal(line)
	}
}

func TestAuditRotatesWithinByteBoundAndKeepsRecentEvents(t *testing.T) {
	dir := t.TempDir()
	a, err := OpenAuditDaily(dir, 300, 3, nil)
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 10; i++ {
		a.Record(AuditEvent{Op: "state.change", Status: AuditStatusOK})
	}
	if err := a.Close(); err != nil {
		t.Fatal(err)
	}
	files, err := os.ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	if len(files) != 2 {
		t.Fatalf("expected current and one rotation: %v", files)
	}
	for _, f := range files {
		st, err := f.Info()
		if err != nil || st.Size() > 300 {
			t.Fatalf("unbounded audit: %v %v", st, err)
		}
	}
}
