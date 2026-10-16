# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | **0.11, read 2026-10-16** (specification 235). 0.10 restated the privacy-marker writer set and pinned the raw-API retry numbers; **0.11 (2026-10-14, section 8, items L to R, the reader amendment `CyrFlip S0073`)** added the exchange clipboard participant: explicit send of whatever is on the clipboard, no hangs, the order of kinds, the caps, the writing of a received value, no value in a log. The label correction of 2026-10-16 changed nothing else. Owner: CyrFlip |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | **writer** (unchanged: the Share Manager copies the SFTP password, login, the `.fmscfg` QR and the broadcast link; the viewer copies names, paths and one payload with an SFTP password) **and, since specification 230, participant and reader** (item L): the Share Manager reads the clipboard on an explicit press and sends it to the user's exchange server, and places a received value (`ClipboardWin32`, `ClipboardExchange`, `ClipboardCoordinator`); the viewer reads the clipboard once, on an explicit paste, to take in a phone code (`QrCodeDecoder.DecodeClipboard`). No history, no in-place transformation, so items D to K and rules 4 and 5 bind nothing here. **Implements `partial 0.11`** (two registry rows, verified 2026-10-16) |

## What this repository must do to stay conformant

- **A credential written to the clipboard carries the privacy markers**: `CanIncludeInClipboardHistory` = 0
  and `CanUploadToCloudClipboard` = 0 as DWORDs, `ExcludeClipboardContentFromMonitorProcessing` by presence.
  Share Manager: `SecureClipboard.ApplyPrivacyFlags` (`src/FastMediaSorterCompanion/Core/`) is the door for
  every secret - the password, login + password, the QR image, the broadcast link; the exchange fingerprint,
  not a secret, goes through `TrySetDataObject` without markers. Viewer: `SecretClipboard` (`src/Resources/`)
  does the same for the exported resource document, held by `SecretClipboardTests`. The source-scan test
  `ShareManager_NeverWritesTheClipboardOutsideTheHelper` keeps any other `Clipboard.Set*` call out of the
  Share Manager; **it cannot see `ClipboardWin32.PlaceNow`, the one deliberate marker-free writer** (item Q: a
  value the user asked to receive carries no marker), so that writer is reviewed by hand (239 CR-19).
- **Reading and placing the exchange value (items L to R)**: only an explicit act sends (a button, a tray item,
  the desktop shortcut) - no timer, no change notification, no start-up send - and nothing filters by a privacy
  marker or previews (item M: what is sent is exactly what is on the clipboard, a password included; the
  guide and the privacy page must say so, which they do not yet, 239 CR-19). The read runs on a worker thread
  with one `OpenClipboard` loop of 12 x 15 ms, closes the clipboard before any file system, conversion or
  network work, and abandons a delay-rendered format that does not answer (item N). One regular file is sent
  streamed after the close, a folder or several files are refused (O.1); a value is written with one open, one
  `EmptyClipboard` and one close, never restored (Q); no value and no file name reaches a log (R).
- **The order of kinds and the line endings follow `DEVICE-EXCHANGE` item CB** (file, image, text; CRLF to LF on
  the wire), **which the two contracts disagree on**: `CLIPBOARD-GUARD` item O says file, text, image and line
  endings as they are. This is a recorded difference for the owners, not a local choice (239 CR-18, Q239-1).
- **Item B's 12 x 15 ms loop is used by the exchange port** and is not used by the writers that go through the
  WinForms wrapper (`SecureClipboard`, `SecretClipboard`, `Utils.CopyTextToClipboard`: 3 x 120 ms; the plain
  `Clipboard.SetText` callers of the viewer take the framework's default and catch and log the failure) nor by
  the viewer's paste reader - a **dated registry exception** on the wrapper, with a clarification for wrappers
  proposed to the contract's owner.
- The viewer's other writers copy non-sensitive text (a file name, a path, a log-package file drop, the
  exchange hand-on string, the redacted diagnosis); one copies a URL with a single-use `?token=` of the LAN
  intake without markers (`Resource_Phone_Code_Form`, 239 CR-19).
