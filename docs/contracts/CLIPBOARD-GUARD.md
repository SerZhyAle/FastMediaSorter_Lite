# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.10, draft (re-read 2026-10-07: the catalog is still at 0.10; the 0.10 amendment restates the privacy-marker writer set and pins the raw-API retry numbers). Owner: CyrFlip |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | consumer, writer side only - the Share Manager copies the SFTP password, login and the `.fmscfg` QR; the viewer copies names, paths and images, and (since the resource export, ticket 118) one payload that carries an SFTP password. No history, no in-place transformation, so items D to K and rules 4 and 5 bind nothing here |

## What this repository must do to stay conformant

- **A credential written to the clipboard carries the 0.10 privacy markers**: `CanIncludeInClipboardHistory`
  = 0 and `CanUploadToCloudClipboard` = 0 as DWORDs, `ExcludeClipboardContentFromMonitorProcessing` by
  presence. Share Manager: `SecureClipboard.ApplyPrivacyFlags` (`src/FastMediaSorterCompanion/Core/`) is the
  one door - the password, login + password, the QR image, the broadcast link and the exchange fingerprint
  all go through it, and the source-scan test `ShareManager_NeverWritesTheClipboardOutsideTheHelper` keeps
  every other writer out (its one exemption is the log archive, a file drop). Viewer: `SecretClipboard`
  (`src/Resources/`) does the same for the exported resource document, held by `SecretClipboardTests`. The
  registry exception on rule 6 stays open only for the manual Win+V check and the commit of this work.
- Clipboard access happens on the UI thread only (held - 0.10 item A allows the UI-thread wrapper) and
  survives another process holding the clipboard: a bounded retry, then a message - never a swallowed
  failure and never an unhandled exception. The WinForms `Clipboard.SetDataObject(data, True, 3, 120)`
  retry is this product's form of the bounded retry (`SecureClipboard`, `SecretClipboard`,
  `Utils.CopyTextToClipboard`); the plain `Clipboard.SetText` callers of the viewer (the batch report, the
  file-details box) take the framework's default and catch and log the failure. Item B pins 12 x 15 ms for a
  raw `OpenClipboard` loop, which the product does not use - a **dated registry exception**, with a
  clarification for wrappers proposed to the contract's owner.
- The viewer's other writers copy only non-sensitive text (a file name, a path, a log-package file drop), so
  rule 6 imposes nothing on them beyond the retry rule.
