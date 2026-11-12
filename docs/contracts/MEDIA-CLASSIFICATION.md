# Pointer - `MEDIA-CLASSIFICATION`

| | |
| --- | --- |
| **Id** | `MEDIA-CLASSIFICATION` |
| **Version** | 0.12, draft (read 2026-10-20, specification 244; read at 0.10 on 2026-10-02 and 2026-10-07). Owner: FastMediaSorter Android. 0.11 (additive) says classification does not require browsing, that a MIME label in a popup alone is not adoption, and that sorting, random order, zoom, move and slideshow follow `MEDIA-VIEWER`, not this enum; 0.12 (additive) adds `.jxl` to `IMAGE`. Implemented: rules 1, 2, 3, 5, 6 (extension fallback) and section 4 (ticket 043); rule 4 on the mainline since 2026-10-22 (registry row closed 2026-10-30, ticket 252). 0.13 (2026-10-29, six extensions) not read yet |
| **Home** | `media-classification/README.md` in the shared contracts catalog |
| **Role here** | consumer - `MediaKindClassifier.KindOf` in `src/MediaKind.vb` (.NET 10 mainline only); the net48 fallback keeps plain extension lists |

## What this repository must do to stay conformant

- An unknown or empty extension classifies as `Other` - the contract's `UNKNOWN` - and never throws (held).
- The owner accepted the desktop-viewer proposal into 0.10. `MediaKindClassifier` maps books,
  streams and containers separately; `.fd-sec` is `Container`. An audiobook is `Audio` unless
  the caller supplies positive book evidence. Local aliases `.tif/.exif/.log/.html/.htm` remain, the
  source-text extensions of ticket 277 (`.sql .sh .bat .cmd .ps1 .cs .vb .cpp .h .c .py .js .css`, never
  `.ts`, which stays `Video`) are local `Document` aliases since 2026-10-29, and
  `.jxl` (JPEG XL, ticket 236, 2026-10-16) is an `Image` entry and, since 0.12, the contract's `IMAGE` row
  lists it too; the section 4 contract-table row in `tests/Lite.Tests/MediaKindClassifierTests.vb` carries it
  (seen 2026-10-20, not re-run); the contract's tables are a subset of the code's, category by category
  (compared 2026-10-07, before 0.12).
- Rule 3 normalization is shared by classification, navigation and archive eligibility. Rule 5
  junk exclusion runs before the browsing extension filter for folder rows, counts and archives;
  providers and storage operations keep their full file sets.
- Section 4 conformance vectors are in `tests/Lite.Tests/MediaKindClassifierTests.vb`. Classification
  does not expand the supported navigation sets or bypass video/audio preferences (ticket 009).
- **Rule 4 (sidecar association)** is `src/SidecarAssociations.vb` (.NET 10 mainline only, whole-file
  `#If Not NETFRAMEWORK`): subtitles, lyrics, a cover or poster and metadata in the primary's own directory, by
  the contract's patterns, without changing the primary's category; the file details, audio view and video
  player call it on their background work. Tests: `tests/Lite.Tests/SidecarAssociationsTests.vb`. The net48
  fallback associates none (maintenance policy).
- Plain NTFS supplies no MIME, so rule 6 uses extension fallback here.
- Classifying an extension is not a promise to render it: the x86 fallback keeps listing formats it cannot
  decode as unsupported.
