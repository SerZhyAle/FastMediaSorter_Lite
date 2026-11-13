---
paths:
  - "src/Documents/Text*.vb"
  - "src/Main_Form.Text*.vb"
  - "src/TextViewRules.vb"
  - "src/Text_Encoding_Form.vb"
  - "src/ModernViewerPreferences.Text.vb"
  - "src/Localization/Localization.TextReader.vb"
  - "tests/Lite.Tests/Text*.vb"
---

# Text reader and editor: encodings, paging, atomic save, drafts, Markdown

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

Guide: [TEXT_READING.md](../../docs/guides/TEXT_READING.md). Local specification 277 holds the section, decision (`D..`) and `VM-..` ids the code cites. The other files of `src/Documents/` (HTML, PDF, EPUB, the converter) are [documents.md](documents.md).

**Components** (one owner per decision; the `Main_Form` partials only call it)
- Pure owners in [src/Documents/](../../src/Documents): `TextEncodingDetector` (which encoding), `TextFilePager` + `TextFileSearch` (a file over the ceiling), `TextFileSaver` (the only writer of a saved file), `TextDocumentBuffer` (text to lines and per-line terminators), `TextDraftManager`, `TextEncodingMemory` + `TextPositionMemory` (the two history stores), `TextReaderLayout` (rows, palette, find), `TextSyntaxHighlighter`, `TextMarkdownRenderer`, `TextExcerpt`, `TextEditRules` (key to command, which chrome items leave the document). `TextFileReader` is the older ordinary preview: what a file gets while full reading is off.
- Glue: `Main_Form.TextView.vb` (`LoadTextDocument` picks preview or reader), `.TextReader.vb`, `.TextEditor.vb`, `.TextMarkdown.vb`, [TextViewRules.vb](../../src/TextViewRules.vb), `Text_Encoding_Form.vb` (the Re-open dialog), [ModernViewerPreferences.Text.vb](../../src/ModernViewerPreferences.Text.vb). Strings: [Localization.TextReader.vb](../../src/Localization/Localization.TextReader.vb), under [localization.md](localization.md).

**Seams and lists**
- Mainline only. `src/Documents/Text*.vb` open with `#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS`; `Main_Form.Text*.vb`, `TextViewRules.vb`, `Text_Encoding_Form.vb` and `ModernViewerPreferences.Text.vb` are whole-file `#If Not NETFRAMEWORK`. All are in the explicit Compile list of [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) and compile to nothing in the x86 exe: a new Text file goes into that list and into [Lite.Tests.vbproj](../../tests/Lite.Tests/Lite.Tests.vbproj) in the same commit.
- Lite.Tests defines `DOCUMENT_PREVIEW_TESTS` on its net48 leg, so the pure owners are tested under BOTH runtimes. They carry no WinForms reference (`TextEditRules` alone imports `System.Windows.Forms`, for key mapping): keep it so, and keep modern-only APIs out of them. The Share Manager links none of these files.

