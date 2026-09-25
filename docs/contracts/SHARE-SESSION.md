# Pointer - `SHARE-SESSION`

| | |
| --- | --- |
| **Id** | `SHARE-SESSION` |
| **Version** | 1.0, active. Owner: FMS Companion (the Go worker holds the server guarantees) |
| **Home** | `remote-folder-access/README.md` + `share-session-reconnect.md` in the shared contracts catalog |
| **Role here** | none - this repository authored the document and moved it into the catalog on 2026-09-22 |

Both halves belong elsewhere: the server guarantees to the worker this repository vendors as a binary,
the client obligations to the Android app. What ships from here is the launcher and the Share Manager UI,
neither of which opens an SFTP session.

## What this repository must do to stay conformant

Nothing directly. What it must **not** do is break the contract's third guarantee - the host key and the
credentials survive every restart, migration, update and host-mode change. That is breakable from here:
the Server-edition installer and `ServerFeatures.HostMode()` decide which host owns the state directory,
and a regenerated host key unpairs every phone that ever scanned a code.
