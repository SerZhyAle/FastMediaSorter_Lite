package deploy

import (
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"strings"
	"sync"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
)

// The local admin channel on Windows: a named pipe with an explicit access list (ticket 162 section
// 4). The data directory keeps 144's ACL - the enrolled user gets nothing there - so the channel's
// permission is the pipe's own: the owner of the data directory, the account the server runs as,
// SYSTEM and Administrators, plus the management SIDs the Server edition's service is given (the
// installer enrols them, as it does for the worker). Remote clients are rejected, and the first
// instance is created with FILE_FLAG_FIRST_PIPE_INSTANCE so no other process can squat the name.

// AdminSDDL is the security descriptor of the pipe: protected (no inheritance), full access to each
// SID given and to nobody else. It is a plain function of its inputs so a test pins its shape.
func AdminSDDL(sids []string) string {
	seen := map[string]bool{}
	var b strings.Builder
	b.WriteString("D:P")
	for _, s := range append(append([]string{}, sids...), "SY", "BA") {
		if s == "" || seen[s] {
			continue
		}
		seen[s] = true
		b.WriteString("(A;;GA;;;" + s + ")")
	}
	return b.String()
}

// adminSIDs collects who may talk to the channel: the owner of the data directory, the account this
// process runs as, and the extra management SIDs.
func adminSIDs(dataDir string, extra []string) ([]string, error) {
	var out []string
	if tu, err := windows.GetCurrentProcessToken().GetTokenUser(); err == nil {
		out = append(out, tu.User.Sid.String())
	} else {
		return nil, fmt.Errorf("cannot read the current user: %w", err)
	}
	if sd, err := windows.GetNamedSecurityInfo(dataDir, windows.SE_FILE_OBJECT, windows.OWNER_SECURITY_INFORMATION); err == nil {
		if owner, _, err := sd.Owner(); err == nil && owner != nil {
			out = append(out, owner.String())
		}
	}
	for _, s := range extra {
		if _, err := windows.StringToSid(s); err != nil {
			return nil, fmt.Errorf("%q is not a SID: %w", s, err)
		}
		out = append(out, s)
	}
	return out, nil
}

type pipeListener struct {
	name   *uint16
	path   string
	sa     *windows.SecurityAttributes
	mu     sync.Mutex
	next   windows.Handle // an instance created ahead, so the name is never free between two Accepts
	closed bool
}

// ListenAdmin creates the pipe of a data directory and returns a listener on it.
func ListenAdmin(dataDir string, extraSIDs []string) (net.Listener, error) {
	sids, err := adminSIDs(dataDir, extraSIDs)
	if err != nil {
		return nil, err
	}
	sd, err := windows.SecurityDescriptorFromString(AdminSDDL(sids))
	if err != nil {
		return nil, err
	}
	sa := &windows.SecurityAttributes{SecurityDescriptor: sd}
	sa.Length = uint32(unsafe.Sizeof(*sa))
	path := AdminPipeName(dataDir)
	name, err := windows.UTF16PtrFromString(path)
	if err != nil {
		return nil, err
	}
	l := &pipeListener{name: name, path: path, sa: sa}
	h, err := l.newInstance(true)
	if err != nil {
		return nil, fmt.Errorf("cannot create the admin pipe: %w", err)
	}
	l.next = h
	return l, nil
}

func (l *pipeListener) newInstance(first bool) (windows.Handle, error) {
	flags := uint32(windows.PIPE_ACCESS_DUPLEX)
	if first {
		flags |= windows.FILE_FLAG_FIRST_PIPE_INSTANCE
	}
	return windows.CreateNamedPipe(l.name, flags,
		windows.PIPE_TYPE_BYTE|windows.PIPE_READMODE_BYTE|windows.PIPE_WAIT|windows.PIPE_REJECT_REMOTE_CLIENTS,
		windows.PIPE_UNLIMITED_INSTANCES, 64<<10, 64<<10, 0, l.sa)
}

func (l *pipeListener) Accept() (net.Conn, error) {
	l.mu.Lock()
	if l.closed {
		l.mu.Unlock()
		return nil, net.ErrClosed
	}
	h := l.next
	l.next = 0
	l.mu.Unlock()
	if h == 0 {
		var err error
		if h, err = l.newInstance(false); err != nil {
			return nil, err
		}
	}
	err := windows.ConnectNamedPipe(h, nil)
	if err != nil && !errors.Is(err, windows.ERROR_PIPE_CONNECTED) {
		_ = windows.CloseHandle(h)
		return nil, err
	}
	l.mu.Lock()
	closed := l.closed
	l.mu.Unlock()
	if closed { // Close woke this Accept with a connection of its own
		_ = windows.DisconnectNamedPipe(h)
		_ = windows.CloseHandle(h)
		return nil, net.ErrClosed
	}
	return &pipeConn{h: h, server: true}, nil
}

// Close stops the listener. A pending Accept is woken by a connection of the listener's own.
func (l *pipeListener) Close() error {
	l.mu.Lock()
	if l.closed {
		l.mu.Unlock()
		return nil
	}
	l.closed = true
	next := l.next
	l.next = 0
	l.mu.Unlock()
	if next != 0 {
		_ = windows.CloseHandle(next)
	}
	// wake an Accept that is blocked in ConnectNamedPipe
	if h, err := windows.CreateFile(l.name, windows.GENERIC_READ|windows.GENERIC_WRITE, 0, nil, windows.OPEN_EXISTING, 0, 0); err == nil {
		_ = windows.CloseHandle(h)
	}
	return nil
}

