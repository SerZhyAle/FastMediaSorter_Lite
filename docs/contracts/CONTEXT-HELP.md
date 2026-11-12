# Pointer - `CONTEXT-HELP`

| | |
| --- | --- |
| **Id** | `CONTEXT-HELP` |
| **Version** | 0.1, draft (read 2026-10-16); 0.2 section 7 item B also held (read 2026-11-08). Owner: shared |
| **Home** | `in-app-help/README.md` in the shared contracts catalog |
| **Role here** | consumer, **partial** (specification 240, 2026-10-16): in-app F1, top-right `?` button on help-bearing windows, shipped `help-map.json`, resolver, offline HTML help pages and static/locale/link gates. Interactive walk, screenshot generator and release evidence pending |

## What this repository holds

1. **The ask (rules 1 to 3):** F1 is handled at window level and is not registered globally. Every help-bearing surface carries a `?` button (`contextHelp`) in the top-right corner with the `app.help` glyph and tooltip/accessible name "Help (F1)" in the active UI language without displacing existing controls. Both F1 and the button resolve through the same resolver and open the same target. A window that is one control carries `ask: "off"` in its map row and takes neither: the QR window (`qr-zoom`) is the code, edge to edge.
2. **Every surface is inventoried (rule 4):** `help-map.json` maps every Form class in the modern viewer and the Share Manager to its help form (`page`, `layer`, `both`, `inherit` or `none`), with explicit reasons for every `inherit` and `none` row.
3. **The answer (rules 5 to 11):** Secondary dialogs and settings pages open offline HTML pages extracted to a local cache. The main viewer window supports both the F1 help card layer and full HTML documentation. Pages follow the 8-part anatomy (Title, Purpose, Screenshot, Order, Elements, Related, Footer) and require no script or remote resources.
4. **Delivery & Privacy (rules 12 to 16):** Help pages are shipped locally and open with no network. Links to external sites require explicit clicks.
5. **The gate (rules 17 to 19):** `tools/Test-ContextHelp.ps1` and unit tests verify the map against the actual Form classes, validate page presence, link targets, and absence of remote requests.
