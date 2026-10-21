package rendezvous

import (
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"runtime"
	"strings"
	"testing"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/password"
)

// White-box cases of the static-file lookup (WEB-BINDING page-served-by-the-server and normative resolution
// 5; audit gaps G-4 and G-13). The same refusals are held again over a real TLS connection in
// static_test.go.

func writeFile(t *testing.T, path, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
}

// linkDir makes link a directory link to target: a symlink, or on Windows a junction (which needs no
// privilege). It skips the test, naming why, when the OS refuses both.
func linkDir(t *testing.T, link, target string) {
	t.Helper()
	if err := os.Symlink(target, link); err == nil {
		return
	} else if runtime.GOOS != "windows" {
		t.Skipf("cannot create a symlink here: %v", err)
	}
	if out, err := exec.Command("cmd", "/c", "mklink", "/J", link, target).CombinedOutput(); err != nil {
		t.Skipf("cannot create a symlink or a junction here: %v: %s", err, out)
	}
}

// linkFile makes link a symlink to the file target, or skips naming why the OS refuses (on Windows a file
// symlink needs privilege or Developer Mode; there is no junction for a file).
func linkFile(t *testing.T, link, target string) {
	t.Helper()
	if err := os.Symlink(target, link); err != nil {
		t.Skipf("cannot create a file symlink here (needs privilege on Windows): %v", err)
	}
}

func TestStaticPartsDecodeOnceAndRefuseWhatIsNotAPlainPath(t *testing.T) {
	ok := map[string][]string{
		"/":                       {"index.html"},
		"/index.html":             {"index.html"},
		"/app.js":                 {"app.js"},
		"/sub/page.html":          {"sub", "page.html"},
		"/a%20b.txt":              {"a b.txt"}, // decoded once
		"/caf%C3%A9.txt":          {"caf\u00e9.txt"},
		"/100%25.txt":             {"100%.txt"}, // an encoded percent that does not make another escape
		"/v2file.js":              {"v2file.js"},
		"/sub/v2/page.html":       {"sub", "v2", "page.html"},
		"/a~b.js":                 {"a~b.js"},
		"/notes~final.txt":        {"notes~final.txt"},
		"/a-b_c.d.e.min.js":       {"a-b_c.d.e.min.js"},
		"/x/y/z/deep.css":         {"x", "y", "z", "deep.css"},
		"/%E4%B8%AD%E6%96%87.txt": {"\u4e2d\u6587.txt"},
	}
	for raw, want := range ok {
		got, good := staticParts(raw)
		if !good || !reflect.DeepEqual(got, want) {
			t.Errorf("staticParts(%q) = %q, %v; want %q", raw, got, good, want)
		}
	}
	refused := []string{
		// traversal and dot components, every one, plain and encoded
		"/..", "/../x", "/a/../x.html", "/./x.html", "/a/./x.html", "/%2e%2e/x.html", "/%2E%2E/x.html", "/%2e%2e%2fx.html",
		"/.hidden/secret.txt", "/sub/.hidden/secret.txt", "/.env", "/sub/.env", "/%2eenv", "/.git/config", "/a/.b/c.html",
		// separators and drive letters
		`/..\x.html`, `/a\b.html`, "/..%5Cx.html", "/a%5Cb.html", "/a%2Fb.html", "/a%2fb.html", "//x.html", "/a//b.html",
		"/C:/Windows/win.ini", "/c:win.ini", "/%43%3A/x", "/index.html:stream", "/index.html::$DATA", "/index.html::$data", "/a%3Ab.html",
		// NUL and other control characters
		"/%00", "/index.html%00.png", "/a\x00b.html", "/a%01b.html", "/a%7fb.html", "/a\tb.html", "/a\nb.html",
		// a second layer of encoding stays refused
		"/%252e%252e/x.html", "/%252e%252e%252fx.html", "/%2541.html", "/a%2520b.txt",
		// malformed escapes
		"/%", "/%zz.html", "/a%2", "/a%.html", "/%e0%a4%a.html",
		// NTFS aliases: trailing dot and trailing space, leading space
		"/index.html.", "/index.html%2e", "/index.html%20", "/index.html ", "/%20index.html", "/sub./x.html", "/sub%20/x.html",
		// 8.3 short names
		"/INDEX~1.HTM", "/PROGRA~1/x.html", "/sub/LONGFI~1.HTML", "/a~12", "/a~1", "/a~1.js",
		// wildcard and invalid Windows characters
		"/a*b.html", "/a?b.html", "/a%3Fb.html", "/a<b.html", "/a%3Cb.html", "/a>b.html", `/a"b.html`, "/a|b.html",
		// Windows device names, with and without an extension
		"/con", "/CON", "/con.txt", "/Nul.html", "/aux.js", "/prn", "/COM1", "/com9.txt", "/lpt1.css", "/sub/nul", "/conin$", "/CONOUT$",
		// directories, no listing
		"/sub/", "/sub//", "//",
		// not a path
		"", "x.html", "*",
		// the protocol's prefix, however it is spelled
		"/v2/x.html", "/V2/x.html", "/%76%32/x.html", "/%56%32/index.html",
		// a text that is not UTF-8
		"/%ff%fe.txt",
		// too long
		"/" + strings.Repeat("a", 256) + ".html", "/" + strings.Repeat("d/", 600) + "x.html",
	}
	for _, raw := range refused {
		if got, good := staticParts(raw); good {
			t.Errorf("staticParts(%q) = %q, want a refusal", raw, got)
		}
	}
}

