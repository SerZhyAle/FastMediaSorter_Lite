---
paths:
  - "src/Documents/Doc*.vb"
  - "src/Documents/Epub*.vb"
  - "src/Documents/Html*.vb"
  - "src/Documents/Pdf*.vb"
  - "src/Main_Form.Document*.vb"
  - "src/Main_Form.EpubReader.vb"
  - "src/Main_Form.HtmlView.vb"
  - "src/Main_Form.Pdf*.vb"
  - "src/Main_Form.BrowserTranslate.vb"
  - "src/DocHtmlTranslateLauncher.vb"
  - "src/Table_Form.Documents.vb"
  - "src/ModernViewerPreferences.Documents.vb"
  - "src/ModernViewerPreferences.Html.vb"
  - "src/Localization/Localization.Html*.vb"
  - "src/Localization/Localization.Documents.vb"
  - "tests/Lite.Tests/Doc*.vb"
  - "tests/Lite.Tests/Html*.vb"
  - "tests/Lite.Tests/Epub*.vb"
  - "tests/Lite.Tests/Pdf*.vb"
  - "tests/Integration/HtmlViewerScenes.ps1"
---

# Document viewing (PDF, EPUB, HTML pages, doc-html-translate)

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

Design: local specifications 274, 275, 276 and 280. User-facing description: [DOCUMENT_READING.md](../../docs/guides/DOCUMENT_READING.md), [HTML_PAGE_VIEWER.md](../../docs/guides/HTML_PAGE_VIEWER.md). The plain-text reader (`Text*.vb`, the text rows of `Table_Form.Documents.vb`) is [text-reader.md](text-reader.md); the floating panel itself is in [viewer-chrome-dialogs.md](viewer-chrome-dialogs.md); the Settings window is [settings-window.md](settings-window.md).

**Seams and project lists**
- **Mainline only.** Every file here is whole-file `#If Not NETFRAMEWORK`, with two exceptions that compile in both exes and carry inline seams: `Main_Form.BrowserTranslate.vb` and `DocHtmlTranslateLauncher.vb` (the image route). Six pure files open with `#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS` and so also compile on the net48 **test** leg (`DocumentContentPolicy`, `DocumentReadingState`, `EpubMetadata`, `EpubPackage`, `HtmlDocumentModels`, `HtmlDocumentPolicy`): they may use only APIs present on both runtimes, and no WinForms or WebView2.
- The net48 `FastMediaSorter.vbproj` lists every file by hand (they compile to nothing there) and `Lite.Tests.vbproj` links them by hand too. A new file needs **both** entries. The hosts (`HtmlDocumentHost`, `HtmlRestrictedHost`, `HtmlRuntimeService`), `DocumentSpeech` and every `Main_Form.*` partial are NOT linked into Lite.Tests, so they have no unit test; the Share Manager project links none of this area.
- The shared load path reaches the area through one call inside `#If Not NETFRAMEWORK` in `Main_Form.MediaLoading.vb` (`TryLoadDocumentContent`).

**Routing (one decision per layer)**
- `DocumentContentPolicy.SelectRoute` (pure) picks PDF reader / PDF preview / EPUB reader / EPUB metadata from the extension and the two switches; `TryLoadDocumentContent` in `Main_Form.DocumentDispatch.vb` then tries the HTML view, then the text reader. Do not add a second extension test elsewhere. A disabled reader never probes or constructs a runtime; HTML is off by default (`HtmlViewerEnabled`), and then the file is plain text and no browser component starts.
- Every loader releases the others (`ReleasePdfDocument`, `ReleaseEpubReader`, `ReleaseHtmlView`); the same trio runs before a file operation and at shutdown. A new reader joins all three call sites or it holds a file open during move/delete.

