# Publishing an UPDATE to the Microsoft Store

How to ship a change to the **already-published** FastMediaSorter LITE Store listing. The first
publish is covered by [STORE_PUBLISHING.md](../../guides/STORE_PUBLISHING.md); this doc is for every submission
after that.

Every Store change - whether it is just new screenshots or a whole new build - goes out as a new
**submission** in Partner Center against the existing product. The product identity, the IARC age
rating, and the reserved name are already set and are **not** redone (see "What you never redo").

---

## Step 0 - Decide which kind of update this is

| Kind | Triggers | New `.msix`? | Time to live |
| --- | --- | --- | --- |
| **A. Listing-only** | Search terms, screenshots, description, features, privacy URL, support contact, price | **No** - reuse the package already in the Store | Often hours (listing-only changes sometimes skip full re-cert) |
| **B. Package** | New app version / bug fix / new feature in the exe | **Yes** - build a fresh MSIX | A few business days (full certification) |

You can do both at once (upload a new package **and** edit the listing in the same submission). When
in doubt: if nothing in the `.exe` changed, it is a Listing-only update.

> **Current pending update is Kind A (Listing-only):** fix Store search discoverability (add search
> terms) and replace the weak hero screenshot. No new binary required. See Step 1 and Step 2.

---

## Step 1 - Listing-only update (Kind A)

In Partner Center: **Apps and games ▸ FastMediaSorter LITE ▸ Start update / Create new submission**.
With no package change, leave the **Packages** page as-is (the live package carries over) and edit
only the **Store listing** page(s). The listing is **per language** - repeat for **English** and
**Russian**.

### 1a. Search terms (fixes "only the full name finds the app")

The Store only indexes the app **title** + **description** for search, so with no search terms set
the app surfaces only for the whole name. Search terms are **hidden** keywords (users never see
them) that feed the search index. Add them under **Store listing ▸ Search terms**.

Limits: **up to 7 terms, ~30 characters each**; a word already in your title/description is ignored
as a duplicate, so spend the slots on words people actually type that are **not** in the name.

**English search terms:**
```
image viewer
photo viewer
photo sorter
media sorter
picture organizer
slideshow
video viewer
```

**Russian search terms:**
```
просмотр фото
просмотрщик изображений
сортировка фото
органайзер фото
просмотр видео
слайдшоу
картинки
```

Also weave the high-value words into the **Description** body (it is indexed too): "image viewer",
"photo viewer", "slideshow", "sort photos / сортировка фото". Do **not** stuff keywords into the
visible **app name** - certification rejects keyword-stuffed names; that is what search terms are for.

### 1b. Screenshots

Requirement: at least 1 PNG, **>= 1366x768**; up to 10 allowed; the **first is the hero** shown in
search results and at the top of the listing. The old live hero is the gray startup help-text screen
(unappealing) - replace it.

Recommended order (assets already prepared in the repo unless noted):

1. **Hero** - real app on a photo with the "ambilight" perspective background:
   [assets/store/screenshot-EN-1366x768.png](../../../assets/store/screenshot-EN-1366x768.png)
2-5. Settings tabs: [settings_t1_dest.png](../../../settings_t1_dest.png),
   [settings_t2_viewing.png](../../../settings_t2_viewing.png), [settings_t3_video.png](../../../settings_t3_video.png),
   [settings_t4_files.png](../../../settings_t4_files.png)
6. (recommended, capture if missing) the OCR on-image **translation overlay** - it is the one
   feature no competing viewer shows.

To regenerate a real screenshot from the running app at the right size, use
[tools/store/capture-app.ps1](../../../tools/store/capture-app.ps1) (launches `bin\Release`, sizes the window,
captures via `PrintWindow`); [tools/store/make-screenshot.ps1](../../../tools/store/make-screenshot.ps1)
produces a generic >= 1366x768 frame.

### 1c. Other listing fields (optional, same submission)

Description / Product features / privacy URL / support contact - source copy lives in
[STORE_PUBLISHING.md](../../guides/STORE_PUBLISHING.md) ("Text templates"). Edit there too if you change them, so
the repo stays the source of truth.

### 1d. Submit

