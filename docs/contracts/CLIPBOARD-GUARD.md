# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.10, draft (read 2026-10-02: the 0.10 amendment restates the privacy-marker writer set and pins the raw-API retry numbers; the guard's semantics are unchanged from what this product implemented). Owner: CyrFlip |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | consumer - the Share Manager copies the SFTP password, login and the `.fmscfg` QR; the viewer copies names, paths and images |

## What this repository must do to stay conformant

- **A credential written to the clipboard carries the 0.10 privacy markers**: `CanIncludeInClipboardHistory`
  = 0 and `CanUploadToCloudClipboard` = 0 as DWORDs, `ExcludeClipboardContentFromMonitorProcessing` by
  presence. Implemented in the working tree 2026-09-30 by `ticket 041` (local specification):
  `SecureClipboard.ApplyPrivacyFlags` in the Share Manager sets exactly the 0.10 set, and the
  source-scan test `ShareManager_NeverWritesTheClipboardOutsideTheHelper` keeps every other writer out.
  The registry exception (rule 6) closes once this is committed and the manual Win+V check has passed.
- Clipboard access happens on the UI thread only (held - 0.10 item A allows the UI-thread wrapper) and
  survives another process holding the clipboard: a bounded retry, then a message - never a swallowed
  failure and never an unhandled exception. The WinForms `Clipboard.SetDataObject(data, True, 3, 120)`
  retry is this product's declared form of the bounded retry; the contract's 12 x 15 ms numbers pin the
  raw `OpenClipboard` loop, which the product does not use.
- The viewer writes only non-sensitive text (a file name, a path) and images, so rule 6 imposes nothing on
  it beyond the retry rule; the registry row says the same.