**HTML host - what a page may do (a boundary, keep it closed)**
- Two WebView2 hosts: `HtmlDocumentHost` (the HTML viewer, local and online profiles; `HtmlDocumentSanitizer` + `HtmlDocumentPolicy`) and `HtmlRestrictedHost` (the EPUB reader; its own `HtmlReaderContent.Sanitize` and policy). They share only `HtmlRuntimeService`: one loader folder per process (`PrepareLoader`, no PATH fallback), one environment per profile, and **local and online profiles never share a user-data folder**. The Edge WebView2 Runtime is never bundled, downloaded or installed; detection runs only on an explicit request (`MinimumRuntimeMajor` is a refusal floor, never an install recommendation). The platform gate is `PlatformOutcomeFor`, an explicit client/server/edition table, not a build-number check.
- Page script, host objects, web messages, dev tools, pop-ups, downloads, permissions, basic auth, client certificates, certificate errors, external-scheme launches and frame navigation are all off or cancelled in `ConfigureCore`; every request is answered by the host (`WebResourceRequested` on `*`: no listener, no folder mapping, no `file:`, no fall-through to DNS). Adding a handler that lets one through is a security change.
- Each session has a private origin `https://s<32 hex>.fms-doc.invalid/`. A request is authorized from the RAW request text (`HtmlDocumentPolicy.ParseRequestKey`), never from `Uri.AbsolutePath` (it canonicalizes `%2e%2e`). A file is authorized on the OPENED handle (`HtmlFileAccess`: final path under the canonical root, no reparse point, not a directory, case-sensitive trees refused), never by `File.Exists` plus a prefix test. The root is the entry's own folder unless an explicit root is given (a converter result).
- Responses carry `ResponseHeaders` (CSP with `default-src 'none'` and `script-src 'none'`, no frames, objects or forms; `nosniff`; `no-store`; `no-referrer`; Permissions-Policy). Approved remote origins only widen style/img/font, at most ten (`MaxApprovedOrigins`), **for the session only, never stored**; an imported profile cannot grant an origin or a root. Byte and count budgets are constants of `HtmlDocumentPolicy`.
- Host-run script is fixed code with values passed as JSON (`AppearanceScript`, `PositionScript`); never splice a user value into script or CSS text.
- Navigation has one classifier, `ClassifyNavigation`: only http(s) and `mailto:` leave the host; `javascript:`, `data:`, `file:`, `blob:`, executables and foreign-session hosts are dropped. A click on an external link asks first (`HtmlExternalLinks` = ask | block), then goes to the system browser through `HtmlBrowserLauncher` (the resolved browser exe with one structured argument, never the `.html` association).
- Online pages are a second switch (`HtmlOnlinePagesEnabled`, needs the first). The only network path is `HtmlNetworkFetcher`: public unicast addresses only (a mixed DNS answer is refused whole and checked again at connect time), ports 80/443, no proxy, cookies or credentials, at most 5 redirects (`MaxRedirects`), each confirmed. Never route a page fetch through another HTTP client of the app.
- Never log an engine exception message or a URL (it can carry document text, a query or a password); outcomes are typed `HtmlOutcome` values mapped by `HtmlViewerText.Outcome` to a sentence without path, query or stack. A new outcome needs its sentence and its 13-language row.
- A `.fd-sec` container never gets a rendered copy: HTML falls back to the text view with a notice (`SecureRefused`, before any filesystem work), the EPUB reader and the converter refuse.

**Readers**
- PDF: Docnet.Core (PDFium). `PdfReaderNative` is called only by `PdfDocumentSource`, under its process-wide `NativeGate`. The full reader works on an owned snapshot in `%TEMP%\fms-document-sessions` (1 GiB cap), so move/delete never waits on a render. The password lives in the open session only (`Ask_Dialog` with `IsPassword`), never in settings or logs. A missing PDFium is `OpenFailure.RendererUnavailable`, not a crash.
- EPUB: `EpubPackage` reads an owned `DocumentSnapshot` copy (same temp folder); nothing is extracted to disk, resource keys go through `EpubPackage.ResolveKey`, only a fixed set of media types is served, DRM is refused with its own message. `EpubMetadata` (cover, title, author) is the preview used when the reader is off.
- A document is identified by `DocumentIdentity` = path + write time + length + provenance; archive and resource modes build a synthetic identity in `DocumentReaderIdentity`. A changed file never inherits the old position. A slideshow neither restores nor records positions.

