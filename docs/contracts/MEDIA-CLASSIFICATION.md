# Pointer - `MEDIA-CLASSIFICATION`

| | |
| --- | --- |
| **Id** | `MEDIA-CLASSIFICATION` |
| **Version** | 0.9, draft (read 2026-09-24). Owner: FastMediaSorter Android. **Implemented: none** - the local classifier predates the draft |
| **Home** | `media-classification/README.md` in the shared contracts catalog |
| **Role here** | consumer - `MediaKindClassifier.KindOf` in `src/MediaKind.vb` (.NET 10 mainline only); the net48 fallback keeps plain extension lists |

## What this repository must do to stay conformant

- An unknown or empty extension classifies as `Other` - the contract's `UNKNOWN` - and never throws (held).
- Realigning the category set and the extension table changes what a folder lists and what navigation
  visits, so it goes through [ticket 043](../specifications/043_SPECIFICATION_MEDIA_CLASSIFICATION_ALIGNMENT.md)
  after the owner answers the desktop-viewer proposal filed beside the contract; the registry carries the
  dated exception until then.
- Classifying an extension is not a promise to render it: the x86 fallback keeps listing formats it cannot
  decode as unsupported.
