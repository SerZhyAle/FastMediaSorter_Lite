# Pointer - `USER-PLAYLIST`

| | |
| --- | --- |
| **Id** | `USER-PLAYLIST` |
| **Version** | 0.12, draft (read 2026-10-02 as "no role"; re-read 2026-10-07 with sections 6 to 9 and the four vectors, and the role corrected). Owner: StreamsPlayer |
| **Home** | `user-playlist/README.md` + `vectors/` in the shared contracts catalog |
| **Role here** | consumer, **partial**, Extended M3U8 only: `src/Streams/M3uPlaylistReader.vb` reads `.m3u` / `.m3u8` files (one file or a folder, recursively) as the input of the `STREAM-BANK` producer (specification 022, [STREAM-BANK.md](STREAM-BANK.md)). Nothing is written back to a playlist and the JSON carrier is not read - section 6 A makes both legal |

## What this repository must do to stay conformant

- **Held, and held by a test since 2026-10-07 (specification 183):** `UserPlaylistConformanceTests` runs the
  catalog's three Extended M3U8 vectors, vendored under `tests/Lite.Tests/TestData/user-playlist-vectors/`
  beside a `PROVENANCE.txt` whose SHA-256s it asserts first (the JSON vector is not vendored - this reader
  does not read that carrier). `valid-extended.m3u8` gives its 3 rows; `valid-extended-hls-entry.m3u8` gives
  all 3 (section 9: a URL ending `.m3u8` inside a playlist is an entry, and the text `#EXT-X-VERSION` inside a
  title, an attribute or a URL does not make a manifest); a title that is missing or blank falls back to the
  URL's host (rule 4); only `http`, `https` and `rtsp` entries are admitted; the input file is never modified.
- **Rule 3, the HLS manifest rejection:** a file with a line that BEGINS `#EXT-X-` is refused whole - 0 rows,
  0 entries counted, one refusal whose reason starts `HlsManifest:` and names the line
  (`M3uPlaylistReader.HlsManifestStatus`) - wherever in the file the tag stands, and for absolute variant URLs
  as for relative segments (`hls-manifest-rejection.m3u8` gives 0 rows because it is a manifest, no longer
  because its segments happen to be relative paths).
- **Section 8 item 1, the ceiling:** a file longer than 32 MiB (33 554 432 bytes) is refused whole before a byte
  is read, with a refusal that starts `TooLarge:` (`MaxPlaylistBytes`, `TooLargeStatus`); a file of exactly the
  ceiling is read. The refusal is per file - the other playlists of a folder are still read - and the buffer is
  sized from the length that was checked, so a file that grows during the read cannot pass the ceiling.
- **Section 8 item 2, the title:** the text after the first comma **not inside double quotes**, trimmed
  (`IndexOfTitleComma`): `group-title="Rock, Pop",Name` yields `Name`, and the quoted attribute still reads
  whole. A record with no such comma has no title and takes rule 4's host fallback.
- **A producer choice outside the contract** (not a contract rule, so not an exception): the playlist's own
  `type` / `tvg-type` / `group-title` is read by WORDS for the declared media kind, so `group-title="Latvia"`
  no longer reads as video through the letters `tv`; `TV`, `IPTV`, `HDTV` and `Webradio` still do (see
  [STREAM-BANK.md](STREAM-BANK.md)).
- Rule 4's non-destructive merge and exact-URL de-duplication do not apply as written: the reader builds a
  new catalog and folds by the `STREAM-BANK` channel identity, never into an existing user collection. Say so
  again if a feature ever imports into one.
- Section 8 item 3 (a network import refuses a media-stream response) does not apply: the reader opens local
  files only.