**Reading and encodings**
- **Opt-ins are off and layered**: `FullTextReaderEnabled`, then `TextEditingEnabled` and `TextTranslationEnabled`; the two count only while the full reader is on. Off means the unchanged preview.
- **Detection order is fixed**, in `TextEncodingDetector.Detect`: user override, byte-order mark, NUL / control-byte binary check, strict UTF-8, a ranking over the legacy code pages, then the system ANSI page below `FallbackConfidence` 0.5. The probe is `ProbeBytes` (64 KiB). No other code guesses an encoding; Re-open calls `DetectWithOverride`.
- **A binary file** is shown only on request, one byte as one character (code page 28591) with control bytes drawn as signs, and is never edited, excerpted or drafted.
- **Ceiling**: `TextEditingMaxMb`. A larger file is read by `TextFilePager` in windows of `TextReadWindowMb`: read-only, no highlighting, with a background whole-file search whose count says what it covered (`TextFileSearch`).
- **`.ts` is not a text extension** (`MEDIA-CLASSIFICATION` rule 1); the only way to text for such a file is "Open as text", offered with full reading on and never for a stream or a secure container.
- **Histories are plain text files** with a header, under `%LOCALAPPDATA%\SZA\FastMediaSorter\documents\` (`text-encodings-v1.txt`, `text-positions-v1.txt`). The encoding memory key is path + provenance + write time + length (a changed file forgets); the position memory key is path + provenance only. A changed layout needs a new `-v2` name, not an edit of `-v1`.

**Editing and saving** (specification 277 D05)
- **Refusals** (`BeginTextEdit`): a paged file, a binary view, a file inside an archive or its cache, a server file or remote cache, a secure container. A file the encoding cannot give back byte for byte (decode, then strict encode) is refused too, and so is a text the RichEdit surface itself would change: a save would rewrite what nobody touched.
- **`TextFileSaver.Save` is the only writer**: strict encode, snapshot check (length + write time, else SHA-256), a sibling temp `.fms-tmp` flushed to disk, an exclusive-open probe, a second check, then `File.Replace` (with a move-aside fallback). Never write the target in place. A character the encoding cannot hold stops the save with nothing written; only the user's explicit word may substitute it or move to UTF-8.
- **Encoding, BOM and every line terminator survive**: the editor works on `TextDocumentBuffer`, which composes the saved bytes from the original terminators (`ComposeText`, `AlignLines`); the RichEdit text is never the file.
- **Backups** are `.~bak` or `.bak1..N` (`BackupName`, `RotateBackups`): optional and bounded.
- **One exit guard, `TextEditorAllowsLeaving`**: navigation, opening another file, move and delete, close and the chrome items all go through it; a new way out of the document calls it. `TextEditorOwnsInput` keeps viewer hotkeys away from a typing editor. The shutdown path writes a draft, or stops the shutdown and names the file (`TextEditStopsShutdown`).
- **Excerpt** (Ctrl+Shift+S on a paged file): `<name> (excerpt).<ext>` beside its source (`CAPTURE-OUTPUT` 0.7 section 5), in the source encoding, never from a binary view.

**Drafts** (D06)
- DPAPI CurrentUser; a versioned binary record (`Magic`, `FormatVersion`); files `*.fms-draft` in `%LOCALAPPDATA%\SZA\FastMediaSorter\text-drafts`, named by a hash of the path. `TextDraftManager.Allowed` admits only local volumes: never an archive, a remote cache, a secure container or a shared folder.
- A draft is removed only by a confirmed save or by the user. One that cannot be read (another user, damaged, written by a later version) is kept, not deleted: a changed record layout bumps `FormatVersion`, and an older reader leaves the newer draft alone.
- A draft is offered on open; "Not now" keeps it, and editing waits until it is answered.

**Markdown, highlighting, search** (bounded on purpose)
- `TextMarkdownRenderer` builds ONE static page by construction: everything escaped, links through the allow-list `SafeLinkTarget`, no images, no script, nothing remote; it is shown on the HTML host from memory. The text document stays loaded underneath, and editing is always on the source (`BeginTextEdit` switches back to source first).
- The highlighter and both searches (reader `TextReaderLayout`, editor `TextEditRules`) carry size, time and match-count limits as constants. Do not lift a bound without a test that measures it.

**Translation**: the menu reuses the doc-html-translate coordinator of [documents.md](documents.md) (`BeginTextSnapshotConversion`, `DocHtmlSnapshot`), NOT `src/Translate/`. The selection or the editor text is an immutable copy taken at the click; the file is not saved first; text from a secure container is never sent; a translation never replaces the text.

**Preferences**: the stored names are `TextPreferenceNames`; each must also pass `ProfileResetScope.IsResetName`, and `TextReaderPreferencesTests` pins their count. `NormalizeTextPreferences` clamps the ranges. A new name is all three edits.

**Threading**: disk reads, regex work, whole-file search, draft I/O, save, replace-all and excerpts run in `Task.Run`; the UI half applies the result. Do not touch the RichEdit from the pool.

**Hotkeys**: Ctrl+E edit, Ctrl+S save, Ctrl+Shift+S save as (editing) or excerpt (paged file), Ctrl+H replace, Ctrl+G line, Ctrl+F find, F3 / Shift+F3 step, Ctrl+Shift+M Markdown source / page, Ctrl+Shift+E re-open with another encoding.

**Tests**: `tests/Lite.Tests/Text*.vb` hold the pure owners; they belong to the viewer suite, not to the between-release checks.
