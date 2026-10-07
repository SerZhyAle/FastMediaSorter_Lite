# Pointer - `INSTALL-TRUST`

| | |
| --- | --- |
| **Id** | `INSTALL-TRUST` |
| **Version** | 1.2, active (read 2026-10-07). Owner: shared |
| **Home** | `install-trust/README.md` + `TRUST_GUIDE.md` (the reference rendering) in the shared contracts catalog |
| **Role here** | producer - **adopted 2026-09-22** (ticket 025 section 7), **re-aligned 2026-09-25** (ticket 037), **re-read against 1.1 on 2026-10-02 and against 1.2 on 2026-10-07** |

What 1.2 added, and where this page stands:

- **Item K (rule 4): no "Unblock".** The per-file Properties checkbox is not a permitted instruction - the only
  ones are More info -> Run anyway and an exception for one install folder. Nothing in this repository tells a
  user to use it: not the thirteen locales of `tools/trust-copy.json`, not the three READMEs, the root pages,
  `lang/**`, `docs/*.html`, the winget manifests, the installer scripts, the Store copy or the release workflow
  (searched 2026-10-07 in English, Russian, Ukrainian and the other ten languages, and for `Zone.Identifier` and
  the mark of the web). `TrustPageContractTests.NoSurfaceTellsTheUserToUnblockAFile` holds it from now on.
- **Item L (rule 1): the four sections in order.** The page has five: what the warning is, why it appears, exactly
  what to click, **what administrator rights are used for and how to take them back**, what the program never does.
  The four of rule 1 stand in their order (the "never does" section is last); the fifth is rule 5's itemization,
  which the reference rendering folds into its first and third sections. Section 3 of the contract reads only that
  the four are carried, so no reordering is owed; `EnglishKeepsRuleOnesOrder` holds the order.

What 1.1 added, and where this page stands:

- **Rule 5b (SHOULD, persistent unelevated changes with undo)** and the itemized elevations of rule 5 are
  held by the existing copy; the Share Manager's autostart is the one persistent unelevated change not yet
  listed - an open adoption item.
- **Rule 7 (item H):** the download-surface and in-app links are held (`TrustPageContractTests`); the
  winget manifests now carry `InstallationNotes` pointing at the page (ships with the next manifest
  version bump).
- **Item C:** the portable-archive instruction "unpack to a permanent folder before the first run" is not
  in the page copy yet, and the behaviour-heuristics naming applies only if the product registers global
  hotkeys - both open adoption items.
- **Item A:** the translated dialog quotes for hi/bn/ur/zh/pt/fr/ar stand unconfirmed on native-locale
  Windows; until confirmed they should fall back to the English string with a bracketed gloss - open
  adoption item.

The installer, the Server edition's installer and the portable ZIP are unsigned, so every GitHub and
winget user may meet SmartScreen. (Releases before 12 June 2026 also offered a single-exe download whose
self-extracting bootstrap draws a known Defender false positive; it no longer ships, and the page keeps
its entry dated rather than deleted - rule 8's reasoning, since those releases are still downloadable.)
Whether a winget install meets SmartScreen at all is not yet measured (ticket 037 Ф-0 step 4), so the page
says only that the builds are unsigned and a warning *may* appear. The Store build is signed by Microsoft
at certification and is outside this.

The page is **`trust.html` at the site root, plus `lang/<code>/trust.html` for the other twelve locales**.
It is a render target of [`tools/Build-SitePages.ps1`](../../tools/Build-SitePages.ps1) from
[`tools/trust-copy.json`](../../tools/trust-copy.json) - never hand-edited, so a change lands in all
thirteen locales in one edit and `-Check` proves the committed HTML still matches its source.

## Where the page is linked from (rule 7)

- the download block of the root page and of every generated language page, and all three READMEs (the
  packaged README carries the link into the ZIP and the installer);
- the GitHub release body, under "Install" (`.github/workflows/release.yml`);
- `server.html`, beside the Server edition's download instructions;
- both winget locale manifests, as a `Documentations` entry (ships with the next release's manifest bump);
- the Inno wizard's options page, first among its links - the page where Setup offers its one optional
  elevation;
- the Share Manager's opt-in dialog (`Share_Enable_Form`) and its Hosting group - the two in-app places
  that raise a UAC prompt - in the active UI language, through `SitePages.TrustPageUrl`;
- the viewer's Settings -> About, beside the privacy link.

No copy of the page ships inside the package: SmartScreen fires on the setup or on the extracted exe
before anything inside can be opened, so the packaged README's link is the carrier (proposed to the
catalog as an amendment, ticket 037 §3.1).

## The elevations the page itemizes (rule 5)

1. **Installing for all users** - the installer's own install-mode choice; the per-user default needs no
   administrator rights.
2. **The Share firewall rule** - asked at the end of a per-user Setup, by an elevated Setup, or by the
   Share Manager's opt-in; program-scoped to `companion\fms-share-worker.exe`. Undo: Manager settings ->
   Network -> "Switch off server features..", or uninstall - which now asks once for the removal when the
   install was per-user, and deletes by program path only, never by the shared rule name.
3. **The Windows service `FastMediaSorterCompanionSFTP`** - from the Server edition's installer or from the
   ordinary edition's Manager settings -> Hosting -> "Switch sharing to a Windows service.."; with it come
   the per-folder `LOCAL SERVICE` grants and the machine state folder `%ProgramData%\FastMediaSorterCompanion`.
   Undo: "Remove the server role", "Return to the User edition..", or uninstalling the entry that owns it.
   A silent uninstall cannot ask and leaves an app-owned service (and a per-user install's rule) in place;
   the page says so.

What survives every undo on purpose - the host key, password and folder list - is named on the page, with
where it lives, because without it a truthful page reads as a leftover.

The viewer itself raises no elevation: since ticket 037 Ф-3.3 it no longer runs the Visual C++
Redistributable itself, it names it and offers Microsoft's download.

## Rule 6

The "what the program never does" list agrees with [`docs/privacy.html`](../privacy.html) and with the Store
privacy block in [`docs/guides/STORE_PUBLISHING.md`](../guides/STORE_PUBLISHING.md) - Microsoft Store has no
data-safety form, so that block, the `runFullTrust` justification and the description are its answers.
All three name the same network contacts: check-host.net (only with internet access for sharing), the
router port mapping and the local-network announcement (only while sharing), and every download the user
asks for or approves.

## Held by tests

`TrustPageContractTests` (in `tests/Lite.Tests`) holds rule 1's section order and per-locale structure
parity, rule 4's forbidden instructions in English, the absence of any "Unblock" instruction in every
surface a user reads (item K), the presence of the page link on every surface listed above that is a file in
this repository, and every language's trust URL pointing at a page that exists.
The render check itself (`tools/Build-SitePages.ps1 -Check`) is a release-class check run by the release
preflight, where `tools/check-placement.jsonl` places it (ticket 042).

When the product becomes signed the page is updated, not deleted: users of older builds still meet the
warning, and the page is what search engines already point at.
