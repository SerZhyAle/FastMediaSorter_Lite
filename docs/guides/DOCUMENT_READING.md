# PDF reading and document settings

The 64-bit viewer keeps document reading in the same window as file sorting. Open Settings with F2,
choose Documents and enable Full PDF reader in the PDF group. The PDF rendering component is an
optional installer component. If it is missing, use the application installer to install or repair it.
Turning the reader off returns to the page preview and preserves bookmarks.

## Navigate and read

Previous file and Next file change the file being sorted. Previous page, Next page and the page-number
field navigate within the PDF. Choose single page, continuous scrolling or a two-page spread. Spread
settings control the first page and the reading direction. Fit page, fit width, actual size and manual
zoom belong to the document. Original, night and sepia change page colors independently of the window
theme. The contents panel follows the document's outline; Back returns to the previous linked page.
The thumbnail panel renders pages as they become visible.

## The document panel

The reading commands stand on the document panel, a floating panel at the top of the window under
the file rows, shown only while a PDF, an EPUB, an HTML page or a text is open. It hides and reveals
like the other floating panels and keeps its place in the F10 keyboard ring; its corner pin holds it
on screen, and the pin is off by default. The document fills the whole window and a pinned panel
covers its top lines, as a pinned row covers a picture; the side pane (contents, search, text,
bookmarks, thumbnails) opens below a pinned panel. For a PDF the panel carries Back, Previous page,
the page box, Next page; the pane buttons, Add bookmark and Read aloud; Rotate, view mode, page
colour and Reset appearance; Open with and doc-html-translate; and the line that says where the keys
go. Press the page box to type a page number in place: Enter goes there, a number outside the
document is pulled to its first or last page, and Escape leaves the old page. An EPUB has the same
panel with a chapter box. When the window is narrow the last commands collect behind the » button,
in reading order; the page box, the chapter box and the page find never do.

## Search, text and bookmarks

Search the current page or the whole PDF, with optional case matching. Results name their page and
show surrounding text. Select a result to navigate. Results are bounded to 500 per search. Cancel stops
the search; navigating to another file rejects results from the old document. Pages without extracted
text are counted separately. Opening a scanned document starts no automatic OCR.

Select native page text with the mouse, or open the Text panel for a separate text reading view. Copy
respects PDF permissions. Text controls own their keystrokes: B, N, digits and Delete do not sort files
while a document control has focus. Escape closes an open side pane first and then returns the keys
to the canvas. The visible file buttons remain available.

Add bookmark asks for a name. Select a bookmark to jump to it, press F2 to rename it or Delete to
remove it. Appearance reset preserves bookmarks and positions. Clear reading history is a separate
Documents command. Position history retains up to 500 document versions; bookmarks have a separate
2000-entry limit. Changing a source file prevents its old reading state from attaching to the new version.

## Read aloud and privacy

Enable Read aloud in Documents, choose an installed Windows voice and set the reading rate. The
reader uses page text or the selected text and stops speaking when the page or document changes.
No voice service or voice download is started. PDF permissions can prevent copying or reading aloud.

Passwords remain in the open session and are absent from settings and logs. Ordinary PDF reading uses
an owned temporary local copy so rendering does not lock the original during a move or deletion. Closing
the session releases that copy after its last native operation finishes. Reading does not upload edits.
Reading positions, bookmarks and document appearance overrides are personal data and are omitted from
the ordinary settings export. PDF rendering and search are local; opening a clicked web link is an
explicit action in the system browser.

## Process a document in doc-html-translate

With Process documents in doc-html-translate on (Documents, doc-html-translate group), the
doc-html-translate button of the PDF and EPUB readers checks the program and asks what to do: open the
page in the browser and translate it there, convert only, translate with the local Ollama model, or
translate with Google. A missing program is installed through winget from the same question or chosen
by hand. Google translation runs only with a cost limit set in the group, and every such run is
approved first; without a limit the button opens doc-html-translate's own window instead. Each run
works in its own folder of the viewer's cache, never beside the document, and the viewer opens the
page only after checking that the run finished and that every file stays inside that folder. The page
opens in the browser or over the document in the HTML page viewer; the result menu on the document panel
opens the original, opens the page in the browser, shows the result folder, saves a copy of the whole
result to a folder you pick, or processes the document again. An unchanged document reuses its last
finished result. Results live under a size budget and a retention period, Clear finished pages empties them,
and a result shown or saved is kept until Clear. A partial or skipped translation, an unreadable file,
a time limit or a stopped run is named in the status line. Picture translation is a separate command
and does not change.

EPUB files retain their cover, title and author preview. Their existing external-open action uses
doc-html-translate. Sorting filters and Windows file associations are separate from reader preferences.

Product naming follows [PRODUCT_NAMING.md](PRODUCT_NAMING.md). The reader uses the same optional PDF
component and notices as the page preview; see [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
