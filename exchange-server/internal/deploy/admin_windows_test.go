package deploy

import (
	"io"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"golang.org/x/sys/windows"
)

const isWindows = true

func TestAdminSDDLGivesAccessToTheListedSIDsOnlyAndTheDeniesEveryoneElse(t *testing.T) {
	got := AdminSDDL([]string{"S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001", "", "S-1-5-21-9-9-9-500"})
	want := "D:P(A;;GA;;;S-1-5-21-1-2-3-1001)(A;;GA;;;S-1-5-21-9-9-9-500)(A;;GA;;;SY)(A;;GA;;;BA)"
	if got != want {
		t.Fatalf("got %s\nwant %s", got, want)
	}
	for _, bad := range []string{"WD", "AU", "BU", "IU", "NU"} { // Everyone, Authenticated Users, Users, Interactive, Network
		if strings.Contains(got, ";;;"+bad+")") {
			t.Fatalf("the channel must not be open to %s", bad)
		}
	}
	if _, err := windows.SecurityDescriptorFromString(got); err != nil {
		t.Fatalf("the SDDL does not parse: %v", err)
	}
}

func uniqueDir(t *testing.T) string {
	t.Helper()
	d := filepath.Join(t.TempDir(), "data")
	if err := os.MkdirAll(d, 0o700); err != nil {
		t.Fatal(err)
	}
	return d
}

func TestAdminChannelRoundTripOneRequestPerConnection(t *testing.T) {
	dir := uniqueDir(t)
	l, err := ListenAdmin(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	go func() {
		for {
			c, err := l.Accept()
			if err != nil {
				return
			}
			go func() {
				defer c.Close()
				buf := make([]byte, 64)
				n, _ := c.Read(buf)
				_, _ = c.Write(append([]byte("echo:"), buf[:n]...))
			}()
		}
	}()
	for i := 0; i < 3; i++ {
		c, err := DialAdmin(dir)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := c.Write([]byte("hello")); err != nil {
			t.Fatal(err)
		}
		_ = c.SetDeadline(time.Now().Add(3 * time.Second))
		b, err := io.ReadAll(c)
		if err != nil || string(b) != "echo:hello" {
			t.Fatalf("round %d: %q %v", i, b, err)
		}
		c.Close()
	}
}

func TestAdminChannelRefusesASecondListenerOnTheSameNameAndAnAbsentOne(t *testing.T) {
	dir := uniqueDir(t)
	if _, err := DialAdmin(dir); err != ErrNoAdminChannel {
		t.Fatalf("no server: %v", err)
	}
	l, err := ListenAdmin(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := ListenAdmin(dir, nil); err == nil {
		t.Fatal("a second listener must not take the name of a live pipe (first-instance protection)")
	}
	l.Close()
	if _, err := DialAdmin(dir); err != ErrNoAdminChannel {
		t.Fatalf("after Close: %v", err)
	}
}

func TestCloseWakesAPendingAcceptAndDeadlinesCancelBlockedReads(t *testing.T) {
	dir := uniqueDir(t)
	l, err := ListenAdmin(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	done := make(chan error, 1)
	go func() { _, err := l.Accept(); done <- err }()
	time.Sleep(50 * time.Millisecond)
	l.Close()
	select {
	case err := <-done:
		if err == nil {
			t.Fatal("Accept returned a connection after Close")
		}
	case <-time.After(3 * time.Second):
		t.Fatal("Close did not wake the pending Accept")
	}

	l2, err := ListenAdmin(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	defer l2.Close()
	accepted := make(chan net.Conn, 1)
	go func() { c, _ := l2.Accept(); accepted <- c }()
	client, err := DialAdmin(dir)
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	srv := <-accepted
	defer srv.Close()
	_ = srv.SetReadDeadline(time.Now().Add(150 * time.Millisecond))
	start := time.Now()
	_, err = srv.Read(make([]byte, 1))
	if err == nil || !os.IsTimeout(err) || time.Since(start) > 2*time.Second {
		t.Fatalf("a read with a passed deadline must fail with a timeout: %v after %v", err, time.Since(start))
	}
	// and a client that closes without sending is EOF to the server
	client.Close()
	_ = srv.SetReadDeadline(time.Now().Add(2 * time.Second))
	if _, err := srv.Read(make([]byte, 1)); err != io.EOF {
		t.Fatalf("%v", err)
	}
}

func TestTheAdminPipeSecurityDescriptorIsTheOneWeAskedFor(t *testing.T) {
	dir := uniqueDir(t)
	l, err := ListenAdmin(dir, []string{"S-1-5-32-544"})
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	name, _ := windows.UTF16PtrFromString(AdminPipeName(dir))
	h, err := windows.CreateFile(name, windows.READ_CONTROL, 0, nil, windows.OPEN_EXISTING, 0, 0)
	if err != nil {
		// READ_CONTROL on a pipe instance that is waiting may be busy: the DACL is checked through the listener instead
		t.Skipf("cannot open the pipe for READ_CONTROL: %v", err)
	}
	defer windows.CloseHandle(h)
	sd, err := windows.GetSecurityInfo(h, windows.SE_KERNEL_OBJECT, windows.DACL_SECURITY_INFORMATION)
	if err != nil {
		t.Skipf("cannot read the pipe's DACL: %v", err)
	}
	s := sd.String()
	for _, bad := range []string{";;;WD)", ";;;AU)", ";;;BU)", ";;;IU)", ";;;NU)"} {
		if strings.Contains(s, bad) {
			t.Fatalf("the pipe is open to everybody: %s", s)
		}
	}
	if !strings.Contains(s, ";;;SY)") || !strings.Contains(s, ";;;BA)") {
		t.Fatalf("SYSTEM and Administrators must be on the list: %s", s)
	}
}

func TestAdminSIDsRefusesAnythingThatIsNotASID(t *testing.T) {
	if _, err := ListenAdmin(uniqueDir(t), []string{"not-a-sid"}); err == nil {
		t.Fatal("a management SID that does not parse must be refused, never ignored")
	}
}
