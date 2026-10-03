# Pointer - contracts this product declares no role in

Declared so the next alignment run does not re-investigate them. Each has a registry row saying the same
thing, with the evidence, dated 2026-09-24.

| Id | Version read | Home in the catalog | Why no role |
| --- | --- | --- | --- |
| `LAN-DISCOVERY` | 0.10, draft (2026-10-02) | `lan-discovery/README.md` | Neither the viewer nor the Share Manager announces, browses or parses mDNS; the announce is the vendored Go worker's. 0.10 rewrote the draft wire to what ships (`_sftp-fms._tcp`, TXT `fp` mandatory), which resolves the wire-deviation proposal this row used to cite |
| `UPDATE-MANIFEST` | 0.10, draft (2026-10-02) | `app-update-feed/README.md` | No in-app update check exists; updates arrive through winget and the Microsoft Store. The row becomes a consumer the day one is proposed |
| `USER-PLAYLIST` | 0.12, draft (2026-10-02) | `user-playlist/README.md` | No playlist is read or written. Specification 022 plans an M3U reader to produce `STREAM-BANK`; its one disagreement with the draft (an HLS URL inside a playlist) was folded into 0.12 in this product's favour, so nothing is open |

`SHARE-SESSION` is also a no-role contract here, with its own pointer, because this repository authored it.
