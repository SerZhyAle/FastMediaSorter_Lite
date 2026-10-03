# Pointer - `STREAM-BANK`

| | |
| --- | --- |
| **Id** | `STREAM-BANK` |
| **Version** | 2.1, active (read 2026-09-24). Owner: FastMediaSorter Android |
| **Home** | `stream-catalog/README.md` in the shared contracts catalog |
| **Role here** | **producer** - implemented under specification 022 (`src/Streams/`) |

Specification 022 implements Fast Media Sorter for Windows as a conforming producer of `stream-catalog.zip`
from `.m3u` and `.m3u8` playlists on the PC.

## Producer Rules Implemented (STREAM-BANK 2.1)

- **Entry zero is `streams.csv`**, followed optionally by `favicon-atlas.png`. No stray entries, no directories, and no second `.csv` file.
- **RFC-4180 CSV**, UTF-8 without BOM, with all 19 canonical columns in exact producer order.
- **De-duplication via Channel Identity fold** (`03` §2.5), collapsing casing, default ports, trailing slashes, and `http`/`https` protocols into canonical rows with fold reporting.
- **Five `name` guarantees** maintained on every row: HTML entities decoded (including double-encoded), leading encoder slots stripped, empty/junk names rebuilt from `host[:port]`, and `U+FFFD` refused and reported.
- **Refusal rules**: non-launchable schemes (only `http`, `https`, `rtsp` permitted), credentials (`user:pass@`), broadcast tokens (`/b/...`), and nested playlist references (`.m3u`, `.pls`) are refused and named in the run report.
- **No favicon index without atlas**: in the first version, no atlas and no `favicon_index` values are emitted (preventing S0925 favicon wiping hazards).
- **Validation suite (V1–V12)**: written bytes are validated prior to atomic destination replacement.
- **Churn guard**: protects against accidental loss of >20% URLs when updating an existing catalog of >=50 rows.
