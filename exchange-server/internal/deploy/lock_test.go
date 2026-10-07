package deploy

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func TestTwoServersOnOneDirectoryAreRefusedAndTheLockDiesWithTheHolder(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "data") // the directory need not exist yet
	release, err := LockDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := LockDir(dir); !errors.Is(err, ErrLocked) {
		t.Fatalf("a second server on the same directory must be refused: %v", err)
	}
	// another directory is another lock
	other, err := LockDir(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	other()
	release()
	again, err := LockDir(dir)
	if err != nil {
		t.Fatalf("the lock must be free after the holder let go: %v", err)
	}
	again()
	// the lock file is the one extra file the directory holds, and it stays empty
	st, err := os.Stat(filepath.Join(dir, LockFile))
	if err != nil || st.Size() != 0 {
		t.Fatalf("%v %v", st, err)
	}
}

func TestAdminPipeNameVector(t *testing.T) {
	// pinned so the Share Manager can check its own computation (computed independently with PowerShell): the first 4 bytes of SHA-256 over the
	// lower-cased absolute path of the data directory, as 8 lower-case hex characters
	const want = `\\.\pipe\fms-exchange-admin-afface35`
	for _, p := range []string{`C:\ProgramData\FastMediaSorterExchange`, `c:\programdata\fastmediasorterexchange`, `C:\ProgramData\.\FastMediaSorterExchange`} {
		if got := AdminPipeName(p); got != want && isWindows {
			t.Errorf("%s: got %s, want %s", p, got, want)
		}
	}
	if AdminPipeName(`C:\a`) == AdminPipeName(`C:\b`) {
		t.Fatal("two directories, one pipe")
	}
}
