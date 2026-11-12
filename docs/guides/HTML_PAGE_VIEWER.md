# HTML page viewer

The 64-bit viewer can show `.html` and `.htm` files as pages, in the same window as file sorting.
It is off by default: Settings (F2) > Documents > HTML pages > HTML page viewing. While it is off,
an HTML file opens as source text through the ordinary text preview, and no browser component is
started. The 32-bit viewer has no page viewer and keeps the text preview. Design and acceptance:
specification 275 (local). The EPUB reader of specification 274 runs on the same host.

## What the page can and cannot do

The page is shown by the Microsoft Edge WebView2 Runtime that Windows already carries; the program
never bundles, downloads or installs it. Settings > Documents > HTML page data > Page viewer
component checks the installed version only when the button is pressed, and the component download
page opens Microsoft's page only on a click. A missing, outdated or blocked Runtime, a missing or
wrong-architecture loader, or an older Windows each give their own message and leave the text view.

The page is parsed by the program (AngleSharp) and served from a private origin: authored script,
forms, downloads, permissions, pop-ups, `file:` addresses and folder mapping are refused, and the
file on disk is never modified. Resources resolve only inside the page's folder; a link that leaves
it is blocked and named. Remote images, styles and fonts of a local page are blocked and counted in
the blocked-resources menu of the document panel; allowing them is a per-site choice for the open
page only, never saved. A link to
another site asks before the browser opens it, or opens nothing when Links to other sites is Block.
A file from a secure `.fd-sec` container is refused before the host starts and shown as text.

## Reading

Original keeps the author's styling; Reading mode applies the theme, font, size, line spacing,
column width and margins of the appearance group (sizes in page pixels, independent of screen
scale). Zoom runs from 25 to 500 %, with Ctrl and the wheel over the page or the zoom buttons of the
document panel. Ctrl+F opens the find box on the panel and types into it in place, Enter or F3 goes
to the next match and Shift+Enter or Shift+F3 to the previous one; case and whole-word matching
apply to a new search. While the page has focus it owns the keys - sorting keys and Delete do not
act on the file; Escape clears the matches and then returns to the file canvas. From the canvas,
PageUp and PageDown scroll the page and the wheel over it scrolls rather than changing the file.

## The document panel

The reading commands stand on the document panel, a floating panel at the top of the window under
the file rows, shown only while a page is open. It hides and reveals like the other floating panels
and keeps its place in the F10 keyboard ring; its corner pin holds it on screen, and the pin is off
by default. The page fills the whole window and a pinned panel covers its top lines, as a pinned
row covers a picture. In the panel's order: Back, Forward and Reload; the find box with Previous
match, Next match and the match count; zoom out, the zoom value (a click resets it), zoom in and
Fit width; Reading mode and Reset appearance; Source text (a file, not an online page), Open in
browser and Print; the blocked-resources menu (when something was blocked); Return to files; and
the line that says where the keys go. When the window is narrow the last of these collect behind
the » button, in that order; the find box never does.

## Pages from the internet

A second, separate switch (Pages from the internet) adds Open HTML page by address.. to the open-URL
menu. Such a page is fetched read-only by the program and shown in a profile kept apart from local
pages: no script, no site sign-in, no cookies kept, redirects (at most five) and plain HTTP each
confirmed, certificate errors refused. The confirmation says what the server sees: an ordinary
request and the IP address.

## Stored data and privacy

Reading positions and per-page appearance live in
`%LOCALAPPDATA%\SZA\FastMediaSorter\documents\html-state-v1.json`, bounded by Pages in reading
history (500 by default, at most 2000; 0 records nothing). Remember position and Remember appearance
switch the recording off; Clear HTML page history deletes the records, and a records file the
program does not trust is kept untouched until then. The Runtime's own profiles are folders the
program owns under `%LOCALAPPDATA%\SZA\FastMediaSorter\documents\webview\` (local and online apart,
custom crash reporting on, removed when the last page closes, or at the next start when the program
was closed with a page open, whether or not the viewer is still switched on); Clear temporary viewer data deletes
what is not in use. The Runtime is a Microsoft component with its own diagnostics: blocking a page's
requests does not make the Runtime itself silent.

## Maintainer notes

- Code: `src/Documents/Html*.vb` (policy, sanitizer, sources and adapters, runtime service, host,
  network fetcher, state), `src/Main_Form.HtmlView.vb` (window, the commands it hands to the document panel, input gate),
  `src/Table_Form.Documents.vb` (settings rows), `src/ModernViewerPreferences.Html.vb`, strings in
  `src/Localization/Localization.Html*.vb`. All of it is whole-file `#If Not NETFRAMEWORK`.
- Every host goes through `HtmlRuntimeService.PrepareLoader()`: the loader folder can be set once
  per process, so the EPUB host and this one share it and the per-profile environments.
- Packaging: one x64 `WebView2Loader.dll` in `webview2\win-x64\`, held by
  `tools/Test-HtmlHostArtifacts.ps1` wherever a package is staged (build rules: `build-release.md`).
- Tests: `Invoke-Tests.ps1 -Suite lite -Filter Html`; the on-screen scenes are opt-in and never part
  of a default run.
