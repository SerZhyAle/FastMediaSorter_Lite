# Pointer - contracts this product declares no role in

Declared so the next alignment run does not re-investigate them. Each has a registry row saying the same
thing, with the evidence, dated 2026-09-24.

| Id | Version read | Home in the catalog | Why no role |
| --- | --- | --- | --- |
| `LAN-DISCOVERY` | 0.9, draft | `lan-discovery/README.md` | Neither the viewer nor the Share Manager announces, browses or parses mDNS; the announce is the vendored Go worker's. The shipped wire differs from the draft - filed as a proposal beside the contract, because the deviation is not this repository's |
| `UPDATE-MANIFEST` | 0.9, draft | `app-update-feed/README.md` | No in-app update check exists; updates arrive through winget and the Microsoft Store. The row becomes a consumer the day one is proposed |
| `USER-PLAYLIST` | 0.9, draft | `user-playlist/README.md` | No playlist is read or written. Specification 022 plans an M3U reader to produce `STREAM-BANK`; its one disagreement with the draft (an HLS URL inside a playlist) is filed as a proposal |

`SHARE-SESSION` is also a no-role contract here, with its own pointer, because this repository authored it.
