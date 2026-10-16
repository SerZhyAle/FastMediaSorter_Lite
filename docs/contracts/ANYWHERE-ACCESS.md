# Pointer - `ANYWHERE-ACCESS`

| | |
| --- | --- |
| **Id** | `ANYWHERE-ACCESS` |
| **Version** | **1.0, active; read 2026-10-16** (specification 235: sections 1-4, 5.5, 8-10, the amendments A to L and `vectors/descriptor-v2.json`; sections 5.1-5.4, 6 and 7 are the superseded 0.10 record and bind nothing). Since 0.11 sections 5-7 are superseded by `DEVICE-EXCHANGE`; section 4 (the descriptor `FMSSFTP1`/`FMSSFTP2` and its phone-code reading, amendment J), section 5.5 (the third-party port) and section 8 (backward compatibility) bind. 0.12 corrected 5.5 (the port is not in `FMSSFTP2`); 0.13 narrowed Drive and put the share id inside TLS; 0.14 added the outward link (K) and the per-grant login (L). Owner: FastMediaSorter Android |
| **Home** | `anywhere-access/README.md` in the shared contracts catalog |
| **Role here** | exchange-server implementer (now through `DEVICE-EXCHANGE`, see its pointer) and, for what stays here, **producer and consumer of the share descriptor** `FMSSFTP1`/`FMSSFTP2` (`src/Exchange/FmsSftpCode.vb`, modern only), of the phone-code intake (specification 227) and of the third-party port (section 5.5). **Implements `partial 1.0`** (registry row, verified 2026-10-16): section 4 is built and its vectors are replayed; the outward link and the per-grant login (K, L), the Drive opt-in (H) and the rungs beyond rung 1 are not met, and each is a dated exception in the registry |

**Reader-facing description:** the portal page [Home network](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/home-network.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

What this pointer covers is the descriptor (`FMSSFTP2` and `vectors/descriptor-v2.json`, rung 1), the `.fmscfg`
additive kind `rendezvousTunnel` (registered by `FMSCFG` 2.4, see its pointer), the third-party TCP port
(section 5.5, requested by `publish.publicPort` and carried by the resource record and a `.fmscfg` access path,
never by `FMSSFTP2`) and the backward-compatibility rules of section 8.

## What this repository holds

1. **The descriptor codec exists.** `src/Exchange/FmsSftpCode.vb` writes and reads `FMSSFTP1` and `FMSSFTP2`
   (whole-file `#If Not NETFRAMEWORK`: the x86 fallback gets none). `FmsSftpCodeTests` replays the vendored
   `descriptor-v2.json` - valid 5, `v1IgnoresV2Keys` 1, refused 9 - from `tests/Lite.Tests/TestData/anywhere-descriptor-vectors/`
   (byte-identical to the 1.0 catalog file; its provenance note still names 0.13). **`fmscfgTunnelPath` is
   replayed by no test**; the tunnel path is pinned structurally (`ShareConfigTunnelPathTests`,
   `ExchangeExportTests`). The reader is stricter than the text of 4.3 on four points and has no "newer
   prefix, update" message (239 CR-17).
2. **The `.fmscfg` export** carries the third-party port as a `portforward` path and the tunnel path last,
   with a valid host and port (`FMSCFG` rule 7); the tunnel path's share-id member is `shareId`
   (`ExchangeExport.vb`, pinned by `ExchangeExportTests`). It is no longer dormant: the Share Manager's
   producer feeds it while the client gate is open.
3. **The server and the producer** are specifications 198
   (umbrella, done), 199-201 and 224-234, under `DEVICE-EXCHANGE`: the third-party port is a lease tied to a
   published resource through `publish.publicPort` (section 5.5 as corrected by 0.12), never to a register
   request; `connect {shareId}` is accepted only inside TLS (amendment I).
4. **Section 8 rule 5 (dual mode)** is held by the byte-identical export tests
   (`ExchangeExportTests`, `ShareConfigTunnelPathTests`) and by the baseline of specification 205, which is
   **red today** on six registry names the viewer added.
5. **The outward link and the guest grant are not delivering** (amendments K and L): the link carries the
   install's shared login, its password is stored and never enforced, and the product offers a guest invitation
   and a link although the worker cannot confine a login to one folder (239 CR-13). A statement elsewhere that
   says otherwise is wrong; the registry is the record.
6. **Drive** (amendment H: `access` reaches Drive only by the user's opt-in) is not built, and specification
   203 does not yet name the opt-in in its work
   list or its acceptance (239 section 4).
7. **No proposal of this repository is open on this contract.** The one it filed on 2026-10-07 (the third-party
   port cannot ride in an `FMSSFTP2` code) was folded as amendment G of 0.12. Amendment L says the per-grant
   login is owed "by a proposal before code"; none is filed (239 CR-13).
