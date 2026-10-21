//go:build !windows

package rendezvous

import "os"

// isLinkOrReparsePoint is true for a symlink or any other irregular file: the web directory is served as
// plain files and directories only (WEB-BINDING normative resolution 5). Lstat does not follow it.
func isLinkOrReparsePoint(fi os.FileInfo) bool {
	return fi.Mode()&(os.ModeSymlink|os.ModeIrregular) != 0
}
