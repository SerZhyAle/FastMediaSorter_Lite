package rendezvous

import (
	"bytes"
	"io"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"unicode/utf8"

	"fms/exchange-server/internal/wire"
)

// Static files: the page the server serves (WEB-BINDING page-served-by-the-server, normative resolution 5).
//
// With a directory of web client files configured (the host setting webDir), an HTTP/1.1 `GET` inside TLS
// for a path outside the protocol's `/v2/` is looked up under that directory: `/` is its index.html, there
// is no listing, no path outside the directory, no dot-file; a fixed table maps the file name to
// Content-Type; the answer carries Content-Length and X-Content-Type-Options: nosniff; there is no CORS
// header, no cookie and nothing is written. The path is decoded and normalized ONCE, then judged: traversal,
// dot components, links and reparse points, aliases of a name and private key material are all refused, and
// every refusal is the same bare 404, so a request cannot tell a missing file from a forbidden one.
const (
	// staticMaxFileBytes bounds one file the server hands out; a larger one is a 404, never read into memory.
	staticMaxFileBytes = 32 << 20
	// staticMaxPath bounds the request path and each component of it.
	staticMaxPath      = 1024
	staticMaxComponent = 255
	// staticSniffBytes is how much of the head of a file is looked at for a PEM private key.
	staticSniffBytes = 1024
	staticChunk      = 32 << 10
)

// staticTypes is the closed table from a lower-case file extension to its Content-Type. Nothing outside it
// is served: there is no mime.TypeByExtension (on Windows it reads the registry, so the answer would depend
// on what is installed on the machine), and an unknown extension is a 404 rather than a guess.
var staticTypes = map[string]string{
	".html":        "text/html; charset=utf-8",
	".htm":         "text/html; charset=utf-8",
	".js":          "text/javascript; charset=utf-8",
	".mjs":         "text/javascript; charset=utf-8",
	".css":         "text/css; charset=utf-8",
	".json":        "application/json",
	".map":         "application/json",
	".webmanifest": "application/manifest+json",
	".txt":         "text/plain; charset=utf-8",
	".xml":         "application/xml",
	".svg":         "image/svg+xml",
	".png":         "image/png",
	".jpg":         "image/jpeg",
	".jpeg":        "image/jpeg",
	".gif":         "image/gif",
	".webp":        "image/webp",
	".avif":        "image/avif",
	".ico":         "image/x-icon",
	".wasm":        "application/wasm",
	".woff":        "font/woff",
	".woff2":       "font/woff2",
	".ttf":         "font/ttf",
	".otf":         "font/otf",
}

// privateKeyExtensions are the extensions (and extension-like parts of a name) of private key material and
// key stores; privateKeyNames the stems of SSH private keys. They are refused even if the table above
// ever lists one, and a name that carries one anywhere after its first dot is refused too ("key.pem.txt").
var privateKeyExtensions = map[string]bool{
	"key": true, "pem": true, "pfx": true, "p12": true, "p8": true, "pk8": true, "der": true,
	"jks": true, "keystore": true, "ppk": true,
}

var privateKeyNames = []string{"id_rsa", "id_dsa", "id_ecdsa", "id_ed25519"}

// reservedDeviceNames are the Windows device names a file name must not be, with or without an extension.
var reservedDeviceNames = map[string]bool{
	"CON": true, "PRN": true, "AUX": true, "NUL": true, "CONIN$": true, "CONOUT$": true,
	"COM1": true, "COM2": true, "COM3": true, "COM4": true, "COM5": true, "COM6": true, "COM7": true, "COM8": true, "COM9": true,
	"LPT1": true, "LPT2": true, "LPT3": true, "LPT4": true, "LPT5": true, "LPT6": true, "LPT7": true, "LPT8": true, "LPT9": true,
}

