# Pointer - `STREAM-BANK`

| | |
| --- | --- |
| **Id** | `STREAM-BANK` |
| **Version** | 2.1, active (read 2026-09-24). Owner: FastMediaSorter Android |
| **Home** | `stream-catalog/README.md` in the shared contracts catalog |
| **Role here** | planned **producer** - specification 022, a proposal; no code exists yet |

The registry row for this product used to read "planned consumer". It is a producer: 022 turns the
`.m3u` and `.m3u8` playlists a user already keeps on the PC into one conforming `stream-catalog.zip`.

## What this repository must do when 022 is taken

- Match columns by **header name**, never by position, and tolerate a column you do not know.
- Merge by stable key and touch only catalog-origin rows. A row the user created or imported by hand is
  never deleted, overwritten or reordered by an import - absence from a producer artifact is not
  authority to destroy.
- Bounds-check every atlas index and degrade to the nothing-case - no thumbnail, no favicon - rather than
  to an error the user has to understand.
- Derive sheet geometry from the image; never assume a row count.
- Keep broadcast rows out of a published catalog (022 section 8.3, from 016's invariant 8).
