package deploy

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestProtectDirLeavesOnlyOwnerSystemAdministrators(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "tls")
	if err := os.Mkdir(dir, 0o700); err != nil {
		t.Fatal(err)
	}
	if err := ProtectDir(dir); err != nil {
		t.Fatal(err)
	}
	sddl, err := DirSDDL(dir)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(sddl, "D:P") {
		t.Errorf("the DACL must be protected from inheritance: %s", sddl)
	}
	// Exactly three ACEs: the current user, SYSTEM (SY) and Administrators (BA).
	if n := strings.Count(sddl, "(A;"); n != 3 {
		t.Errorf("want 3 allow ACEs, got %d in %s", n, sddl)
	}
	for _, other := range []string{";;;BU)", ";;;WD)", ";;;AU)", ";;;IU)"} { // Users, Everyone, Authenticated, Interactive
		if strings.Contains(sddl, other) {
			t.Errorf("a broad group still has access (%s): %s", other, sddl)
		}
	}
	// The directory still works for its owner: a key file can be created inside.
	f := filepath.Join(dir, "key.pem")
	if err := os.WriteFile(f, []byte("x"), 0o600); err != nil {
		t.Fatalf("the owner lost access: %v", err)
	}
}
