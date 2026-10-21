package rendezvous

import (
	"os"
	"syscall"
)

// isLinkOrReparsePoint is true for a symlink, a junction (mount point) or any other reparse point: the
// web directory is served as plain files and directories only (WEB-BINDING normative resolution 5).
// Lstat does not follow it, so the attribute is the file system's own word for it.
func isLinkOrReparsePoint(fi os.FileInfo) bool {
	if fi.Mode()&(os.ModeSymlink|os.ModeIrregular) != 0 {
		return true
	}
	if d, ok := fi.Sys().(*syscall.Win32FileAttributeData); ok {
		return d.FileAttributes&syscall.FILE_ATTRIBUTE_REPARSE_POINT != 0
	}
	return false
}