Add submission notes for the certification tester if anything is non-obvious (e.g. "listing-only:
added search terms + new screenshots, package unchanged"), then **Submit to the Store**.

---

## Step 2 - Package update (Kind B)

Only when the `.exe` changed. The build is identical to the first publish - the version is the only
moving part, and the script handles it.

### 2a. Build the new MSIX

```powershell
cd publishing\msix
.\build-msix.ps1 `
  -IdentityName         "<Package/Identity/Name from Partner Center>" `
  -Publisher            "<Package/Identity/Publisher, CN=... >" `
  -PublisherDisplayName "<PublisherDisplayName>"
```

The defaults baked into [msix/build-msix.ps1](../../../msix/build-msix.ps1) already match this product
(`SZA.FastMediaSorterLITE` / `CN=F98ACEDB-...` / `SZA`), so a bare `.\build-msix.ps1` also works -
but confirm against **Product ▸ Product identity** before every upload; a mismatch is rejected.

Output: `msix/dist/FastMediaSorter_LITE-<version>-x64.msix`, **unsigned** - upload as-is, Microsoft
re-signs on certification. Do **not** `-SelfSign` the Store package (`-SelfSign` is local sideload
testing only). See [msix/README.md](../../../msix/README.md) for build prerequisites and the OCR-payload flags
(`-IncludeBestOcr` / `-SkipOcrPayload`).

### 2b. Version must increase (Store rule)

The Store requires every new package to have a **strictly higher** version than the live one, with a
4-part `Major.Minor.Build.0` shape (revision **must** be 0). `build-msix.ps1` derives this from the
exe's `YY.M.D.HHmm` stamp automatically:

```
Major = YY,  Minor = M*100 + D,  Build = HHmm,  Revision = 0
e.g. 26.6.19.1230  ->  26.619.1230.0
```

Because every part is time-derived, a later build is always a higher version - **do not hand-edit
it**. Just make sure the new build's timestamp is later than the live package's (it will be).

### 2c. Upload and submit

Partner Center ▸ new submission ▸ **Packages** ▸ upload the `.msix`. The Store auto-detects
architecture (x64) and version from the package. Combine with any listing edits from Step 1 if you
want them in the same release, then **Submit**.

---

## What you never redo

These are one-time and carry across all updates - touching them is almost always a mistake:

- **Product identity** (`Identity/Name`, `Publisher`, `PublisherDisplayName`) - fixed for the life of
  the product; changing it would orphan the listing. The build script must keep matching it.
- **Reserved app name** - "FastMediaSorter LITE" stays.
- **IARC age rating** - bound to the questionnaire answers, not the version. Ordinary updates keep the
  General rating. You re-run the IARC questionnaire **only** if a change alters those answers (adding
  ads, in-app purchases, accounts, or user-to-user sharing). The current feature set (local viewer /
  sorter, optional user-configured OCR translation, no ads/accounts/UGC) does not. Global Rating ID
  `7d9b315a-f211-8505-80d0-3f4bee633770` is portable to other IARC storefronts. See
  [STORE_PUBLISHING.md](../../guides/STORE_PUBLISHING.md) "Age rating (IARC)".

---

## Step 3 - Certification and rollout

- Submission goes through automated + (for package updates) manual certification, typically a few
  business days; listing-only changes are usually faster.
- The optional translation feature makes outbound HTTP to a **user-configured** local/remote endpoint
  (Ollama / LibreTranslate); OCR is fully local. This is already stated in the description + privacy
  policy, which pre-empts reviewer questions about network use - keep it stated on any description
  edit.
- On approval the update rolls out automatically; existing installs update through the Store. You can
  set a **gradual rollout** percentage on the submission if you want a staged release.
- If certification fails, the report names the failing requirement - fix and resubmit the same way.

---

## Quick checklist

**Listing-only (Kind A):**
- [ ] EN search terms added (7), RU search terms added (7), none duplicating the title
- [ ] Description carries the key search words (image/photo viewer, slideshow, sort photos)
- [ ] New hero screenshot set (not the gray help screen), >= 1366x768, EN + RU
- [ ] Submission note explains "listing-only, package unchanged"
- [ ] Submitted

**Package (Kind B):**
- [ ] `.exe` change built in Release
- [ ] `build-msix.ps1` run; identity values confirmed against Partner Center
- [ ] Unsigned `.msix` (no `-SelfSign`); version is later than the live package
- [ ] Package uploaded, listing edits (if any) included
- [ ] Submitted

---

_Related: [STORE_PUBLISHING.md](../../guides/STORE_PUBLISHING.md) (first publish + listing copy),
[msix/README.md](../../../msix/README.md) (packaging detail), [docs/privacy.html](../../privacy.html) (live
privacy page)._
