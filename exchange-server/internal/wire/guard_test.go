package wire

import (
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// These are the source scans of ticket 161 (T-3, rule 7): the legacy schemaVersion is recognised in
// exactly one place, a `refused` is built only by Refuse, and no call hands Refuse anything but a
// reason constant or a variable that holds one.

func moduleGoFiles(t *testing.T, tests bool) map[string]string {
	t.Helper()
	root, err := filepath.Abs(filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(root, "go.mod")); err != nil {
		t.Fatalf("not the module root: %v", err)
	}
	out := map[string]string{}
	_ = filepath.Walk(root, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(p, ".go") {
			return nil
		}
		if strings.HasSuffix(p, "_test.go") != tests {
			return nil
		}
		b, _ := os.ReadFile(p)
		rel, _ := filepath.Rel(root, p)
		out[filepath.ToSlash(rel)] = string(b)
		return nil
	})
	if len(out) < 8 {
		t.Fatalf("the scan saw only %d files: the root is wrong", len(out))
	}
	return out
}

func TestTheLegacySchemaVersionExistsInExactlyOnePlace(t *testing.T) {
	legacy := regexp.MustCompile(`(?i)schemaVersion"?\s*(?::|=|:=)\s*1\b`)
	var hits []string
	for name, src := range moduleGoFiles(t, false) {
		for _, m := range legacy.FindAllString(src, -1) {
			hits = append(hits, name+": "+m)
		}
	}
	if len(hits) != 1 || !strings.HasPrefix(hits[0], "internal/wire/codec.go:") {
		t.Fatalf("the number 1 of the superseded wire must appear exactly once, in codec.go: %v", hits)
	}
}

func TestARefusedIsBuiltOnlyByRefuse(t *testing.T) {
	literal := regexp.MustCompile(`\bRefused\{`)
	for name, src := range moduleGoFiles(t, false) {
		if name == "internal/wire/reason.go" {
			continue
		}
		if literal.MatchString(src) {
			t.Errorf("%s builds a Refused by hand: use wire.Refuse", name)
		}
	}
}

func TestRefuseTakesAReasonConstantOrAVariable(t *testing.T) {
	call := regexp.MustCompile(`\bRefuse\(\s*([^)]*)\)`)
	funcDecl := regexp.MustCompile(`(?m)^func \([^)]*\) Refuse\([^\n]*\n`)
	okArg := regexp.MustCompile(`^(?:wire\.)?Reason[A-Z]\w*$|^[a-z]\w*(?:\.\w+)*$`)
	for _, tests := range []bool{false, true} {
		for name, src := range moduleGoFiles(t, tests) {
			if name == "internal/wire/reason.go" || name == "internal/wire/guard_test.go" || name == "internal/wire/wire_test.go" {
				continue
			}
			src = funcDecl.ReplaceAllString(src, "")
			for _, m := range call.FindAllStringSubmatch(src, -1) {
				if arg := strings.TrimSpace(m[1]); !okArg.MatchString(arg) {
					t.Errorf("%s: Refuse(%s) takes neither a Reason constant nor a variable", name, arg)
				}
			}
		}
	}
	// the pattern must be able to fail
	for _, bad := range []string{`"unavailable"`, `wire.Reason("x")`, `Reason("x")`} {
		if okArg.MatchString(bad) {
			t.Errorf("the scan lets %s through", bad)
		}
	}
}

// TestEveryReasonConstantIsInTheList: the nine constants of reason.go are exactly AllReasons, so a
// tenth cannot be added without the list (and so without Valid) knowing.
func TestEveryReasonConstantIsInTheList(t *testing.T) {
	src := moduleGoFiles(t, false)["internal/wire/reason.go"]
	consts := regexp.MustCompile(`(?m)^\s*(Reason[A-Z]\w*)\s+Reason\s*=`).FindAllStringSubmatch(src, -1)
	if len(consts) != len(AllReasons) {
		t.Fatalf("reason.go declares %d reason constants, AllReasons lists %d", len(consts), len(AllReasons))
	}
}

// TestTheReasonsTheServerCanEmitAreExactlyTheNineOfTheContract is ticket 167 rule 7: the set of
// reasons the server can emit is built by scanning the sources for every Reason constant a non-test
// file names, and it must be inside the list of DEVICE-EXCHANGE 7.10 - and every one of the nine must
// be emitted somewhere (a reason nothing can produce would be a promise the server cannot keep).
func TestTheReasonsTheServerCanEmitAreExactlyTheNineOfTheContract(t *testing.T) {
	ident := regexp.MustCompile(`\b(?:wire\.)?(Reason[A-Z]\w*)\b`)
	declared := map[string]bool{}
	for _, m := range regexp.MustCompile(`(?m)^\s*(Reason[A-Z]\w*)\s+Reason\s*=`).FindAllStringSubmatch(moduleGoFiles(t, false)["internal/wire/reason.go"], -1) {
		declared[m[1]] = true
	}
	if len(declared) != 9 || len(AllReasons) != 9 {
		t.Fatalf("%d declared, %d listed: the contract has nine reasons", len(declared), len(AllReasons))
	}
	emitted := map[string]bool{}
	for name, src := range moduleGoFiles(t, false) {
		if strings.HasPrefix(name, "internal/wire/") || strings.HasPrefix(name, "internal/testkit/") {
			continue // the declaration, the Valid method and the harness are not emissions
		}
		for _, m := range ident.FindAllStringSubmatch(src, -1) {
			if !declared[m[1]] {
				t.Errorf("%s names %s, which is not one of the nine reasons of 7.10", name, m[1])
			}
			emitted[m[1]] = true
		}
	}
	for r := range declared {
		if !emitted[r] {
			t.Errorf("%s is in the list but nothing in the server emits it", r)
		}
	}
	// the scan can fail: a tenth constant, and a reason used but not declared, are seen
	if declared["ReasonTeapot"] || !ident.MatchString("wire.ReasonTeapot") {
		t.Error("the pattern must see a reason constant that is not declared")
	}
}

// the same list is what the code says: every Reason value is kebab-case and unique
func TestReasonValuesAreKebabCaseAndUnique(t *testing.T) {
	seen := map[Reason]bool{}
	kebab := regexp.MustCompile(`^[a-z]+(-[a-z]+)*$`)
	for _, r := range AllReasons {
		if !kebab.MatchString(string(r)) || seen[r] {
			t.Errorf("%q", r)
		}
		seen[r] = true
	}
}
