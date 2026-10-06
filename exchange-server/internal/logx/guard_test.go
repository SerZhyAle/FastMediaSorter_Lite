package logx

import (
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// moduleRoot is exchange-server/ (this test file lives in internal/logx).
func moduleRoot(t *testing.T) string {
	t.Helper()
	root, err := filepath.Abs(filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(root, "go.mod")); err != nil {
		t.Fatalf("not the module root: %v", err)
	}
	return root
}

func goFiles(t *testing.T, root string) []string {
	t.Helper()
	var out []string
	_ = filepath.Walk(root, func(p string, info os.FileInfo, err error) error {
		if err == nil && !info.IsDir() && strings.HasSuffix(p, ".go") && !strings.HasSuffix(p, "_test.go") {
			out = append(out, p)
		}
		return nil
	})
	return out
}

// TestStaticGuard is contract section 5.3 as a source scan (ticket 139 T-4): the log is the only
// way out for a session, and it names a session by its 8-hex fingerprint only.
func TestStaticGuard(t *testing.T) {
	root := moduleRoot(t)
	importLog := regexp.MustCompile(`(?m)^\s*(?:\w+\s+)?"(log|log/slog)"\s*$`)
	stdout := regexp.MustCompile(`fmt\.Fprint\w*\(\s*os\.Std(?:out|err)\b|\bos\.Std(?:out|err)\.Write`)
	textArg := regexp.MustCompile(`logx\.Text\([^)]*\b(?:[Ss]hareI[Dd]|[Tt]unnelI[Dd]|[Pp]assword|ResumeToken)\b`)
	for _, f := range goFiles(t, root) {
		rel, _ := filepath.Rel(root, f)
		rel = filepath.ToSlash(rel)
		src, err := os.ReadFile(f)
		if err != nil {
			t.Fatal(err)
		}
		s := string(src)
		inLogx := strings.HasPrefix(rel, "internal/logx/")
		inCmd := strings.HasPrefix(rel, "cmd/")
		if !inLogx && importLog.MatchString(s) {
			t.Errorf("%s imports log or log/slog: use internal/logx", rel)
		}
		if !inCmd && !inLogx && stdout.MatchString(s) {
			t.Errorf("%s writes to the standard streams outside cmd/", rel)
		}
		if textArg.MatchString(s) {
			t.Errorf("%s passes an id, a token or a password to logx.Text", rel)
		}
	}
}

// The guard must be able to fail: prove each pattern matches what it is meant to catch.
func TestGuardPatternsCatch(t *testing.T) {
	importLog := regexp.MustCompile(`(?m)^\s*(?:\w+\s+)?"(log|log/slog)"\s*$`)
	if !importLog.MatchString("import (\n\t\"fmt\"\n\t\"log\"\n)") {
		t.Error("the log import pattern misses a plain import")
	}
	if !importLog.MatchString("import (\n\tl \"log/slog\"\n)") {
		t.Error("the log import pattern misses an aliased slog import")
	}
	stdout := regexp.MustCompile(`fmt\.Fprint\w*\(\s*os\.Std(?:out|err)\b|\bos\.Std(?:out|err)\.Write`)
	if !stdout.MatchString(`fmt.Fprintln(os.Stderr, "x")`) || !stdout.MatchString(`os.Stdout.Write(b)`) {
		t.Error("the standard-stream pattern misses a direct write")
	}
	textArg := regexp.MustCompile(`logx\.Text\([^)]*\b(?:[Ss]hareI[Dd]|[Tt]unnelI[Dd]|[Pp]assword|ResumeToken)\b`)
	if !textArg.MatchString(`logx.Text("id", m.ShareID)`) || !textArg.MatchString(`logx.Text("t", tunnelID)`) {
		t.Error("the Text-argument pattern misses an id")
	}
}
