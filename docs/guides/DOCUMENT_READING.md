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

## Search, text and bookmarks

Search the current page or the whole PDF, with optional case matching. Results name their page and
show surrounding text. Select a result to navigate. Results are bounded to 500 per search. Cancel stops
the search; navigating to another file rejects results from the old document. Pages without extracted
text are counted separately. Opening a scanned document starts no automatic OCR.

Select native page text with the mouse, or open the Text panel for a separate text reading view. Copy
respects PDF permissions. Text controls own their keystrokes: B, N, digits and Delete do not sort files
while a document control has focus. Escape dismisses the document panel and returns to the canvas.
The visible file buttons remain available.

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

EPUB files retain their cover, title and author preview. Their existing external-open action uses
doc-html-translate. Sorting filters and Windows file associations are separate from reader preferences.

Product naming follows [PRODUCT_NAMING.md](PRODUCT_NAMING.md). The reader uses the same optional PDF
component and notices as the page preview; see [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
