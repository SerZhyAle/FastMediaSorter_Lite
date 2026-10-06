package main

import (
	"errors"
	"io"
	"io/fs"
	"os"
	"path"
	"strings"

	"github.com/pkg/sftp"
)

// rootedHandlers serves one directory through os.Root: every path a client sends - relative,
// absolute, with `..` or through a symlink - is resolved inside that directory or refused. An
// SFTP server with only a working directory would let an absolute path leave the folder, and a
// by-hand proof with a real client must not be able to touch anything outside the scratch folder.
type rootedHandlers struct{ root *os.Root }

func newRootedHandlers(dir string) (sftp.Handlers, *os.Root, error) {
	r, err := os.OpenRoot(dir)
	if err != nil {
		return sftp.Handlers{}, nil, err
	}
	h := &rootedHandlers{root: r}
	return sftp.Handlers{FileGet: h, FilePut: h, FileCmd: h, FileList: h}, r, nil
}

// name turns a client path ("/a/b", "a/../b", "/") into a name relative to the root.
func name(p string) string {
	p = path.Clean("/" + strings.ReplaceAll(p, "\\", "/"))
	p = strings.TrimPrefix(p, "/")
	if p == "" {
		return "."
	}
	return p
}

func (h *rootedHandlers) Fileread(r *sftp.Request) (io.ReaderAt, error) {
	f, err := h.root.Open(name(r.Filepath))
	if err != nil {
		return nil, err
	}
	return f, nil
}

func (h *rootedHandlers) Filewrite(r *sftp.Request) (io.WriterAt, error) {
	flags := os.O_WRONLY | os.O_CREATE
	if f := r.Pflags(); f.Trunc {
		flags |= os.O_TRUNC
	} else if f.Excl {
		flags |= os.O_EXCL
	}
	f, err := h.root.OpenFile(name(r.Filepath), flags, 0o644)
	if err != nil {
		return nil, err
	}
	return f, nil
}

func (h *rootedHandlers) Filecmd(r *sftp.Request) error {
	n := name(r.Filepath)
	switch r.Method {
	case "Setstat":
		return nil // times and modes are not part of this proof
	case "Rename":
		return h.root.Rename(n, name(r.Target))
	case "Rmdir", "Remove":
		return h.root.Remove(n)
	case "Mkdir":
		return h.root.Mkdir(n, 0o755)
	default: // Symlink, Link: a link could point out of the folder
		return sftp.ErrSSHFxOpUnsupported
	}
}

func (h *rootedHandlers) Filelist(r *sftp.Request) (sftp.ListerAt, error) {
	n := name(r.Filepath)
	switch r.Method {
	case "List":
		d, err := h.root.Open(n)
		if err != nil {
			return nil, err
		}
		defer d.Close()
		entries, err := d.ReadDir(-1)
		if err != nil {
			return nil, err
		}
		infos := make([]os.FileInfo, 0, len(entries))
		for _, e := range entries {
			if fi, err := e.Info(); err == nil {
				infos = append(infos, fi)
			}
		}
		return lister(infos), nil
	case "Stat", "Lstat":
		fi, err := h.root.Stat(n)
		if r.Method == "Lstat" {
			fi, err = h.root.Lstat(n)
		}
		if err != nil {
			return nil, err
		}
		return lister([]os.FileInfo{fi}), nil
	default:
		return nil, errors.New("unsupported")
	}
}

type lister []fs.FileInfo

func (l lister) ListAt(out []os.FileInfo, off int64) (int, error) {
	if off >= int64(len(l)) {
		return 0, io.EOF
	}
	n := copy(out, l[off:])
	if int64(n)+off >= int64(len(l)) {
		return n, io.EOF
	}
	return n, nil
}
