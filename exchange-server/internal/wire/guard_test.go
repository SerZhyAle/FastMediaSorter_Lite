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
