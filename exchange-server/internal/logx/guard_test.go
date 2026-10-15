package logx

import (
	"go/ast"
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"regexp"
	"sort"
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

// The patterns of the scan, shared with the test that proves they can fail.
var (
	importLog = regexp.MustCompile(`(?m)^\s*(?:\w+\s+)?"(log|log/slog)"\s*$`)
	stdout    = regexp.MustCompile(`fmt\.Fprint\w*\(\s*os\.Std(?:out|err)\b|\bos\.Std(?:out|err)\.Write`)
	// an id, a token, a password, a pairing code, a login, an `access` or a descriptor handed to Text
	textArg = regexp.MustCompile(`logx\.Text\([^)]*(?:[Ss]hareI[Dd]|[Tt]unnelI[Dd]|[Dd]eviceI[Dd]|[Rr]esourceI[Dd]|[Bb]roadcastI[Dd]|` +
		`[Pp]assword|[Tt]oken|ResumeToken|PairingCode|[Pp]airing[Cc]ode|[Aa]ccess|[Dd]escriptor|\.Login|[Ll]ogin\b|[Ii]nvitationCode|[Ss]haredPassword)`)
	// a wire envelope, a record or a string built from one handed to the logger
	envelopeArg = regexp.MustCompile(`logx\.\w+\([^)]*(?:\bwire\.(?:Encode|Frame|Record|Envelope)|\.Marshal\(|json\.|Encode\(|\.Raw\b|Envelope)`)
	// a forbidden field KEY (DEVICE-EXCHANGE 7.9: a log never carries these)
	badKey = regexp.MustCompile(`logx\.(?:Text|Int)\(\s*"(?:token|password|pairingCode|access|shareId|broadcastId|deviceToken|resourceId|deviceId|tunnelId|secret|invitationCode)"`)
)

// TestStaticGuard is DEVICE-EXCHANGE 7.9 as a source scan (tickets 139 T-4 and 167 rules 4 and 5):
// the log is the only way out for an id, and it names an id by its 8-hex fingerprint only.
func TestStaticGuard(t *testing.T) {
	root := moduleRoot(t)
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
			t.Errorf("%s passes an id, a token, a password, a code or a descriptor to logx.Text", rel)
		}
		if !inLogx && envelopeArg.MatchString(s) {
			t.Errorf("%s hands a wire envelope, a record or an encoding to the logger", rel)
		}
		if badKey.MatchString(s) {
			t.Errorf("%s uses a forbidden log field key", rel)
		}
	}
}

// The guard must be able to fail: prove each pattern matches what it is meant to catch.
func TestGuardPatternsCatch(t *testing.T) {
	if !importLog.MatchString("import (\n\t\"fmt\"\n\t\"log\"\n)") {
		t.Error("the log import pattern misses a plain import")
	}
	if !importLog.MatchString("import (\n\tl \"log/slog\"\n)") {
		t.Error("the log import pattern misses an aliased slog import")
	}
	if !stdout.MatchString(`fmt.Fprintln(os.Stderr, "x")`) || !stdout.MatchString(`os.Stdout.Write(b)`) {
		t.Error("the standard-stream pattern misses a direct write")
	}
	for _, bad := range []string{
		`logx.Text("id", m.ShareID)`, `logx.Text("t", tunnelID)`, `logx.Text("d", req.DeviceID)`,
		`logx.Text("p", e.Password)`, `logx.Text("c", e.PairingCode)`, `logx.Text("a", rec.Access)`,
		`logx.Text("l", e.Login)`, `logx.Text("x", deviceToken)`, `logx.Text("r", resourceID)`,
	} {
		if !textArg.MatchString(bad) {
			t.Errorf("the Text-argument pattern misses %s", bad)
		}
	}
	for _, bad := range []string{`logx.Text("e", wire.Encode(x))`, `logx.Text("e", string(raw.Raw))`, `logx.Text("j", json.Marshal(x))`} {
		if !envelopeArg.MatchString(bad) {
			t.Errorf("the envelope pattern misses %s", bad)
		}
	}
	for _, bad := range []string{`logx.Text("token", x)`, `logx.Text("access", x)`, `logx.Int("password", 1)`, `logx.Text( "shareId", x)`} {
		if !badKey.MatchString(bad) {
			t.Errorf("the key pattern misses %s", bad)
		}
	}
	for _, ok := range []string{`logx.Text("why", ternary(c, "a", "b"))`, `logx.Text("kind", "sftp-share")`, `logx.Device(id)`} {
		if textArg.MatchString(ok) || badKey.MatchString(ok) || envelopeArg.MatchString(ok) {
			t.Errorf("a clean call is flagged: %s", ok)
		}
	}
}

// TestOnlyTheKnownFieldConstructorsExist: there is no constructor that takes a raw secret. A new
// exported function that returns a Field must be added to this list on purpose, with the reader
// asking whether it can carry a secret.
func TestOnlyTheKnownFieldConstructorsExist(t *testing.T) {
	fset := token.NewFileSet()
	pkgs, err := parser.ParseDir(fset, ".", func(fi os.FileInfo) bool { return !strings.HasSuffix(fi.Name(), "_test.go") }, 0)
	if err != nil {
		t.Fatal(err)
	}
	var got []string
	for _, p := range pkgs {
		for _, f := range p.Files {
			for _, d := range f.Decls {
				fn, ok := d.(*ast.FuncDecl)
				if !ok || fn.Recv != nil || !fn.Name.IsExported() || fn.Type.Results == nil {
					continue
				}
				for _, r := range fn.Type.Results.List {
					if id, ok := r.Type.(*ast.Ident); ok && id.Name == "Field" {
						got = append(got, fn.Name.Name)
					}
				}
			}
		}
	}
	sort.Strings(got)
	want := []string{"Addr", "Broadcast", "Device", "Int", "Invitation", "Login", "Port", "Resource", "Share", "Text", "Tunnel"}
	if strings.Join(got, ",") != strings.Join(want, ",") {
		t.Fatalf("the Field constructors are %v, want %v", got, want)
	}
}
