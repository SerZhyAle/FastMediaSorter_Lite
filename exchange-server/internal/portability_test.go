package internal_test

import (
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// TestNoOSLayerOutsideDeploy holds the portability boundary of 133 section 4: a Linux microservice
// replaces internal/deploy (and the *_windows.go files), never the protocol core. No other file may
// import golang.org/x/sys/windows or syscall, or declare itself Windows-only.
func TestNoOSLayerOutsideDeploy(t *testing.T) {
	root, err := filepath.Abs("..")
	if err != nil {
		t.Fatal(err)
	}
	imp := regexp.MustCompile(`(?m)^\s*(?:\w+\s+)?"(golang\.org/x/sys/windows[^"]*|syscall)"\s*$`)
	// A positive windows constraint; "!windows" (the Linux twin of a seam) is not one.
	tag := regexp.MustCompile(`(?m)^//go:build\s+(?:\(?\s*)?windows\b`)
	checked := 0
	err = filepath.Walk(root, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(p, ".go") {
			return err
		}
		rel, _ := filepath.Rel(root, p)
		rel = filepath.ToSlash(rel)
		if strings.HasPrefix(rel, "internal/deploy/") || strings.HasSuffix(rel, "_windows.go") || strings.HasSuffix(rel, "_windows_test.go") {
			return nil
		}
		src, err := os.ReadFile(p)
		if err != nil {
			return err
		}
		checked++
		if m := imp.FindStringSubmatch(string(src)); m != nil {
			t.Errorf("%s imports %s outside internal/deploy", rel, m[1])
		}
		if tag.Match(src) {
			t.Errorf("%s is Windows-only (build tag) outside internal/deploy and *_windows.go", rel)
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	if checked < 10 {
		t.Fatalf("the scan saw only %d files: the root is wrong", checked)
	}
}
