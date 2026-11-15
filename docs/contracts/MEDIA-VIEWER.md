# Pointer - `MEDIA-VIEWER`

| | |
| --- | --- |
| **Id** | `MEDIA-VIEWER` |
| **Version** | 1.0, active (since 2026-10-17); read again on 2026-11-15 (L0323 catch-up) at 1.0, the current catalog version; partial. Open, dated 2026-11-15 in the registry (ticket L0327): rule 2, the manual random action can pick the current file; rule 5, only the stored choice `shuffleCycle` is a no-repeat order and plain random repeats; rules 1 (sort, filter), 2 (jump input), 3 (`Ctrl`+wheel) and 4 (collision naming, same-folder refusal, next item after a move) not yet read clause by clause. Owner: shared (FastMediaSorter Android, Windows and Web) |
| **Home** | `media-viewer/README.md` in the shared contracts catalog |
| **Role here** | implementer - the Windows viewer (`src/Main_Form*.vb`, `src/FileManager.vb`, the zoom and slideshow partials) for the functions it offers; a function the platform does not offer is declared, never silently different |

## What this repository must do to stay conformant

- Follow the applicable numbered rules of the contract for sorting and filtering, random selection and jump,
  zoom and rotation, the slideshow and the move/copy/delete actions; the file-type classification is
  `MEDIA-CLASSIFICATION`.
- A justified difference is a dated registry exception, not a local reading of the rule.

## Where it stands

The registry row of this product and its dated exceptions say which rules are proven and which are open;
this pointer never repeats them. Specification 244 holds the clause-by-clause audit.