// staticParts decodes the request path once and returns its components, or false for a path that names
// no servable file. "/" is index.html. Each component is percent-decoded exactly once and then judged on
// the decoded text, so an encoded separator, an encoded dot or an encoded NUL cannot slip past a check
// made on the encoded text, and a second layer of encoding (%252e) stays what it is: literal text that
// names nothing (and is refused outright, since no page needs it).
func staticParts(rawPath string) ([]string, bool) {
	if rawPath == "/" {
		return []string{"index.html"}, true
	}
	if len(rawPath) == 0 || len(rawPath) > staticMaxPath || rawPath[0] != '/' || strings.HasSuffix(rawPath, "/") {
		return nil, false // not a path, or a directory
	}
	for i := 0; i < len(rawPath); i++ {
		if c := rawPath[i]; c < 0x20 || c == 0x7f || c == '\\' {
			return nil, false
		}
	}
	segments := strings.Split(rawPath[1:], "/")
	parts := make([]string, 0, len(segments))
	for _, seg := range segments {
		if seg == "" {
			return nil, false
		}
		dec, err := url.PathUnescape(seg)
		if err != nil || !staticComponentOK(dec) {
			return nil, false
		}
		parts = append(parts, dec)
	}
	if strings.EqualFold(parts[0], "v2") {
		return nil, false // the path prefix /v2/ stays the protocol's, however it is spelled
	}
	return parts, true
}

// staticComponentOK judges one decoded component.
func staticComponentOK(c string) bool {
	if c == "" || len(c) > staticMaxComponent || !utf8.ValidString(c) {
		return false
	}
	if c[0] == '.' { // ".", ".." and every dot-file or dot-directory
		return false
	}
	if c != strings.TrimSpace(c) || strings.HasSuffix(c, ".") {
		return false // Windows drops a trailing dot and trailing spaces: "index.html." is index.html
	}
	for i := 0; i < len(c); i++ {
		switch b := c[i]; {
		case b < 0x20, b == 0x7f: // NUL and the other control characters
			return false
		case strings.IndexByte(`/\:<>"|?*`, b) >= 0:
			// a separator (an encoded one), a drive letter or a stream (C:, ::$DATA), and the characters
			// no Windows name has
			return false
		case b == '%' && i+2 < len(c) && isHex(c[i+1]) && isHex(c[i+2]):
			return false // a second layer of percent-encoding: no page needs it
		case b == '~' && shortNameTail(c[i+1:]):
			return false // an 8.3 short name ("PROGRA~1", "INDEX~1.HTM") is an alias of another name
		}
	}
	stem := c
	if i := strings.IndexByte(stem, '.'); i >= 0 {
		stem = stem[:i]
	}
	return !reservedDeviceNames[strings.ToUpper(strings.TrimRight(stem, " "))]
}

func isHex(b byte) bool {
	return b >= '0' && b <= '9' || b >= 'a' && b <= 'f' || b >= 'A' && b <= 'F'
}

// shortNameTail is true when s (the text after a '~') is the digits of an 8.3 short name followed by the
// end of the name or its extension: "1", "1.HTM", "12.JS".
func shortNameTail(s string) bool {
	i := 0
	for i < len(s) && s[i] >= '0' && s[i] <= '9' {
		i++
	}
	return i > 0 && (i == len(s) || s[i] == '.')
}

// staticPolicy is what may be served by name: no dot component, no private key material, and a
// file name whose extension is in the closed table. It is run on the name as requested and again on the
// name as the file system spells it, so an alias (an 8.3 short name, another case) cannot get past it.
func staticPolicy(parts []string) (ctype string, ok bool) {
	if len(parts) == 0 {
		return "", false
	}
	for _, p := range parts {
		if p == "" || p[0] == '.' {
			return "", false
		}
	}
	name := strings.ToLower(parts[len(parts)-1])
	for _, k := range privateKeyNames {
		if name == k || strings.HasPrefix(name, k+".") {
			return "", false
		}
	}
	segments := strings.Split(name, ".")
	for _, ext := range segments[1:] {
		if privateKeyExtensions[ext] {
			return "", false
		}
	}
	ctype, ok = staticTypes[filepath.Ext(name)]
	return ctype, ok
}

// looksLikePrivateKey is true for a file whose first bytes are a PEM private key header
// ("-----BEGIN PRIVATE KEY-----", "RSA PRIVATE KEY", "EC PRIVATE KEY", "ENCRYPTED PRIVATE KEY",
// "OPENSSH PRIVATE KEY"), after an optional byte order mark and blank space.
func looksLikePrivateKey(head []byte) bool {
	head = bytes.TrimPrefix(head, []byte("\xef\xbb\xbf"))
	head = bytes.TrimLeft(head, " \t\r\n")
	if !bytes.HasPrefix(head, []byte("-----BEGIN ")) {
		return false
	}
	line := head
	if i := bytes.IndexByte(line, '\n'); i >= 0 {
		line = line[:i]
	}
	return bytes.Contains(line, []byte("PRIVATE KEY"))
}

