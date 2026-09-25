# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.9.1, draft (read 2026-09-24). Owner: CyrFlip. **Implemented: none** |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | consumer - the Share Manager copies the SFTP password, login and the `.fmscfg` QR; the viewer copies names, paths and images |

## What this repository must do to stay conformant

- **A credential written to the clipboard carries the privacy flags** that keep it out of clipboard history
  and the cloud clipboard. Not done today; the registry carries the dated exception and
  [ticket 041](../specifications/041_SPECIFICATION_CLIPBOARD_PRIVACY.md) does it.
- Clipboard access happens on the UI thread only (held) and survives another process holding the clipboard:
  a bounded retry, then a message - never a swallowed failure and never an unhandled exception.
