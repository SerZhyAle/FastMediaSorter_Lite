# Pointer - contracts this product declares no role in

Declared so the next alignment run does not re-investigate them. Each has a registry row saying the same
thing, with the evidence, re-dated 2026-10-07 (first declared 2026-09-24).

| Id | Version read | Home in the catalog | Why no role |
| --- | --- | --- | --- |
| `LAN-DISCOVERY` | 0.10, draft (2026-10-07) | `lan-discovery/README.md` | Neither the viewer nor the Share Manager announces, browses or parses mDNS; the announce is the vendored Go worker's. 0.10 rewrote the draft wire to what ships (`_sftp-fms._tcp`, TXT `fp` mandatory), which resolves the wire-deviation proposal this row used to cite. One seam to watch: `EndpointResolver.PlanCandidates` (`src/Resources/`) takes an optional `discovered` endpoint that no caller sets today; the day a browse feeds it, this row becomes a consumer |
| `UPDATE-MANIFEST` | 0.11, draft (2026-10-07) | `app-update-feed/README.md` | No in-app update check exists; updates arrive through winget and the Microsoft Store, which own the version comparison. The only related code opens a fixed `releases/latest` URL in the browser (`Main_Form.ShareLauncher.vb`). 0.11 adds rule 6 (part-by-part integer version comparison) for a client; with no client there is nothing to compare. The row becomes a consumer the day one is proposed; which number a manifest would publish for this product's stamp (`YY.M.D.HHmm`, remapped for the Store as `YY.(M*100+D).HHmm.0`) is the question 0.11 leaves open |

`USER-PLAYLIST` is **no longer on this page**: it was declared no-role on 2026-09-24 because "no playlist is
read or written", and that stopped being true when specification 022 shipped the playlist reader of the
stream-catalog producer (`src/Streams/M3uPlaylistReader.vb`). It has its own pointer,
[USER-PLAYLIST.md](USER-PLAYLIST.md), and a consumer row.

`SHARE-SESSION` is also a no-role contract here, with its own pointer, because this repository authored it.
