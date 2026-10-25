package logx

import (
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
	"unicode"
	"unicode/utf8"
)

// The local operational audit (spec 247, DEVICE-EXCHANGE 1.5 section C of RECOMMENDATIONS-2026-10-19):
// WHO connected or changed WHAT, FROM WHERE, WHEN and with what outcome, as one bounded
// `[AUDIT]` line per event in its own day file next to the security log.
//
// This is NOT the security log above (write): that one keeps every id as an 8-hex fingerprint and its
// guard tests hold; the audit is the catalog's sanctioned separate artifact that answers the operator's
// "who did this", so it carries the plain login, deviceId and device name of the AUTHENTICATED actor
// and never a secret. What may never appear here either: a password, a token, a pairing code, an
// access descriptor, a connection string, clipboard contents or anything sealed - for those DETAILS
// says only that they changed. Every member is client-influenced until proven otherwise: each is
// cleaned, bounded and quote-escaped so a device name cannot forge or break another audit line. The
// rendering is local; nothing parses it back and it is never sent over the wire.

// Bounds of one audit member, in bytes (injection-safe: a field cannot grow a line past these).
const (
	AuditMaxToken   = 64  // actor, device id, product, remote, op, status
	AuditMaxName    = 96  // device friendly name, target
	AuditMaxDetails = 256 // the quoted delta
)

// AuditStatusOK is the STATUS of an event that happened; AuditRefusal builds a refusal with its reason.
const AuditStatusOK = "OK"

func AuditRefusal(reason string) string { return "REFUSED:" + reason }

// AuditEvent is one connection, authentication or mutation the operator may be asked about. Actor,
// Device and Product are what the server AUTHENTICATED or was told before authentication ("unknown"
// when neither - supplied text is a hint, never proof). Transport is "TLS-stream", "WebSocket",
// "NamedPipe" or "" when the event did not cross the network.
type AuditEvent struct {
	Op         string
	Actor      string
	Device     string
	DeviceName string
	Product    string // product name and version as one word ("FMS_A/1.4"; a token member has no spaces)
	Remote     string // ip:port of the caller
	Transport  string
	Target     string // the account, device, broadcast or resource the change is about
	Details    string // non-sensitive delta; for a secret, "password changed" and nothing more
	Status     string // AuditStatusOK or AuditRefusal(reason)
}

// Audit writes the bounded [AUDIT] lines. Like Logger it is safe on a nil *Audit, which discards, and
// safe for concurrent use.
type Audit struct {
	mu        sync.Mutex
	w         io.Writer
	file      *os.File
	path      string
	dir       string
	max       int64
	size      int64
	days      int
	now       func() time.Time
	lastSweep time.Time
	product   string
}

// NewAudit writes to w with no day files (tests, captures).
func NewAudit(w io.Writer) *Audit { return &Audit{w: w, now: time.Now} }

// OpenAuditDaily owns a UTC day file audit-<day>.log in dir with the per-file byte cap maxBytes and
// the retention of days, the same numbers the security log runs with.
func OpenAuditDaily(dir string, maxBytes int64, days int, now func() time.Time) (*Audit, error) {
	if days < 1 || days > 36500 || maxBytes < 1 {
		return nil, os.ErrInvalid
	}
	if now == nil {
		now = time.Now
	}
	a := &Audit{dir: dir, max: maxBytes, days: days, now: now}
	if err := a.dayFile(a.now()); err != nil {
		return nil, err
	}
	if err := a.Sweep(); err != nil {
		_ = a.file.Close()
		return nil, err
	}
	return a, nil
}

// IsAuditDayFile accepts only this audit's UTC day files and their single bounded rotation.
func IsAuditDayFile(name string) bool {
	name = strings.TrimSuffix(name, ".1")
	if !strings.HasPrefix(name, "audit-") || !strings.HasSuffix(name, ".log") {
		return false
	}
	day := strings.TrimSuffix(strings.TrimPrefix(name, "audit-"), ".log")
	t, err := time.Parse("2006-01-02", day)
	return err == nil && t.Format("2006-01-02") == day
}

func (a *Audit) dayFile(at time.Time) error {
	path := filepath.Join(a.dir, "audit-"+at.UTC().Format("2006-01-02")+".log")
	if a.file != nil && a.path == path {
		return nil
	}
	if a.file != nil {
		_ = a.file.Close()
	}
	a.file, a.w, a.path = nil, nil, ""
	f, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o600)
	if err != nil {
		return err
	}
	if st, err := f.Stat(); err == nil {
		a.size = st.Size()
	}
	a.file, a.w, a.path = f, f, path
	a.writeBanner()
	return nil
}

// SetProduct makes every audit file start with a "server.file-open" event whose bracket names this
// server and its version ("fms-exchange-server/26.10.21.1442"): the other lines name the CLIENT's
// version, so without it a file says nothing about the build that wrote it. The file open now gets
// the event at once; a later day file or a rotation gets it as its first line.
func (a *Audit) SetProduct(product string) {
	if a == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	a.product = product
	a.writeBanner()
}

// writeBanner puts the version event at the head of the file just opened. The caller holds a.mu.
func (a *Audit) writeBanner() {
	if a.product == "" || a.w == nil {
		return
	}
	n, _ := io.WriteString(a.w, a.render(AuditEvent{Op: "server.file-open", Actor: "system", Device: "server",
		Product: a.product, Details: "log file opened", Status: AuditStatusOK}))
	a.size += int64(n)
}