// openStatic finds the file the parts name under webDir and opens it, or says no. The root is resolved
// through links once per request (the operator may re-point it); everything below it must be plain
// directories and one regular file - a symlink, a junction or any other reparse point under the root is
// refused, not followed - and the file system's own spelling of the final path (which turns an 8.3 short
// name or another case into the real name) must be the requested one and pass the policy again.
func openStatic(webDir string, parts []string) (f *os.File, size int64, ctype string, ok bool) {
	if webDir == "" {
		return nil, 0, "", false
	}
	if _, ok := staticPolicy(parts); !ok {
		return nil, 0, "", false
	}
	abs, err := filepath.Abs(webDir)
	if err != nil {
		return nil, 0, "", false
	}
	root, err := filepath.EvalSymlinks(abs)
	if err != nil {
		return nil, 0, "", false
	}
	if fi, err := os.Stat(root); err != nil || !fi.IsDir() {
		return nil, 0, "", false
	}
	cur := root
	var last os.FileInfo
	for i, p := range parts {
		cur = filepath.Join(cur, p)
		fi, err := os.Lstat(cur)
		if err != nil || isLinkOrReparsePoint(fi) {
			return nil, 0, "", false
		}
		if i < len(parts)-1 {
			if !fi.IsDir() {
				return nil, 0, "", false
			}
			continue
		}
		if !fi.Mode().IsRegular() {
			return nil, 0, "", false // a directory is never listed or indexed
		}
		last = fi
	}
	resolved, err := filepath.EvalSymlinks(cur)
	if err != nil {
		return nil, 0, "", false
	}
	rel, err := filepath.Rel(root, resolved)
	if err != nil || rel == "." || rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) || filepath.IsAbs(rel) {
		return nil, 0, "", false // resolved outside the root
	}
	spelled := strings.Split(rel, string(os.PathSeparator))
	if len(spelled) != len(parts) {
		return nil, 0, "", false
	}
	for i := range spelled {
		if !strings.EqualFold(spelled[i], parts[i]) {
			return nil, 0, "", false // an alias: the real name is another spelling
		}
	}
	if ctype, ok = staticPolicy(spelled); !ok {
		return nil, 0, "", false
	}

	file, err := os.Open(resolved)
	if err != nil {
		return nil, 0, "", false
	}
	opened, err := file.Stat()
	if err != nil || !opened.Mode().IsRegular() || !os.SameFile(last, opened) || opened.Size() > staticMaxFileBytes {
		_ = file.Close()
		return nil, 0, "", false
	}
	head := make([]byte, staticSniffBytes)
	n, err := file.ReadAt(head, 0)
	if err != nil && err != io.EOF {
		_ = file.Close()
		return nil, 0, "", false
	}
	if looksLikePrivateKey(head[:n]) {
		_ = file.Close()
		return nil, 0, "", false
	}
	return file, opened.Size(), ctype, true
}

// serveStaticFile answers a request under the configured web directory: GET only (405 otherwise), a file
// or a bare 404. The 200 carries exactly Content-Type, Content-Length, X-Content-Type-Options: nosniff and
// Connection: close - no CORS header, no cookie, no caching header, no new authentication surface. The
// file is streamed in chunks, never read whole into memory.
func (s *Server) serveStaticFile(st *Stream, req wire.HTTPRequest) {
	if req.Method != "GET" {
		_ = st.WriteRaw(wire.HTTPResponse(405, 0))
		return
	}
	parts, ok := staticParts(req.Path)
	if !ok {
		_ = st.WriteRaw(wire.HTTPResponse(404, 0))
		return
	}
	f, size, ctype, ok := openStatic(s.cfg.WebDir, parts)
	if !ok {
		_ = st.WriteRaw(wire.HTTPResponse(404, 0))
		return
	}
	defer f.Close()
	head := "HTTP/1.1 200 OK\r\nContent-Type: " + ctype + "\r\nContent-Length: " + strconv.FormatInt(size, 10) +
		"\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n"
	if err := st.WriteRaw([]byte(head)); err != nil {
		return
	}
	src := io.NewSectionReader(f, 0, size)
	buf := make([]byte, staticChunk)
	for {
		n, err := src.Read(buf)
		if n > 0 {
			if werr := st.WriteRaw(buf[:n]); werr != nil {
				return
			}
		}
		if err != nil {
			return
		}
	}
}
