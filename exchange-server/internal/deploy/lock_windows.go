package deploy

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"golang.org/x/sys/windows"
)

// LockDir takes the exclusive lock of a data directory: store.lock opened with a sharing mode of none,
// so a second server (or an offline command) on the same directory is refused with ErrLocked. The
// returned function releases it; a process that dies releases it too.
func LockDir(dir string) (release func(), err error) {
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return nil, err
	}
	p, err := windows.UTF16PtrFromString(filepath.Join(dir, LockFile))
	if err != nil {
		return nil, err
	}
	h, err := windows.CreateFile(p, windows.GENERIC_READ|windows.GENERIC_WRITE, 0, nil,
		windows.OPEN_ALWAYS, windows.FILE_ATTRIBUTE_NORMAL, 0)
	if err != nil {
		if errors.Is(err, windows.ERROR_SHARING_VIOLATION) {
			return nil, ErrLocked
		}
		return nil, fmt.Errorf("cannot lock the data directory: %w", err)
	}
	return func() { _ = windows.CloseHandle(h) }, nil
}
