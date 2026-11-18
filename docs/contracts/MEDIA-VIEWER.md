# Pointer - `MEDIA-VIEWER`

| | |
| --- | --- |
| **Id** | `MEDIA-VIEWER` |
| **Version** | 1.0, active (since 2026-10-17); read again on 2026-11-15 (L0323 catch-up) at 1.0, the current catalog version; partial. Open, dated 2026-11-15 in the registry (ticket L0327): rule 2, the manual random action can pick the current file; rule 5, only the stored choice `shuffleCycle` is a no-repeat order and plain random repeats; rules 1, 3 and 4 were read clause by clause on 2026-11-15 (ticket L0334, read not run): the registry carries three more dated exceptions (name-sort culture; the wheel on a view that cannot zoom and the Ctrl+wheel step; the net48 gaps), and the still-open items are rule 2's jump input (not read), rule 1's folders-before-files in the file-manager mode (not traced) and rule 3's anchor for a child-surface wheel event (not run). Owner: shared (FastMediaSorter Android, Windows and Web) |
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
