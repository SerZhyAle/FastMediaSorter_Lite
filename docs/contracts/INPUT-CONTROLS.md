# Pointer - `INPUT-CHORD` and `INPUT-PARITY`

| | |
| --- | --- |
| **Ids** | `INPUT-CHORD` (how a combination is stored, shown, refused) and `INPUT-PARITY` (the shared action vocabulary with a binding per device) |
| **Version** | 0.3 / 0.3, draft (read 2026-10-02). Owner: CyrFlip - the chord notation and the keyboard and mouse columns; the device columns are `shared` with a steward per column |
| **Home** | `input-controls/README.md` in the shared contracts catalog |
| **Role here** | declared, not yet implementing - this product has no chord storage or global hook today; the catalog expects it (with StreamsPlayer) to write device columns of `INPUT-PARITY` eventually |

## Why this repository points at it

The catalog's `input-controls` function binds "every product with a user interface", and the parity
table names this product as an expected contributor for the device columns nobody has written yet
(touch was taken by FastMediaSorter Android on 2026-09-30; pen and media keys are still open). No code
here stores or displays chords, so `INPUT-CHORD` binds nothing yet and `INPUT-PARITY`'s vocabulary is not
consumed by any shipped surface.

When the product first persists a user-chosen key combination, this pointer becomes a consumer row: read
the chord token rules (0.3, rules 1-4) and the refusal rules before writing any format, and take the
vocabulary instead of inventing action names. Until then the registry row says "declared" so the next
alignment run does not re-investigate.
