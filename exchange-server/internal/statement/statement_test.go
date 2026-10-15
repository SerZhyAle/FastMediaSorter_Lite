package statement

import (
	"strings"
	"testing"
)

// The statement is a promise to an operator, so each fact it must carry is a test (DEVICE-EXCHANGE
// 7.9, ticket 167 rule 8).
func TestTheStatementSaysWhatTheContractSaysTheOperatorCanSee(t *testing.T) {
	must := map[string][]string{
		"relay-visible":          {"relay", "operator", "in the clear", "never written to disk"},
		"tunnel-broadcast-plain": {"tunnel", "RTSP", "HTTP", "operator"},
		"access-visible":         {"access details", "memory", "operator", "never written to disk", "never logged"},
		"sftp-opaque":            {"SSH", "cannot read"},
		"multi-user-limit":       {"other people's accounts", "limit", "end to end", "open"},
		"flat-trust":             {"every other device", "remove", "pairing codes", "access details", "remove itself"},
		// ticket 229 (22 items BU and CC): the operator sees a clipboard value in the clear, in
		// memory only, and it is one per account within its limit
		"clipboard-visible": {"memory", "nowhere else", "never written to disk", "lost when the server restarts",
			"operator can read it in the clear", "one value", "size limit"},
	}
	if len(Points) != len(must) {
		t.Fatalf("the statement has %d points, the test knows %d", len(Points), len(must))
	}
	seen := map[string]bool{}
	for _, p := range Points {
		keys, ok := must[p.ID]
		if !ok {
			t.Fatalf("an unknown point %q: add what it must say", p.ID)
		}
		if seen[p.ID] {
			t.Fatalf("a duplicate id %q", p.ID)
		}
		seen[p.ID] = true
		for _, k := range keys {
			if !strings.Contains(p.Text, k) {
				t.Errorf("point %s does not say %q: %s", p.ID, k, p.Text)
			}
		}
	}
}

// House text style of anything a person reads: no ellipsis, no em dash, one sentence-sized fact each.
func TestTheStatementIsPlainText(t *testing.T) {
	for _, p := range Points {
		if strings.Contains(p.Text, "...") || strings.ContainsRune(p.Text, '—') || strings.ContainsRune(p.Text, '…') {
			t.Errorf("%s: house text style (no `...`, no em dash): %s", p.ID, p.Text)
		}
		if len(p.Text) > 500 || p.ID == "" || strings.ContainsAny(p.ID, " ABCDEFGHIJKLMNOPQRSTUVWXYZ") {
			t.Errorf("%s: a short text under a kebab-case id", p.ID)
		}
	}
	if !strings.Contains(Text(), "\n\n") {
		t.Fatal("the paragraphs are separated")
	}
}

func TestRetentionComesFromTheSetting(t *testing.T) {
	if !strings.Contains(WithRetention(7), "7 days") || strings.Contains(WithRetention(7), "31 days") {
		t.Fatal("retention is not configured")
	}
}
