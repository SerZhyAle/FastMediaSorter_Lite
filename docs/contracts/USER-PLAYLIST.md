# Pointer - `USER-PLAYLIST`

| | |
| --- | --- |
| **Id** | `USER-PLAYLIST` |
| **Version** | 0.12, draft (read 2026-10-02 as "no role"; re-read 2026-10-07 with sections 6 to 9 and the four vectors, and the role corrected). Owner: StreamsPlayer |
| **Home** | `user-playlist/README.md` + `vectors/` in the shared contracts catalog |
| **Role here** | consumer, **partial**, Extended M3U8 only: `src/Streams/M3uPlaylistReader.vb` reads `.m3u` / `.m3u8` files (one file or a folder, recursively) as the input of the `STREAM-BANK` producer (specification 022, [STREAM-BANK.md](STREAM-BANK.md)). Nothing is written back to a playlist and the JSON carrier is not read - section 6 A makes both legal |

## What this repository must do to stay conformant

- **Held, run against the catalog's vectors on 2026-10-07:** `valid-extended.m3u8` gives its 3 rows;
  `valid-extended-hls-entry.m3u8` gives all 3 (section 9: a URL ending `.m3u8` inside a playlist is an
  entry, and the text `#EXT-X-VERSION` inside a title does not make a manifest); a title that is missing or
  blank falls back to the URL's host (rule 4); only `http`, `https` and `rtsp` entries are admitted; the input
  file is never modified.
- **Not held, a dated registry exception:** rule 3's HLS manifest rejection (a file with a line that begins
  `#EXT-X-` is a manifest, rejected with `HlsManifest` and 0 entries) - the reader skips every `#` directive,
  so `hls-manifest-rejection.m3u8` yields 0 rows only because its segments are relative paths, and the same
  manifest with absolute variant URLs imports them as stations; section 8 item 1's 32 MiB ceiling (the file is
  read whole); and section 8 item 2 (the title is the text after the first comma **not inside double
  quotes** - `group-title="Rock, Pop",Name` yields the title `Pop",Name`).
- Rule 4's non-destructive merge and exact-URL de-duplication do not apply as written: the reader builds a
  new catalog and folds by the `STREAM-BANK` channel identity, never into an existing user collection. Say so
  again if a feature ever imports into one.
- Section 8 item 3 (a network import refuses a media-stream response) does not apply: the reader opens local
  files only.
