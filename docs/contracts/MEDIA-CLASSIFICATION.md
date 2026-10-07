# Pointer - `MEDIA-CLASSIFICATION`

| | |
| --- | --- |
| **Id** | `MEDIA-CLASSIFICATION` |
| **Version** | 0.10, draft (read 2026-10-02, re-read 2026-10-07: the catalog is still at 0.10). Owner: FastMediaSorter Android. Implemented: rules 1, 2, 3, 5, 6 (extension fallback) and section 4 (ticket 043); **not** rule 4 |
| **Home** | `media-classification/README.md` in the shared contracts catalog |
| **Role here** | consumer - `MediaKindClassifier.KindOf` in `src/MediaKind.vb` (.NET 10 mainline only); the net48 fallback keeps plain extension lists |

## What this repository must do to stay conformant

- An unknown or empty extension classifies as `Other` - the contract's `UNKNOWN` - and never throws (held).
- The owner accepted the desktop-viewer proposal into 0.10. `MediaKindClassifier` maps books,
  streams and containers separately; `.fd-sec` is `Container`. An audiobook is `Audio` unless
  the caller supplies positive book evidence. Local aliases `.tif/.exif/.log/.html/.htm` remain; the
  contract's tables are a subset of the code's, category by category (compared 2026-10-07).
- Rule 3 normalization is shared by classification, navigation and archive eligibility. Rule 5
  junk exclusion runs before the browsing extension filter for folder rows, counts and archives;
  providers and storage operations keep their full file sets.
- Section 4 conformance vectors are in `tests/Lite.Tests/MediaKindClassifierTests.vb`. Classification
  does not expand the supported navigation sets or bypass video/audio preferences (ticket 009).
- **Rule 4 (sidecar association) is not implemented**: no general sidecar resolver exists, and none is
  claimed. It is a dated registry exception; the day a feature needs one (a subtitle or cover that
  travels with its file) it is written against the contract's patterns, not invented.
- Plain NTFS supplies no MIME, so rule 6 uses extension fallback here.
- Classifying an extension is not a promise to render it: the x86 fallback keeps listing formats it cannot
  decode as unsupported.
