# Pointer - `UI-SIZING`, `BUTTONS`, `BOOLEAN-OPTION`, `COLLAPSIBLE-GROUP`, `SETTINGS-NAVIGATION`

| | |
| --- | --- |
| **Id** | `UI-SIZING`, `BUTTONS`, `BOOLEAN-OPTION`, `COLLAPSIBLE-GROUP`, `SETTINGS-NAVIGATION` |
| **Version** | read at 1.2 / 2.2 / 2.2 / 1.3 / 2.3 on 2026-11-15 (L0323 catch-up; the current catalog versions). All five rows partial; exceptions open, dated 2026-11-15 in the registry: `UI-SIZING` s2 r3/r4 (Share Manager rail rows 26 px, Settings glyph tiers) and s4 (no 100/150/200 percent capture); `BUTTONS` s3 r4 (ellipsized chrome captions) and s5; `BOOLEAN-OPTION` s2/s3 (Welcome wizards) and s5; `COLLAPSIBLE-GROUP` s3 r6, s4 r2, s5 (not measured); `SETTINGS-NAVIGATION` 2.2 item E (rail child glyphs), s5 r4 (Settings cold open) and 2.1 item A, s6; ticket L0325 (cold open also L0279 T-1). Owner: shared |
| **Home** | `ui-sizing/README.md`, `buttons/README.md`, `boolean-option/README.md`, `collapsible-group/README.md`, `settings-navigation/README.md` in the shared contracts catalog |
| **Role here** | consumer, **migration open, no full conformance claimed** (local specification 243, opened 2026-10-17; closed items and open items are in the registry block of 2026-11-15). Surfaces: the modern viewer (chrome, dialogs, secondary windows, Settings window), the Share Manager and the official website with the offline help corpus. The net48 fallback is under the maintenance policy and is touched only where shared source reaches it |

## What this repository holds

1. **The ask:** one size model (`UI-SIZING`: 32 pointer, 48 touch, a declared and tested compact floor, scale and text enlargement independent, one shared token resolver); content-fit buttons with whole-button wrapping, neutral Cancel, No and Close, ordered dialog answers (`BUTTONS`); a leading checkbox row whose explanation and help never toggle (`BOOLEAN-OPTION`); full-width headers with a leading filled state triangle (`COLLAPSIBLE-GROUP`); a start-side rail with theme and language first and a complete About last (`SETTINGS-NAVIGATION`). The website follows `PAGE-STYLE` 3.1 for the same button and group rules.
2. **The plan and the inventory:** [UNIFIED_UI_CONTRACTS](../guides/UNIFIED_UI_CONTRACTS.md) holds the versions read, the size-profile record, the permanent variants, the per-surface inventory, the dated milestone plan with its proposed deadline and the open owner questions. The plan is not a conformance claim.
3. **The evidence:** a requirement advances only on a real capture or measurement (`SettingsLayoutScenes`, `SettingsTimingScenes`, `ModernChromeScenes`, `WindowsUiAudit`, the timing sampler of 20 warm and 20 first-visit samples). Static reading is a plan, never proof.
4. **Registry rows:** this product's rows in the shared registry change only after the owner approves the plan and only for this product; an adoption row advances on landed evidence.
5. **Related pointers:** [APP-UX](APP-UX.md) (`WINDOWS-UI`, `APP-STYLE`, `APP-SETTINGS`), [ICON](ICON.md) (`ICON-RENDER`), [PAGE](PAGE.md) (`PAGE-STYLE`), [CONTEXT-HELP](CONTEXT-HELP.md).
