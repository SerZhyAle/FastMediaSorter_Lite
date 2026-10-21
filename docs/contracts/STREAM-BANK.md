# Pointer - `STREAM-BANK`

| | |
| --- | --- |
| **Id** | `STREAM-BANK` |
| **Version** | 2.4, active (2.2 of 2026-10-02 added rule 13 - a blank or absent `is_live` means "not stated" - which a producer that always leaves the column blank already holds; 2.3 of 2026-10-07 added rule 14, the closed `language` and `country` value sets; 2.4 of 2026-10-17 is the consolidation). The body below was last read against 2.2 on 2026-10-07; the 2.3 and 2.4 clauses are not yet audited against `src/Streams/`. Owner: FastMediaSorter Android |
| **Home** | `stream-catalog/README.md` in the shared contracts catalog |
| **Role here** | **producer** - implemented under specification 022 (`src/Streams/`), on `main` since 2026-10-03 and in **no release yet** (the latest release predates it); the contract's own header still lists this product as "planned" |

Specification 022 implements Fast Media Sorter & Sharing for Windows (FMS_Win) as a conforming producer of `stream-catalog.zip`
from `.m3u` and `.m3u8` playlists on the PC. The registry row for this product used to read "planned
consumer", then "planned producer, not started"; it is a producer, and the row says so since 2026-10-07.
The producer's own version tag (`StreamCatalogSchema.ContractTag`, printed in its report and held by a test)
reads `STREAM-BANK 2.2`.

## Producer Rules Implemented (STREAM-BANK 2.1, unchanged by 2.2)

- **Entry zero is `streams.csv`**, followed optionally by `favicon-atlas.png`. No stray entries, no directories, and no second `.csv` file.
- **RFC-4180 CSV**, UTF-8 without BOM, with all 19 canonical columns in exact producer order.
- **De-duplication via Channel Identity fold** (`03` §2.5), collapsing casing, default ports, trailing slashes, and `http`/`https` protocols into canonical rows with fold reporting.
- **Five `name` guarantees** maintained on every row: HTML entities decoded (including double-encoded), leading encoder slots stripped, empty/junk names rebuilt from `host[:port]`, and `U+FFFD` refused and reported.
- **Refusal rules**: non-launchable schemes (only `http`, `https`, `rtsp` permitted), credentials (`user:pass@`), broadcast tokens (`/b/...`), and nested playlist references (`.m3u`, `.pls`) are refused and named in the run report.
- **No favicon index without atlas**: in the first version, no atlas and no `favicon_index` values are emitted (preventing S0925 favicon wiping hazards).
- **Validation suite (V1-V12)**: written bytes are validated prior to atomic destination replacement.
- **Churn guard**: protects against accidental loss of >20% URLs when updating an existing catalog of >=50 rows.
- **`is_live` is always blank** (2.2 rule 13: "not stated"), never `false`: this producer does not know whether a
  playlist entry is live.

## What is not claimed

- No real consumer (StreamsPlayer, the Android app) has imported a ZIP built here; the evidence is this
  product's own validator and unit suite, not a cross-product run.
- The producer derives `media_kind` from the playlist's own `type`/`group-title`/`radio` attributes before the
  URL classifier (`M3uPlaylistReader.ClassifyMediaKind`); `03` section 4 specifies only the URL classifier, so
  this is a producer choice outside the contract. It reads the attribute by WORDS since 2026-10-07
  (specification 183): a word that is `tv` / `tvs` or ends in `tv` (`IPTV`, `HDTV`), or is `radio` / `radios` or
  ends in `radio` (`Webradio`), declares the kind; a group title that merely contains the letters
  (`Latvia`, `Radiohead`) declares nothing, is recorded as an unrecognized declared kind and leaves the URL
  classifier to decide. Held by `UserPlaylistConformanceTests`; the earlier substring reading is gone.
