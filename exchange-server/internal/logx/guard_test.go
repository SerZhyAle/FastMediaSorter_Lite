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
	// The names of sealed content and of the keys around it (DEVICE-EXCHANGE 27 items DH, DI, DL, DQ, ticket 234)
	// are forbidden keys as well: whatever such a field would carry is a box, a key or bytes of a record.
	badKey = regexp.MustCompile(`logx\.(?:Text|Int)\(\s*"(?:token|password|pairingCode|access|shareId|broadcastId|deviceToken|resourceId|deviceId|tunnelId|secret|invitationCode|` +
		`sealed|sealedAccess|box|keyRing|ring|e2eKey|kem|fromKey|ct|wk|enc|chunk|payload|relayKey|tunnelPin|credentials|verifier|verifiers|` +
		`authorization|salt)"`)
	// A sealed box, a sealed value, a key, a key ring or the bytes of a chunk or a record handed to Text (27 item
	// DQ: the server holds them as opaque bytes and never writes one down, not even a prefix). Applied to a source
	// whose string literals are blanked, so a fixed word such as "sealed" in a message is no finding. A broadcast's
	// listener verifiers and a listener's Authorization header (DEVICE-EXCHANGE item BF, ticket 263) are the same
	// kind of value: a salt and a hash a guess can be tested against, and a Basic credential that is a password.
	sealedArg = regexp.MustCompile(`logx\.Text\([^)]*(?:\.Sealed\b|\b[Ss]ealed\w*\b|\.Box\b|\b[Bb]ox\w*\b|[Kk]eyRing|[Rr]elayKey|[Tt]unnelPin|[Ee]2[Ee][Kk]ey|` +
		`\.KEM\b|\bkem\b|[Ff]romKey|\.Payload\b|\bpayload\b|\.Chunk\b|\bchunk\b|\.Wk\b|\bwk\b|\.Enc\b|\.Ct\b|\bct\b|` +
		`\b[Vv]erifiers?\b|\b[Aa]uthorization\b|\.Salt\b|\bsalt\b)`)
	stringLiteral = regexp.MustCompile("\"(?:[^\"\\\\\\n]|\\\\.)*\"|`[^`]*`")
)

// blankStrings replaces every string literal of a source with an empty one, keeping the rest.
func blankStrings(s string) string { return stringLiteral.ReplaceAllString(s, `""`) }

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
		if sealedArg.MatchString(blankStrings(s)) {
			t.Errorf("%s passes a box, a sealed value, a key, a key ring or chunk bytes to logx.Text", rel)
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
	// sealed content (ticket 234 item DQ): a box, a sealed access, a key, a key ring, chunk bytes, in any spelling
	for _, bad := range []string{
		`logx.Text("b", box)`, `logx.Text("b", string(m.Box))`, `logx.Text("s", rec.SealedAccess)`, `logx.Text("s", v.Sealed)`,
		`logx.Text("s", string(sealedBody))`, `logx.Text("k", e2eKey)`, `logx.Text("k", dev.E2EKey)`, `logx.Text("k", keyRing)`,
		`logx.Text("k", hex.EncodeToString(chunk.Payload))`, `logx.Text("k", string(payload))`, `logx.Text("k", env.FromKey)`,
		`logx.Text("k", kem)`, `logx.Text("k", wk)`, `logx.Text("k", e.Enc)`, `logx.Text("k", ct)`, `logx.Text("k", relayKey)`,
		`logx.Text("k", tunnelPin)`,
		// a listener verifier or credential (item BF)
		`logx.Text("v", verifiers)`, `logx.Text("v", m.Verifiers)`, `logx.Text("v", string(verifier.Hash))`, `logx.Text("s", v.Salt)`,
		`logx.Text("a", st.Authorization())`, `logx.Text("a", req.Authorization)`, `logx.Text("a", authorization)`,
	} {
		if !sealedArg.MatchString(blankStrings(bad)) {
			t.Errorf("the sealed-content pattern misses %s", bad)
		}
	}
	for _, bad := range []string{`logx.Text("sealed", x)`, `logx.Text("box", x)`, `logx.Int("e2eKey", 1)`, `logx.Text( "keyRing", x)`,
		`logx.Text("payload", x)`, `logx.Text("fromKey", x)`, `logx.Text("wk", x)`, `logx.Text("tunnelPin", x)`,
		`logx.Text("verifiers", x)`, `logx.Text("authorization", x)`, `logx.Text("salt", x)`} {
		if !badKey.MatchString(bad) {
			t.Errorf("the key pattern misses %s", bad)
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
	// the fixed words and enums of sealed content are clean: a kind, a reason, a size class, a count
	for _, ok := range []string{`logx.Text("kind", "sealed")`, `logx.Text("kind", v.Kind)`, `logx.Text("why", chunkWhy(err))`,
		`logx.Text("size", sizeClass(m.Size))`, `logx.Text("reason", "sealed content is not accepted")`, `logx.Text("kind", wire.ClipKindSealed)`,
		`logx.Int("chunks", n)`, `logx.Int("bytes", len(payload))`, `logx.Text("why", "payload too large")`} {
		if sealedArg.MatchString(blankStrings(ok)) || badKey.MatchString(ok) {
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
