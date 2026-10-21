package config

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// checkWebDir refuses a webDir that could hand out the server's own secrets (WEB-BINDING
// page-served-by-the-server "never ... a private key", normative resolution 5; audit gap G-6): the
// directory of web client files must not be, contain or lie inside the data directory (exchange.json
// with the operator's password record, accounts.json, the TLS directory with key.pem) or the TLS
// directory, and must not contain the private key file of a CA-signed certificate (tls.keyFile).
//
// Every path is made absolute and resolved through links first, so a junction or symlink that leads
// to a protected directory is the protected directory. dataDir is empty when the config does not know
// where it lives (a config built in memory): then only the key file is checked.
func (c Config) checkWebDir() error {
	if c.WebDir == "" {
		return nil
	}
	if strings.ContainsRune(c.WebDir, 0) {
		return fmt.Errorf("webDir %q is not a path", c.WebDir)
	}
	root := canonicalPath(c.WebDir)
	if c.dataDir != "" {
		data := canonicalPath(c.dataDir)
		for _, p := range []struct{ name, dir string }{
			{"data directory", data},
			{"TLS directory", canonicalPath(filepath.Join(c.dataDir, TLSDir))},
		} {
			switch {
			case samePath(root, p.dir):
				return fmt.Errorf("webDir %q is the %s of the server: it would serve the server's own secrets", c.WebDir, p.name)
			case within(p.dir, root):
				return fmt.Errorf("webDir %q lies inside the %s of the server: it would serve the server's own secrets", c.WebDir, p.name)
			case within(root, p.dir):
				return fmt.Errorf("webDir %q contains the %s of the server: it would serve the server's own secrets", c.WebDir, p.name)
			}
		}
	}
	keys := []string{c.TLS.KeyFile}
	if c.dataDir != "" {
		keys = append(keys, filepath.Join(c.dataDir, TLSDir, KeyName))
	}
	for _, k := range keys {
		if k != "" && within(root, canonicalPath(k)) {
			return fmt.Errorf("webDir %q contains the private key file of the server (%s)", c.WebDir, filepath.Base(k))
		}
	}
	return nil
}

// ValidateIn is Validate for a config whose data directory is known: the directory of paths. Load
// already remembers where the file came from, so a Load-then-Validate caller is checked without it; a
// caller that holds the Paths (the server's start, the admin commands) says so explicitly, which also
// covers a config that was not loaded from a file.
func (c Config) ValidateIn(p Paths) error {
	c.dataDir = p.Dir
	return c.Validate()
}

// canonicalPath is an absolute, link-free, cleaned spelling of p. A path that does not exist (yet) is
// resolved as far as it does: the deepest existing ancestor through its links, the rest as written.
func canonicalPath(p string) string {
	abs, err := filepath.Abs(p)
	if err != nil {
		abs = filepath.Clean(p)
	}
	rest := ""
	cur := abs
	for {
		if real, err := filepath.EvalSymlinks(cur); err == nil {
			return filepath.Join(real, rest)
		}
		parent := filepath.Dir(cur)
		if parent == cur {
			return abs
		}
		rest = filepath.Join(filepath.Base(cur), rest)
		cur = parent
	}
}

// samePath reports whether two canonical paths name one place (case-insensitively on Windows).
func samePath(a, b string) bool {
	rel, err := filepath.Rel(a, b)
	return err == nil && rel == "."
}

// within reports whether child is parent or lies below it.
func within(parent, child string) bool {
	rel, err := filepath.Rel(parent, child)
	if err != nil || filepath.IsAbs(rel) {
		return false
	}
	return rel == "." || (rel != ".." && !strings.HasPrefix(rel, ".."+string(os.PathSeparator)))
}