func (l *pipeListener) Addr() net.Addr { return pipeAddr(l.path) }

type pipeAddr string

func (a pipeAddr) Network() string { return "pipe" }
func (a pipeAddr) String() string  { return string(a) }

// DialAdmin connects to the running server's admin pipe, or returns ErrNoAdminChannel.
func DialAdmin(dataDir string) (net.Conn, error) {
	name, err := windows.UTF16PtrFromString(AdminPipeName(dataDir))
	if err != nil {
		return nil, err
	}
	deadline := time.Now().Add(2 * time.Second)
	for {
		h, err := windows.CreateFile(name, windows.GENERIC_READ|windows.GENERIC_WRITE, 0, nil, windows.OPEN_EXISTING, 0, 0)
		if err == nil {
			return &pipeConn{h: h}, nil
		}
		switch {
		case errors.Is(err, windows.ERROR_FILE_NOT_FOUND):
			return nil, ErrNoAdminChannel
		case errors.Is(err, windows.ERROR_PIPE_BUSY) && time.Now().Before(deadline):
			time.Sleep(20 * time.Millisecond)
		case errors.Is(err, windows.ERROR_ACCESS_DENIED):
			return nil, fmt.Errorf("this user may not use the admin channel (access denied): %w", err)
		default:
			return nil, err
		}
	}
}

// pipeConn is one end of a byte-mode pipe as a net.Conn, with blocking I/O whose deadlines cancel the
// pending operation.
type pipeConn struct {
	h      windows.Handle
	server bool

	mu       sync.Mutex
	deadline time.Time
	timer    *time.Timer
	expired  bool
	closed   bool
}

func (c *pipeConn) Read(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	if err := c.check(); err != nil {
		return 0, err
	}
	var n uint32
	err := windows.ReadFile(c.h, p, &n, nil)
	if err != nil {
		return int(n), c.mapErr(err)
	}
	return int(n), nil
}

func (c *pipeConn) Write(p []byte) (int, error) {
	if err := c.check(); err != nil {
		return 0, err
	}
	total := 0
	for total < len(p) {
		var n uint32
		if err := windows.WriteFile(c.h, p[total:], &n, nil); err != nil {
			return total, c.mapErr(err)
		}
		total += int(n)
	}
	return total, nil
}

func (c *pipeConn) check() error {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.closed {
		return net.ErrClosed
	}
	if !c.deadline.IsZero() && !time.Now().Before(c.deadline) {
		c.expired = true
		return os.ErrDeadlineExceeded
	}
	return nil
}

func (c *pipeConn) mapErr(err error) error {
	c.mu.Lock()
	expired, closed := c.expired, c.closed
	c.mu.Unlock()
	switch {
	case errors.Is(err, windows.ERROR_BROKEN_PIPE), errors.Is(err, windows.ERROR_PIPE_NOT_CONNECTED), errors.Is(err, windows.ERROR_NO_DATA):
		return io.EOF
	case errors.Is(err, windows.ERROR_OPERATION_ABORTED) && expired:
		return os.ErrDeadlineExceeded
	case closed:
		return net.ErrClosed
	}
	return err
}

func (c *pipeConn) Close() error {
	c.mu.Lock()
	if c.closed {
		c.mu.Unlock()
		return nil
	}
	c.closed = true
	if c.timer != nil {
		c.timer.Stop()
	}
	c.mu.Unlock()
	_ = windows.CancelIoEx(c.h, nil)
	// A server end flushes before it disconnects, so the answer reaches the client (a disconnect throws
	// unread data away); the flush waits for the client, so it is bounded - Close returns within two
	// seconds and the cleanup finishes on its own goroutine.
	done := make(chan struct{})
	h, server := c.h, c.server
	go func() {
		defer close(done)
		if server {
			_ = windows.FlushFileBuffers(h)
			_ = windows.DisconnectNamedPipe(h)
		}
		_ = windows.CloseHandle(h)
	}()
	select {
	case <-done:
	case <-time.After(2 * time.Second):
	}
	return nil
}

func (c *pipeConn) SetDeadline(t time.Time) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.deadline = t
	c.expired = false
	if c.timer != nil {
		c.timer.Stop()
		c.timer = nil
	}
	if t.IsZero() {
		return nil
	}
	d := time.Until(t)
	if d <= 0 {
		d = time.Millisecond
	}
	h := c.h
	c.timer = time.AfterFunc(d, func() {
		c.mu.Lock()
		if c.closed {
			c.mu.Unlock()
			return
		}
		c.expired = true
		c.mu.Unlock()
		_ = windows.CancelIoEx(h, nil) // the blocked ReadFile or WriteFile returns ERROR_OPERATION_ABORTED
	})
	return nil
}

func (c *pipeConn) SetReadDeadline(t time.Time) error  { return c.SetDeadline(t) }
func (c *pipeConn) SetWriteDeadline(t time.Time) error { return c.SetDeadline(t) }
func (c *pipeConn) LocalAddr() net.Addr                { return pipeAddr("local") }
func (c *pipeConn) RemoteAddr() net.Addr               { return pipeAddr("remote") }
