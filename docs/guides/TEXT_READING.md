# Text reading and editing

The 64-bit viewer can read and edit text files in the same window as file sorting. Both are off by
default: Settings (F2) > Documents > Text files > Full reading of text files, and Editing text files
beside it, which works only with reading on. While reading is off, a text file opens in the ordinary
text preview, unchanged. The 32-bit viewer has neither and keeps the preview. Design and acceptance:
specification 277 (local).

## Reading

The reader takes every file the viewer opens as text. Fonts, theme,
line numbers, tab width, line spacing, visible whitespace, the status line and syntax colours are set
in the Text files groups of the Documents page. Syntax colours cover JSON, XML, YAML, Markdown, INI
and logs; a large file is shown without them. A Markdown file opens as source or as a formatted page,
and Ctrl+Shift+M switches between the two. The formatted page runs no script and loads nothing remote.

Ctrl+F finds in the file with case, whole-word and regular-expression switches, F3 and Shift+F3 step
through the hits, and Ctrl+G goes to a line. The viewer can remember the position in each file.

A file larger than the editing limit is read in parts of the size set by "Part size of a large file, MB".
It cannot be edited here, and the document panel offers the two ways on: save the selection as a new
file (Ctrl+Shift+S), which writes `<name> (excerpt).<ext>` beside it in the source encoding, or open
the file in an external program. Search on such a file runs in the background over the whole file, and
its count names what it covered.

## Encodings

A file with a byte-order mark or valid UTF-8 opens as what it is. Any other file is matched against
Windows-1251, CP866, KOI8-R, KOI8-U, Windows-1252, ISO-8859-1, Shift_JIS, GBK, Big5 and EUC-KR, and
opens in the one its text reads naturally in. A file with too little to judge opens in the encoding
chosen under "Encoding when detection fails". Ctrl+Shift+E offers another encoding with a preview
of the first lines and a count of what that encoding cannot read. Nothing is written to the file. The
choice can be remembered until the file changes.

## Open as text

"Open as text" in the file menu of an image or a video shows this file in the reader, this once; the
next file goes by its kind again. It is offered with full reading on, and not for a stream or a secure
container. A file that looks binary says so first. It is then shown for viewing only: one byte is one
character, control bytes are drawn as signs, and editing, saving an excerpt and the draft are refused.

## Editing

With editing on, Ctrl+E or the Edit button edits a file on this computer or in a shared folder. A file
inside an archive, on a server, in a secure container or in a stream is read-only. Ctrl+S saves, Ctrl+Shift+S saves as,
Ctrl+H replaces, Ctrl+G goes to a line, and the panel picks the line ending. Leaving a file with unsaved
changes, by moving to another file, moving or deleting it or closing the window, asks first.

A save writes a temporary file beside the original and replaces the original in one step, so a failure
leaves the original whole. The encoding, byte-order mark and line endings stay what they were; a
character the encoding cannot hold stops the save with nothing written, and the question names it and offers UTF-8 or a replacement sign. A file changed or removed by another
program since it was opened is reported before anything is overwritten. A backup copy beside the file is
optional, with a limit on versions per file.

Drafts of unsaved changes are written on a timer for files on this computer, encrypted for the current
Windows user and kept in the program's data folder. No draft is kept for a shared folder. After a crash,
opening the file offers to restore its draft; "Not now" keeps the reader and the draft, and editing waits
until the draft is answered. When Windows shuts down, the changes go to a draft; where no draft can hold
them, the shutdown is stopped and the viewer says which file to save.

## Data

Remembered positions and encodings stay on this computer. "Clear the text file history" deletes them without
touching the files or the general settings. Nothing is uploaded.

Product naming follows [PRODUCT_NAMING.md](PRODUCT_NAMING.md).