**Stored data**
- PDF/EPUB state is three registry JSON values (`DocumentPageHistory`, `DocumentBookmarks`, `DocumentAppearanceHistory`, each at most 8 MiB; `HistoryLimit` 500 positions, `BookmarkLimit` 2000 bookmarks), cut from the ordinary settings export. A new `Document*`/`Pdf*`/`Epub*` preference must join `documentPreferenceNames` AND `NormalizeDocumentPreferences`; per-document overrides exist only for the names in `appearanceNames`.
- HTML state is `%LOCALAPPDATA%\SZA\FastMediaSorter\documents\html-state-v1.json` (`HtmlReaderState`): no page text, query, selection or cookies - the entry type has no field for them. A file it does not trust (bad JSON, another `SchemaVersion`, too large) puts the store in recovery lock: kept byte for byte, never overwritten, only Clear replaces it; a new field means a new `SchemaVersion` and file name. HTML preferences are `HtmlPreferenceNames`; WebView profiles live under `documents\webview\` and are swept at launch.

**doc-html-translate document route (276)**
- `DocumentConverterEnabled` off keeps the old handoff (`DocHtmlTranslate.BuildArguments`). On, **`DocHtmlJobArguments.Build` is the only command-line builder**: an `ArgumentList` (never hand-quoted), absolute paths only, the input last, `-noopen` so the viewer opens only a verified result. Paid mode (`-google`) exists only with a per-run cost cap and the user's answer for that run; without a cap the converter's own window (`doc-html-ui.exe`, never given the document) is offered instead.
- One owned job at a time; it ends with the viewer and with a stop, and only the process the viewer started is ever killed - never one found by name. Folders under `%LOCALAPPDATA%\SZA\FastMediaSorter`: `document-convert-cache` (one per job, never beside the document) and `document-convert-input` (editor snapshots, apart so the cache trim never sees them). Console output is drained and dropped; only job id, mode and outcome reach the log.
- The result is evidence, not proof: `DocHtmlResultReader` finds the single child carrying the record `.doc-html-translate.json` (schema 1; a higher one means not completed), then checks that the entry lies inside the folder, has no reparse point and the tree is bounded. Only then does the page open - in the browser or in the HTML host rooted at the result folder - and a shown page is leased in its `.fms-document-job.json` ticket so cleanup skips it.
- Archives, resources and `.fd-sec` content are refused. The image route (`DocHtmlTranslateLauncher.vb`, `Main_Form.BrowserTranslate.vb`) is owned by the converter product and frozen by `OCR-INVOCATION` 1.1 - never change it here; the document route cites `OCR-INVOCATION` 1.2 items B (launch), C (record), D (outcome), G (cost) and H (no detach).

**Panel and input**
- `DocumentPanelRules` is the pure model (item ids, groups, pinned core, conditional items); captions are localization KEYS (Russian source strings). The glue is `Main_Form.DocumentPanel.vb`; a reader hands over its items and owns nothing of the panel.
- Inside an open page the page owns the keys (no sorting, no Delete) and the wheel; Escape clears the find, then returns to the canvas (`HtmlViewOwnsInput`). Keep PDF, EPUB and HTML consistent when changing a reader key.

**Tests**
- Lite.Tests classes: `DocHtml*Tests` (converter), `DocumentPanelRulesTests`, `DocumentPanelSurfaceTests` (`Kind=UiForm`, so it runs through `tools/Test-UiForms.ps1` only), `DocumentReadingStateTests`, `Epub*Tests`, `Html*Tests` (policy, sanitizer, local source, fetcher, adapters, state), `Pdf*Tests`.
- The on-screen scenes `tests/Integration/HtmlViewerScenes.ps1` are a hand run (`Run-AllTests.ps1 -HtmlScenes`); the artifact check `tools/Test-HtmlHostArtifacts.ps1` belongs to the build and release runners ([build-release.md](build-release.md)).
