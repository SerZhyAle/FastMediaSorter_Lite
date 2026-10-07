# Pointer - `OCR-INVOCATION`

| | |
| --- | --- |
| **Id** | `OCR-INVOCATION` |
| **Version** | 1.1, active (re-read 2026-10-07, unchanged since 1.1 of 2026-09-25: the amendment changes no flag, default or exit code - a caller built against 1.0 keeps working, and the launcher already omits `-ocr-lang` on `auto` and treats the 1.1 no-plates stop as success). Owner: doc-html-translate |
| **Home** | `ocr-overlay/integration-image-translate.md` in the shared contracts catalog |
| **Role here** | consumer - the "translate in the browser" button, `src/DocHtmlTranslateLauncher.vb` |

## What this repository must do to stay conformant

- Pass the image path **positionally and last**, behind `-ocr-lang`, `-force` and `-folder`; the flag
  spellings are frozen by the producing product and must not be changed here.
- Keep the detection order, including the `%LOCALAPPDATA%\Microsoft\WinGet\Links` absolute path: a process
  started before the install cannot see the new PATH alias, which is the contract's own warning.
- Treat a non-zero exit as a failure to report, never to swallow.
- Never point `-folder` at the user's own picture folder.
- Send `-ocr-lang` only for a language the user chose. With the source language on `auto` the flag is
  omitted, so the producer runs its own script detection; sending this product's assumed ladder as if it
  were a choice switched that off (ticket 033 Ф-7, 2026-09-25). The command line is built by
  `DocHtmlTranslate.BuildArguments` and held by `DocHtmlTranslateArgumentsTests`.
- Refuse the launch for a secure container: the only path to hand over is the sealed file.

Two documented departures, neither a deviation: the output always goes to a cache under `%LOCALAPPDATA%`,
and the direct-download ZIP fallback of section 4 is not implemented - winget plus the Store page is the
whole install offer.