func TestStaticPolicyIsAClosedTableWithoutKeyMaterial(t *testing.T) {
	want := map[string]string{
		"index.html": "text/html; charset=utf-8", "page.HTM": "text/html; charset=utf-8", "app.js": "text/javascript; charset=utf-8",
		"app.MJS": "text/javascript; charset=utf-8", "style.css": "text/css; charset=utf-8", "data.json": "application/json",
		"app.js.map": "application/json", "site.webmanifest": "application/manifest+json", "a.txt": "text/plain; charset=utf-8",
		"i.svg": "image/svg+xml", "i.png": "image/png", "i.JPG": "image/jpeg", "i.jpeg": "image/jpeg", "i.gif": "image/gif",
		"i.webp": "image/webp", "i.avif": "image/avif", "favicon.ico": "image/x-icon", "m.wasm": "application/wasm",
		"f.woff": "font/woff", "f.woff2": "font/woff2", "f.ttf": "font/ttf", "f.otf": "font/otf",
	}
	for name, ctype := range want {
		got, ok := staticPolicy([]string{"sub", name})
		if !ok || got != ctype {
			t.Errorf("staticPolicy(%q) = %q, %v; want %q", name, got, ok, ctype)
		}
	}
	refused := []string{
		// not in the table: no guess, no octet-stream
		"data.bin", "setup.exe", "noext", "LICENSE", "archive.zip", "notes.md", "db.sqlite", "app.config", "x.php", "x.aspx", "x.ps1", "x.dll",
		// private key material and key stores, by name
		"server.key", "SERVER.KEY", "key.pem", "cert.pfx", "cert.p12", "key.p8", "key.pk8", "key.der", "store.jks", "store.keystore", "id.ppk",
		"server.key.txt", "key.pem.json", "a.der.html", "x.p12.js", "id_rsa", "id_rsa.txt", "ID_ED25519", "id_ed25519.json", "id_ecdsa", "id_dsa.html",
		// dot components
		".env", ".hidden.html",
	}
	for _, name := range refused {
		if got, ok := staticPolicy([]string{"sub", name}); ok {
			t.Errorf("staticPolicy(%q) = %q, want a refusal", name, got)
		}
	}
	if _, ok := staticPolicy([]string{".hidden", "a.html"}); ok {
		t.Error("a dot directory above the file was accepted")
	}
	if _, ok := staticPolicy([]string{"a", ".b", "c.html"}); ok {
		t.Error("a dot directory in the middle was accepted")
	}
	if _, ok := staticPolicy(nil); ok {
		t.Error("an empty path was accepted")
	}
}