// Sweep removes only owned audit day files outside the configured UTC date window (once a day).
func (a *Audit) Sweep() error {
	a.mu.Lock()
	defer a.mu.Unlock()
	today := a.now().UTC()
	if !a.lastSweep.IsZero() && today.Sub(a.lastSweep) < 24*time.Hour {
		return nil
	}
	entries, err := os.ReadDir(a.dir)
	if err != nil {
		return err
	}
	midnight := time.Date(today.Year(), today.Month(), today.Day(), 0, 0, 0, 0, time.UTC)
	cutoff := midnight.AddDate(0, 0, -(a.days - 1)).Format("2006-01-02")
	for _, entry := range entries {
		name := entry.Name()
		if entry.Type().IsRegular() && IsAuditDayFile(name) && name[6:16] < cutoff {
			if err := os.Remove(filepath.Join(a.dir, name)); err != nil {
				return err
			}
		}
	}
	a.lastSweep = today
	return nil
}

// Close closes a file audit.
func (a *Audit) Close() error {
	if a == nil {
		return nil
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.file != nil {
		err := a.file.Close()
		a.file, a.w, a.path = nil, nil, ""
		return err
	}
	return nil
}

// Record writes one [AUDIT] line. Missing identity renders as "unknown". Each day has a bounded
// current file and one rotation; a single line larger than the byte cap is dropped.
func (a *Audit) Record(e AuditEvent) {
	if a == nil {
		return
	}
	line := a.render(e)

	if a.dir != "" {
		_ = a.Sweep() // before the lock, like the security log's write; the sweep keeps its own once-a-day guard
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.dir != "" {
		if a.file == nil {
			return
		}
		if err := a.dayFile(a.now()); err != nil {
			return
		}
		if a.max > 0 && a.size+int64(len(line)) > a.max && a.size > 0 {
			_ = a.file.Close()
			a.file, a.w = nil, nil
			_ = os.Remove(a.path + ".1")
			_ = os.Rename(a.path, a.path+".1")
			a.path = ""
			if err := a.dayFile(a.now()); err != nil {
				return
			}
		}
		if a.max > 0 && a.size+int64(len(line)) > a.max {
			return
		}
	} else if a.w == nil {
		return
	}
	n, _ := io.WriteString(a.w, line)
	a.size += int64(n)
}

// render is the one [AUDIT] line of an event, newline included.
func (a *Audit) render(e AuditEvent) string {
	var b strings.Builder
	b.WriteString("[AUDIT] ")
	b.WriteString(a.now().UTC().Format(time.RFC3339))
	b.WriteString(" [")
	b.WriteString(auditToken(e.Product, AuditMaxToken))
	if e.Remote != "" || e.Transport != "" {
		b.WriteString(" @ ")
		b.WriteString(auditToken(e.Remote, AuditMaxToken))
	}
	if e.Transport != "" {
		b.WriteString(" ")
		b.WriteString(auditToken(e.Transport, AuditMaxToken))
	}
	b.WriteString("] ACTOR=")
	b.WriteString(auditToken(e.Actor, AuditMaxToken))
	b.WriteString(" DEVICE=")
	b.WriteString(auditToken(e.Device, AuditMaxToken))
	if e.DeviceName != "" {
		b.WriteString(" DEVNAME=\"")
		b.WriteString(auditQuoted(e.DeviceName, AuditMaxName))
		b.WriteString("\"")
	}
	b.WriteString(" OP=")
	b.WriteString(auditToken(e.Op, AuditMaxToken))
	if e.Target != "" {
		b.WriteString(" TARGET=")
		b.WriteString(auditToken(e.Target, AuditMaxName))
	}
	b.WriteString(" DETAILS=\"")
	b.WriteString(auditQuoted(e.Details, AuditMaxDetails))
	b.WriteString("\" STATUS=")
	b.WriteString(auditToken(e.Status, AuditMaxToken))
	b.WriteByte('\n')
	return b.String()
}

// auditToken cleans a value that must be one word: controls and spaces are replaced (an empty or
// blank result is "unknown"), quotes and backslashes are dropped, and the value is capped in bytes.
func auditToken(s string, max int) string {
	if strings.TrimSpace(s) == "" {
		return "unknown"
	}
	var b strings.Builder
	for _, r := range s {
		switch {
		case unicode.IsControl(r) || unicode.IsSpace(r) || r == utf8.RuneError:
			b.WriteByte('_')
		case r == '"' || r == '\\':
			// dropped: a token member has no quoting to escape into
		case r == ' ':
			b.WriteByte('_')
		default:
			b.WriteRune(r)
		}
	}
	out := b.String()
	if len(out) > max {
		out = out[:max]
		for !utf8.ValidString(out) {
			out = out[:len(out)-1]
		}
	}
	if strings.TrimSpace(out) == "" {
		return "unknown"
	}
	return out
}

// auditQuoted cleans a value that renders inside double quotes: controls (CR and LF included) become
// spaces so no value can forge a line, a quote and a backslash are escaped, and the value is capped.
func auditQuoted(s string, max int) string {
	var b strings.Builder
	for _, r := range s {
		switch {
		case unicode.IsControl(r) || r == '\u2028' || r == '\u2029' || r == utf8.RuneError:
			b.WriteByte(' ')
		case r == '"' || r == '\\':
			b.WriteByte('\\')
			b.WriteRune(r)
		default:
			b.WriteRune(r)
		}
	}
	out := b.String()
	if len(out) > max {
		out = out[:max]
		for !utf8.ValidString(out) {
			out = out[:len(out)-1]
		}
		// A truncated escape must not consume the closing quote of this member.
		n := 0
		for i := len(out) - 1; i >= 0 && out[i] == '\\'; i-- {
			n++
		}
		if n%2 != 0 {
			out = out[:len(out)-1]
		}
	}
	return out
}
