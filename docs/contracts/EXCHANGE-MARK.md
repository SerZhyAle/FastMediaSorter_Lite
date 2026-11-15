# Pointer - `EXCHANGE-MARK`

| | |
| --- | --- |
| **Id** | `EXCHANGE-MARK` |
| **Version** | read on 2026-11-15 (L0323 catch-up) at 0.4, draft, the current catalog version; partial. Open, dated 2026-11-15 in the registry (ticket L0328): rule 6 guidance - `ExchangeMark.Render` resamples the held PNG through `DrawImage` even at 1:1 and no test compares pixels; rule 14 - the title of `exchange-server/README.md` does not hold the full name. Earlier reading: 0.3, read 2026-10-20 |
| **Home** | `exchange-mark/README.md` in the shared contracts catalog |
| **Role here** | consumer - modern viewer, Share Manager, exchange server, help and site |

Artwork is imported byte for byte by `tools/Sync-ExchangeMark.ps1` into
`assets/exchange-mark/`; `-Check` compares the files and provenance against the catalog.
The shared `ExchangeMark` helper selects DPI sizes and the high-contrast glyph.
The help map declares head and inline images; the generator and `Test-ContextHelp.ps1`
hold their placement and accessible names. Specification 248 records the surface walk
and the remaining visual evidence. Fixed-layout dialogs without room retain their names
under rule 8. The product's launcher and window icons remain its own under rule 12.
