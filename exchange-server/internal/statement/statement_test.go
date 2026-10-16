package statement

import (
	"crypto/sha256"
	"encoding/hex"
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

// --- the honest text of a server that seals (27 items DE and DQ, ticket 234) ---

// The contract asks for the honest text "not reworded and not shortened". These are the SHA-256 of the three
// texts as the contract gives them (UTF-8, wrapped lines joined by one space, no quotation marks): a change
// of one letter fails here, and the contract is amended first.
var honestHashes = map[string]string{
	"en": "d2d9b7a1c24132fc14981a92c0e26af746ca2e69bc6db1c2703b2fd3dc5d4e23",
	"ru": "eefbb2fea82105bfad8b376ee625da8f16323272cf32ccd3b5ac7bda2ad75cba",
	"uk": "001811c1bde49d9dbe03ee9e21adc3df3a413ad1ed2b038f40c75c375ed9f29b",
}

func TestTheHonestTextIsTheContractsOwnInEveryLanguageItIsGivenIn(t *testing.T) {
	if strings.Join(HonestLanguages, ",") != "en,ru,uk" {
		t.Fatalf("the contract gives the text in en, ru and uk: %v", HonestLanguages)
	}
	for _, lang := range HonestLanguages {
		text, ok := Honest(lang)
		if !ok {
			t.Fatalf("%s: no text", lang)
		}
		sum := sha256.Sum256([]byte(text))
		if got := hex.EncodeToString(sum[:]); got != honestHashes[lang] {
			t.Errorf("%s: the honest text is not the contract's own (sha256 %s)", lang, got)
		}
		if strings.Contains(text, "...") || strings.ContainsRune(text, '—') || strings.ContainsRune(text, '…') || strings.ContainsAny(text, "\r\n") {
			t.Errorf("%s: one paragraph of plain text", lang)
		}
	}
}

func TestALanguageTheContractDoesNotGiveHasNoHonestTextAndNoneIsInvented(t *testing.T) {
	for _, lang := range []string{"de", "fr", "es", "pl", "ar", "zh", "ja", "ko", "hi", "pt", "it", "tr", "be", "", "EN"} {
		if text, ok := Honest(lang); ok || text != "" {
			t.Errorf("%q: the contract gives no text, so there is none: %q", lang, text)
		}
	}
}

func TestAServerThatAdvertisesSealedSaysTheHonestTextNotThatItsOperatorCanRead(t *testing.T) {
	sealed := []string{"cast", "sealed"}
	if !Sealed(sealed) || Sealed(nil) || Sealed([]string{"cast"}) || Sealed([]string{"Sealed"}) {
		t.Fatal("a server is sealed exactly when its features list `sealed`")
	}
	en, _ := Honest("en")
	got := TextFor(sealed)
	if got != en {
		t.Fatalf("the statement of a sealed server is the honest text:\n%s", got)
	}
	for _, old := range []string{"operator can see", "operator can read", "in the clear", "end to end, so that even the", "open work"} {
		if strings.Contains(got, old) {
			t.Errorf("a sealed server must not say %q any more", old)
		}
	}
	for _, must := range []string{"cannot open what goes through it", "add a device of their own to your account", "list of devices"} {
		if !strings.Contains(got, must) {
			t.Errorf("the honest text lacks %q", must)
		}
	}
	if w := WithRetentionFor(sealed, 7); !strings.HasPrefix(w, en+"\n\n") || !strings.Contains(w, "7 days") {
		t.Errorf("the retention note follows the honest text: %q", w)
	}
}

// A server without the capability keeps the statement it always had, point for point (item DP).
func TestAServerThatDoesNotSealKeepsItsStatement(t *testing.T) {
	for _, features := range [][]string{nil, {}, {"cast"}} {
		if TextFor(features) != Text() || WithRetentionFor(features, 7) != WithRetention(7) {
			t.Errorf("%v: the statement of a server that does not seal does not change", features)
		}
	}
	if HonestID == "" || strings.ContainsAny(HonestID, " ABCDEFGHIJKLMNOPQRSTUVWXYZ") {
		t.Errorf("a kebab-case id: %q", HonestID)
	}
	for _, p := range Points {
		if p.ID == HonestID {
			t.Errorf("the id %q is already a point of the old statement", HonestID)
		}
	}
}