func TestLooksLikePrivateKeyReadsTheHeadOfAFile(t *testing.T) {
	for _, head := range []string{
		"-----BEGIN PRIVATE KEY-----\nMIIE",
		"-----BEGIN RSA PRIVATE KEY-----\n",
		"-----BEGIN EC PRIVATE KEY-----\r\n",
		"-----BEGIN ENCRYPTED PRIVATE KEY-----\n",
		"-----BEGIN OPENSSH PRIVATE KEY-----\n",
		"\xef\xbb\xbf-----BEGIN PRIVATE KEY-----\n",
		"\n\r\n  \t-----BEGIN PRIVATE KEY-----\n",
		"-----BEGIN PRIVATE KEY-----", // no newline
	} {
		if !looksLikePrivateKey([]byte(head)) {
			t.Errorf("not recognised as a private key: %q", head)
		}
	}
	for _, head := range []string{
		"", "<!doctype html>", "-----BEGIN CERTIFICATE-----\nMIIC", "-----BEGIN PUBLIC KEY-----\n", "x -----BEGIN PRIVATE KEY-----",
		"-----BEGIN CERTIFICATE-----\n-----BEGIN PRIVATE KEY-----\n", // the first line decides: a certificate first is not a key file
		"BEGIN PRIVATE KEY",
	} {
		if looksLikePrivateKey([]byte(head)) {
			t.Errorf("a harmless head was taken for a private key: %q", head)
		}
	}
}

// staticFixture is a web directory with an outside directory next to it that holds a secret.
type staticFixture struct {
	root, outside string
}

func newStaticFixture(t *testing.T) staticFixture {
	t.Helper()
	base := t.TempDir()
	f := staticFixture{root: filepath.Join(base, "web"), outside: filepath.Join(base, "outside")}
	writeFile(t, filepath.Join(f.root, "index.html"), "<html>index</html>")
	writeFile(t, filepath.Join(f.root, "app.js"), "console.log(1)")
	writeFile(t, filepath.Join(f.root, "sub", "page.html"), "<html>page</html>")
	writeFile(t, filepath.Join(f.outside, "secret.txt"), "OUTSIDE-SECRET")
	return f
}

func open(t *testing.T, webDir string, parts ...string) (string, bool) {
	t.Helper()
	f, size, ctype, ok := openStatic(webDir, parts)
	if !ok {
		return "", false
	}
	defer f.Close()
	buf := make([]byte, size)
	if _, err := f.ReadAt(buf, 0); err != nil && size > 0 {
		t.Fatal(err)
	}
	return ctype + "|" + string(buf), true
}

func TestOpenStaticServesPlainFilesAndNothingElse(t *testing.T) {
	f := newStaticFixture(t)
	if got, ok := open(t, f.root, "index.html"); !ok || got != "text/html; charset=utf-8|<html>index</html>" {
		t.Errorf("index.html: %q, %v", got, ok)
	}
	if got, ok := open(t, f.root, "sub", "page.html"); !ok || got != "text/html; charset=utf-8|<html>page</html>" {
		t.Errorf("sub/page.html: %q, %v", got, ok)
	}
	for _, parts := range [][]string{{"missing.html"}, {"sub"}, {"sub", "missing.html"}, {"app.js", "x.js"}, {"index.html", "x"}} {
		if got, ok := open(t, f.root, parts...); ok {
			t.Errorf("%q was served: %q", parts, got)
		}
	}
	if _, ok := open(t, "", "index.html"); ok {
		t.Error("an empty webDir served a file")
	}
	if _, ok := open(t, filepath.Join(f.root, "nope"), "index.html"); ok {
		t.Error("a webDir that does not exist served a file")
	}
	if _, ok := open(t, filepath.Join(f.root, "index.html"), "x.html"); ok {
		t.Error("a webDir that is a file served a file")
	}
	// the policy runs inside openStatic as well: a key file that exists is not served
	writeFile(t, filepath.Join(f.root, "server.key"), "x")
	writeFile(t, filepath.Join(f.root, "data.bin"), "x")
	writeFile(t, filepath.Join(f.root, ".hidden.html"), "x")
	for _, name := range []string{"server.key", "data.bin", ".hidden.html"} {
		if got, ok := open(t, f.root, name); ok {
			t.Errorf("%s was served: %q", name, got)
		}
	}
}

