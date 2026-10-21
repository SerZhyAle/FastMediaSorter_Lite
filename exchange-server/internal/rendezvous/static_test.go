package rendezvous_test

import (
	"bufio"
	"bytes"
	"crypto/tls"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"testing"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/rendezvous"
)

// The page the server serves, from a real TLS connection (WEB-BINDING page-served-by-the-server and normative
// resolution 5; audit gap G-4): a fixed table of types, a size bound, no file the request did not name
// plainly, nothing outside the directory, nothing behind a link, no key.

func staticWrite(t *testing.T, path, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
}

// staticLinkDir makes link a directory link to target (a symlink, or a junction on Windows) or skips the
// test, saying why.
func staticLinkDir(t *testing.T, link, target string) {
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

type staticAnswer struct {
	status int
	header http.Header
	body   []byte
	raw    []byte
}

// staticGet sends one request target as written (percent escapes are not touched) and reads the answer to the
// end of the connection.
func staticGet(t *testing.T, addr, method, target string) staticAnswer {
	t.Helper()
	conn, err := tls.Dial("tcp", addr, &tls.Config{InsecureSkipVerify: true})
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()
	if _, err := conn.Write([]byte(method + " " + target + " HTTP/1.1\r\nHost: " + addr + "\r\nConnection: close\r\n\r\n")); err != nil {
		t.Fatal(err)
	}
	var raw bytes.Buffer
	br := bufio.NewReader(io.TeeReader(conn, &raw))
	resp, err := http.ReadResponse(br, nil)
	if err != nil {
		t.Fatalf("%s %s: %v", method, target, err)
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(io.LimitReader(resp.Body, 8<<20))
	if err != nil {
		t.Fatalf("%s %s: %v", method, target, err)
	}
	return staticAnswer{status: resp.StatusCode, header: resp.Header, body: body, raw: raw.Bytes()}
}

// staticEnv serves dir; the connection limiter is opened wide so that a table of requests is not throttled.
func staticEnv(t *testing.T, dir string) *layerEnv {
	t.Helper()
	return startLayer(t, func(c *rendezvous.Config) {
		c.WebDir = dir
		lc := limits.DefaultConfig()
		lc.Rates[limits.Connection] = limits.Rate{PerMinute: 100000, Burst: 1000}
		c.Limiter = limits.NewLimiter(lc, nil)
	})
}

func TestStaticFilesAreServedWithTheFixedHeaders(t *testing.T) {
	root := filepath.Join(t.TempDir(), "web")
	big := strings.Repeat("0123456789abcdef", 20000) // 320000 bytes: more than one chunk
	staticWrite(t, filepath.Join(root, "index.html"), "<html>index</html>")
	staticWrite(t, filepath.Join(root, "app.js"), "console.log(1)")
	staticWrite(t, filepath.Join(root, "assets", "big.json"), big)
	staticWrite(t, filepath.Join(root, "sub", "page.html"), "<html>page</html>")
	staticWrite(t, filepath.Join(root, "empty.css"), "")
	env := staticEnv(t, root)

	cases := []struct {
		target, ctype, body string
	}{
		{"/", "text/html; charset=utf-8", "<html>index</html>"},
		{"/index.html", "text/html; charset=utf-8", "<html>index</html>"},
		{"/index.html?x=1&y=%2e%2e", "text/html; charset=utf-8", "<html>index</html>"},
		{"/app.js", "text/javascript; charset=utf-8", "console.log(1)"},
		{"/sub/page.html", "text/html; charset=utf-8", "<html>page</html>"},
		{"/assets/big.json", "application/json", big},
		{"/empty.css", "text/css; charset=utf-8", ""},
	}
	for _, tc := range cases {
		a := staticGet(t, env.addr, "GET", tc.target)
		if a.status != 200 || a.header.Get("Content-Type") != tc.ctype || string(a.body) != tc.body {
			t.Errorf("GET %s: status %d, type %q, %d bytes; want 200, %q, %d bytes", tc.target, a.status, a.header.Get("Content-Type"), len(a.body), tc.ctype, len(tc.body))
			continue
		}
		if got := a.header.Get("Content-Length"); got != strconv.Itoa(len(tc.body)) {
			t.Errorf("GET %s: Content-Length %q, want %d", tc.target, got, len(tc.body))
		}
		if a.header.Get("X-Content-Type-Options") != "nosniff" {
			t.Errorf("GET %s: X-Content-Type-Options %q, want nosniff", tc.target, a.header.Get("X-Content-Type-Options"))
		}
		for _, h := range []string{"Access-Control-Allow-Origin", "Access-Control-Allow-Credentials", "Set-Cookie", "Www-Authenticate", "Location"} {
			if a.header.Get(h) != "" {
				t.Errorf("GET %s carries %s: %q", tc.target, h, a.header.Get(h))
			}
		}
	}
}

func TestStaticRefusalsAreABare404ForEveryFormOfAnEscape(t *testing.T) {
	base := t.TempDir()
	root := filepath.Join(base, "web")
	staticWrite(t, filepath.Join(root, "index.html"), "<html>index</html>")
	staticWrite(t, filepath.Join(root, "sub", "page.html"), "<html>page</html>")
	staticWrite(t, filepath.Join(base, "outside", "secret.txt"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(base, "secret.txt"), "OUTSIDE-SECRET")
	// files that exist in the directory and must still not be served
	staticWrite(t, filepath.Join(root, ".env"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, ".hidden", "x.html"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "server.key"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "cert.pem"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "id_rsa"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "data.bin"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "noext"), "OUTSIDE-SECRET")
	staticWrite(t, filepath.Join(root, "readme.txt"), "-----BEGIN PRIVATE KEY-----\nOUTSIDE-SECRET\n-----END PRIVATE KEY-----\n")
	staticWrite(t, filepath.Join(root, "LongFileName.html"), "OUTSIDE-SECRET")
	staticLinkDir(t, filepath.Join(root, "link"), filepath.Join(base, "outside"))
	staticLinkDir(t, filepath.Join(root, "up"), base)
	staticLinkDir(t, filepath.Join(root, "alias"), filepath.Join(root, "sub"))
	if err := os.Symlink(filepath.Join(base, "secret.txt"), filepath.Join(root, "filelink.txt")); err != nil {
		t.Logf("file symlink not created, its case is not exercised here (the white-box test tries it as well): %v", err)
	}
	// the links are real: the file system itself reads the secret through them, so a 404 below is the server's doing
	for _, p := range []string{"link", "up/outside"} {
		if b, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(p), "secret.txt")); err != nil || string(b) != "OUTSIDE-SECRET" {
			t.Fatalf("the link %s does not lead to the secret: %v", p, err)
		}
	}
	env := staticEnv(t, root)

	// the happy path next to them still works
	if a := staticGet(t, env.addr, "GET", "/sub/page.html"); a.status != 200 {
		t.Fatalf("a plain file: status %d", a.status)
	}

	refused := []string{
		// traversal
		"/../secret.txt", "/../outside/secret.txt", "/sub/../../secret.txt", "/%2e%2e/secret.txt", "/%2E%2E/secret.txt",
		"/..%2fsecret.txt", "/%2e%2e%2fsecret.txt", "/..%5csecret.txt", "/%2e%2e%5coutside%5csecret.txt", "/sub/%2e%2e/index.html",
		"/./index.html", "/sub/./page.html", "/%2e/index.html",
		// double encoding is decoded once only
		"/%252e%252e/secret.txt", "/%252e%252e%252fsecret.txt", "/sub%252fpage.html", "/%2541pp.js",
		// separators, drive letters, streams, device names
		"/sub%2fpage.html", "/sub%5cpage.html", `/sub\page.html`, "//sub/page.html", "/sub//page.html",
		"/C:/Windows/win.ini", "/c%3a/windows/win.ini", "/index.html::$DATA", "/index.html:secret", "/con", "/NUL.html", "/aux.txt",
		// NUL and control characters
		"/%00", "/index.html%00.png", "/index.html%0d%0a", "/%01.html",
		// dot components and dotfiles that exist
		"/.env", "/%2eenv", "/.hidden/x.html", "/sub/.hidden/x.html", "/.git/config",
		// NTFS aliases
		"/index.html.", "/index.html%2e", "/index.html%20", "/INDEX~1.HTM", "/LONGFI~1.HTM", "/sub./page.html",
		// key material and unknown types, although the files exist
		"/server.key", "/cert.pem", "/id_rsa", "/data.bin", "/noext", "/readme.txt",
		// directories: no listing
		"/sub", "/sub/", "/link", "/link/", "/assets/",
		// links and reparse points, to the outside and to the inside
		"/link/secret.txt", "/up/secret.txt", "/up/outside/secret.txt", "/alias/page.html", "/filelink.txt",
		// the protocol's own prefix, however it is spelled
		"/v2/x.html", "/V2/x.html", "/%76%32/x.html", "/v2",
		// not found at all
		"/missing.html", "/sub/missing.html", "/index.html/x", "/page.html/",
		// bad escapes
		"/%", "/%zz.html", "/%e0%a4%a.html", "/%ff.txt",
	}
	for _, target := range refused {
		a := staticGet(t, env.addr, "GET", target)
		if a.status != 404 || len(a.body) != 0 || bytes.Contains(a.raw, []byte("OUTSIDE-SECRET")) {
			t.Errorf("GET %s: status %d, %d body bytes; want a bare 404", target, a.status, len(a.body))
			continue
		}
		for _, h := range []string{"Content-Type", "Access-Control-Allow-Origin", "Set-Cookie"} {
			if a.header.Get(h) != "" {
				t.Errorf("GET %s: a refusal carries %s: %q", target, h, a.header.Get(h))
			}
		}
	}

	// anything but GET is a 405, whether the file exists or not
	for _, method := range []string{"POST", "PUT", "DELETE", "HEAD", "OPTIONS", "PATCH"} {
		for _, target := range []string{"/index.html", "/missing.html", "/"} {
			if a := staticGet(t, env.addr, method, target); a.status != 405 || len(a.body) != 0 {
				t.Errorf("%s %s: status %d, want 405", method, target, a.status)
			}
		}
	}
}

func TestStaticFilesAreNotServedWithoutAWebDir(t *testing.T) {
	root := filepath.Join(t.TempDir(), "web")
	staticWrite(t, filepath.Join(root, "index.html"), "<html>index</html>")
	env := staticEnv(t, "")
	for _, target := range []string{"/", "/index.html"} {
		if a := staticGet(t, env.addr, "GET", target); a.status != 404 {
			t.Errorf("GET %s without a web directory: status %d, want 404", target, a.status)
		}
	}
}
