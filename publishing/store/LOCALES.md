# Store listing locales - status and how to add the other eleven

The Store listing currently carries **two** locales: `en-us` and `ru`. Those are the
columns that exist in `listingData.csv`, and they now say the app's interface is
available in 13 languages.

## Why the CSV does not already have 13 columns

Partner Center's listing CSV is an **export-then-merge** artifact: the importer only
accepts columns for locales that **already exist** in the product's listing. Adding
`de`, `it`, `es`, `fr`, `pt-br`, `uk`, `ar`, `hi`, `bn`, `ur` and `zh-hans` to the file
before creating those listings makes the import fail, not succeed.

Hand-adding the columns here would therefore produce a file that looks finished and
cannot be imported - the worst of both.

## The order that works

1. **Partner Center -> Product -> Store listings -> Manage additional languages.**
   Create the eleven listings. Nothing to paste yet; they can stay empty.
2. **Export the listing CSV again.** The new locale columns appear.
3. **Merge our copy into that export**, not the other way round: keep the exported
   column order and the exported `Field`/`ID` rows exactly as they came.
4. **Import**, then re-open each locale and confirm nothing that was filled before is
   now blank.
5. Attach the screenshots from [screenshots/](screenshots/) - the file names carry the
   Partner Center locale (`screenshot-de-1920x1080.png`), so there is no guessing.

## Where the copy comes from

Short description and feature lines for all 13 languages already exist and are
maintained in two places, both of which are sources of truth for their own surface:

| Surface | Source |
| --- | --- |
| Screenshot captions (13 languages) | [screenshot-copy.json](screenshot-copy.json) |
| Site first pages (13 languages) | [../../tools/site-copy.json](../../tools/site-copy.json) - `tagline`, `features`, `know` |

The site copy is the closest match to a Store description: it is the same product
pitch, already translated and already reviewed for the same audience. Use it as the
draft rather than translating a fourth time.

## Keyword policy - check this per locale, not once

Store policy 10.1.3: at most **7** search terms, and **no third-party product names in
any language**. A machine translation will happily put a competitor's brand into a
description; check all thirteen, not just the English one.

## Honesty note

English, Russian and Ukrainian are proofread by the author. The other ten are machine
translations. The app, the README and the site all say so; the Store description says
so too, and it should keep saying so as long as it is true.