func TestOpenStaticRefusesAPrivateKeyByContentWhateverItIsCalled(t *testing.T) {
	f := newStaticFixture(t)
	writeFile(t, filepath.Join(f.root, "readme.txt"), "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----\n")
	writeFile(t, filepath.Join(f.root, "notes.html"), "\xef\xbb\xbf  -----BEGIN RSA PRIVATE KEY-----\n")
	writeFile(t, filepath.Join(f.root, "cert.txt"), "-----BEGIN CERTIFICATE-----\nMIIC\n-----END CERTIFICATE-----\n")
	for _, name := range []string{"readme.txt", "notes.html"} {
		if got, ok := open(t, f.root, name); ok {
			t.Errorf("%s with a private key header was served: %q", name, got)
		}
	}
	if _, ok := open(t, f.root, "cert.txt"); !ok {
		t.Error("a certificate (public) text file was refused")
	}
}

func TestOpenStaticSizeBound(t *testing.T) {
	f := newStaticFixture(t)
	for name, size := range map[string]int64{"edge.js": staticMaxFileBytes, "over.js": staticMaxFileBytes + 1} {
		p := filepath.Join(f.root, name)
		fh, err := os.Create(p)
		if err != nil {
			t.Fatal(err)
		}
		if err := fh.Truncate(size); err != nil {
			t.Fatal(err)
		}
		_ = fh.Close()
	}
	if file, size, _, ok := openStatic(f.root, []string{"edge.js"}); !ok || size != staticMaxFileBytes {
		t.Errorf("a file of exactly the bound: ok=%v size=%d", ok, size)
	} else {
		_ = file.Close()
	}
	if file, _, _, ok := openStatic(f.root, []string{"over.js"}); ok {
		_ = file.Close()
		t.Error("a file above the bound was opened")
	}
}

func TestOpenStaticRefusesLinksAndReparsePointsEvenInsideTheRoot(t *testing.T) {
	f := newStaticFixture(t)

	// a directory link (a symlink, or on Windows a junction) to a directory outside the root
	linkDir(t, filepath.Join(f.root, "link"), f.outside)
	if b, err := os.ReadFile(filepath.Join(f.root, "link", "secret.txt")); err != nil || string(b) != "OUTSIDE-SECRET" {
		t.Fatalf("the link does not lead to the secret, so this test proves nothing: %v", err)
	}
	if got, ok := open(t, f.root, "link", "secret.txt"); ok {
		t.Errorf("a file behind a directory link to the outside was served: %q", got)
	}
	// a directory link to a directory INSIDE the root is refused as well: no link is followed at all
	linkDir(t, filepath.Join(f.root, "alias"), filepath.Join(f.root, "sub"))
	if got, ok := open(t, f.root, "alias", "page.html"); ok {
		t.Errorf("a file behind a directory link inside the root was served: %q", got)
	}
	if got, ok := open(t, f.root, "sub", "page.html"); !ok {
		t.Errorf("the real directory stopped working: %q", got)
	}
	// a link that points up out of the root
	linkDir(t, filepath.Join(f.root, "up"), filepath.Dir(f.root))
	if got, ok := open(t, f.root, "up", "outside", "secret.txt"); ok {
		t.Errorf("a file behind a link to the parent was served: %q", got)
	}
}

