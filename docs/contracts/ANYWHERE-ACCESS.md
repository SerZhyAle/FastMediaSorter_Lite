# Pointer - `ANYWHERE-ACCESS`

| | |
| --- | --- |
| **Id** | `ANYWHERE-ACCESS` |
| **Version** | 0.13, draft (read in full 2026-10-07). Since 0.11 sections 5, 6 and 7 are **superseded** by `DEVICE-EXCHANGE` sections 5, 7 and 8 and stay as the record; section 4 (the descriptor `FMSSFTP1`/`FMSSFTP2`, its rung 1 vectors), section 5.5 (the third-party port) and section 8 (backward compatibility) still bind. 0.12 corrected 5.5: the port is not in `FMSSFTP2`; 0.13 narrowed Drive (a share's credentials and addresses reach it only by the user's opt-in) and put the share id inside TLS. Owner: FastMediaSorter Android |
| **Home** | `anywhere-access/README.md` in the shared contracts catalog |
| **Role here** | exchange-server implementer (now through `DEVICE-EXCHANGE`, see its pointer) and, for what stays here, **producer and consumer of the share descriptor** `FMSSFTP1`/`FMSSFTP2` and of the third-party port (local specifications 170 and 171). The server specification 133 is re-scoped by local specification 160 |

**Reader-facing description:** the portal page [Home network](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/home-network.html) of the shared "Media exchange between your devices" portal (not published until the Android site publishes it). Nothing there binds this contract.

What this pointer still covers is the descriptor (`FMSSFTP2` and `vectors/descriptor-v2.json`, rung 1), the
`.fmscfg` additive kind `rendezvousTunnel` (registered by `FMSCFG` 2.4, see its pointer), the third-party TCP port
(section 5.5, requested by `publish.publicPort` and carried by the resource record and a `.fmscfg` access path,
never by `FMSSFTP2`) and the backward-compatibility rules of section 8.

## What this repository holds while the contract is `[PROPOSED]`

1. **No descriptor codec exists.** Nothing in `src/` or `exchange-server/` writes or reads an `FMSSFTP1` or
   `FMSSFTP2` code, so the rung 1 vectors are not run here: the writer is 170, the reader 171 (which must refuse a
   malformed code whole, read an `FMSSFTP1` code by the v1 rules only, and replay the catalog's vectors from a
   vendored copy with a provenance note).
2. **The dormant `.fmscfg` export** (`ExchangeExport`, ticket 148, kept by 170) was built to the 0.10 text with
   provisional names: the third-party port rides as a `portforward` path (which 5.5 as corrected confirms) and the
   tunnel path is written last with a valid host and port (`FMSCFG` rule 7). Its share-id member is named
   `shareId`, as `FMSCFG` 2.4 registers it (renamed from the provisional `sid` on 2026-10-07).
3. **The server and the producer are local specifications 161-168 and 170-180**, under the umbrella 160: the
   dev-tree server was built to the superseded wire and its core was replaced on 2026-10-07 by 161-164 (the third
   party port is now a lease tied to a published resource through `publish.publicPort`, section 5.5 as corrected
   by 0.12, never to a register request; the wire is `DEVICE-EXCHANGE`'s). No client has spoken to it, the
   descriptor codec of section 4 still does not exist, and nothing ships before the server wire's rung 2. The producer no longer asks the worker to register anything: the Share Manager's device agent publishes
   what the worker serves.
4. **Section 8 rule 5 (dual mode)** is held by the byte-identical export tests today and by the baseline of local
   specification 177 once the clients exist: with no account and no server a share is served and exported exactly
   as before.
5. **No proposal of this repository is open on this contract.** The one it filed on 2026-10-07 (the third-party
   port cannot ride in an `FMSSFTP2` code) was folded as amendment G of 0.12.
6. **The registry row** (role P/C, `Implements` `partial 0.10`) stays behind 0.13 by design: what exists was built
   to the superseded text, and the descriptor of section 4 is not built at all.