func TestOpenStaticRefusesAFileSymlink(t *testing.T) {
	f := newStaticFixture(t)
	linkFile(t, filepath.Join(f.root, "out.txt"), filepath.Join(f.outside, "secret.txt"))
	if b, err := os.ReadFile(filepath.Join(f.root, "out.txt")); err != nil || string(b) != "OUTSIDE-SECRET" {
		t.Fatalf("the link does not lead to the secret, so this test proves nothing: %v", err)
	}
	if got, ok := open(t, f.root, "out.txt"); ok {
		t.Errorf("a symlink to a file outside the root was served: %q", got)
	}
	linkFile(t, filepath.Join(f.root, "in.js"), filepath.Join(f.root, "app.js"))
	if got, ok := open(t, f.root, "in.js"); ok {
		t.Errorf("a symlink to a file inside the root was served: %q", got)
	}
}

// The web directory may itself be reached through a link: an operator's choice of path is not a request's.
func TestOpenStaticServesAWebDirThatIsItselfALink(t *testing.T) {
	f := newStaticFixture(t)
	viaLink := filepath.Join(filepath.Dir(f.root), "web-link")
	linkDir(t, viaLink, f.root)
	if got, ok := open(t, viaLink, "sub", "page.html"); !ok || !strings.HasSuffix(got, "<html>page</html>") {
		t.Errorf("a web directory behind a link was not served: %q, %v", got, ok)
	}
}

func TestOpenStaticSpellingMustBeTheFileSystemsOwn(t *testing.T) {
	f := newStaticFixture(t)
	// the same name in another case is the same file on a case-insensitive file system and is served as the
	// file it is; where the file system distinguishes case it is simply not there
	_, served := open(t, f.root, "INDEX.HTML")
	if runtime.GOOS == "windows" && !served {
		t.Error("another case of the same name was refused on Windows")
	}
	// a path with a different number of parts than the resolved one can never match
	if _, ok := open(t, f.root, "sub", "..", "index.html"); ok {
		t.Error("a path through .. was served")
	}
}

func TestAdminPaceKeysByTheLimitersKey(t *testing.T) {
	store, err := accounts.Open(accounts.Options{Dir: t.TempDir(), Params: password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32}})
	if err != nil {
		t.Fatal(err)
	}
	pacer := store.Pacer()
	s := New(Config{AdminPacer: pacer})
	tcp := func(ip string) net.Addr { return &net.TCPAddr{IP: net.ParseIP(ip), Port: 40000} }

	// two addresses of one IPv6 /64 are one client: the second is turned away inside the slot
	if !s.adminPace(tcp("2001:db8:1:2::1")) {
		t.Fatal("the first check was turned away")
	}
	if wait, ok := pacer.Reserve(accounts.AddrKey(limits.Key(tcp("2001:db8:1:2:ffff:eeee:dddd:7")))); !ok || wait <= 0 {
		t.Errorf("another address of the same /64 was not paced: wait=%v ok=%v", wait, ok)
	}
	// another /64, another IPv4 address and a v4-mapped form of the first IPv4 are different clients
	if wait, ok := pacer.Reserve(accounts.AddrKey(limits.Key(tcp("2001:db8:1:3::1")))); !ok || wait != 0 {
		t.Errorf("another /64 was paced with the first: wait=%v ok=%v", wait, ok)
	}
	if !s.adminPace(tcp("192.0.2.10")) {
		t.Fatal("an IPv4 client was turned away")
	}
	if wait, ok := pacer.Reserve(accounts.AddrKey(limits.Key(tcp("::ffff:192.0.2.10")))); !ok || wait <= 0 {
		t.Errorf("the v4-mapped form of an IPv4 client was not paced with it: wait=%v ok=%v", wait, ok)
	}
	if wait, ok := pacer.Reserve(accounts.AddrKey(limits.Key(tcp("192.0.2.11")))); !ok || wait != 0 {
		t.Errorf("another IPv4 address was paced with the first: wait=%v ok=%v", wait, ok)
	}
	// without a pacer there is no pace
	if !New(Config{}).adminPace(tcp("2001:db8::1")) {
		t.Error("a server without a pacer turned a check away")
	}
}
