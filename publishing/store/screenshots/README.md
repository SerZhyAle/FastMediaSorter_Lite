# Store screenshots - 13 locales

Ready-to-upload Microsoft Store listing images, one per UI locale.
**These PNGs are render targets - regenerate them, never edit them by hand**
(canon invariant 16).

| | |
| --- | --- |
| Size | 1920x1080 PNG (Store minimum is 1366x768) |
| Count | 13 - one per locale in [../screenshot-copy.json](../screenshot-copy.json) |
| Weight | ~330 KB each, 4.2 MB total |
| Naming | `screenshot-<store-locale>-1920x1080.png`, where `<store-locale>` is the Partner Center locale, so the file maps to its listing with no guessing |

## Regenerate

```powershell
.\publishing\store\make-store-screenshots.ps1                 # all 13
.\publishing\store\make-store-screenshots.ps1 -Locales de,ar  # a subset
.\publishing\store\make-store-screenshots.ps1 -SkipCapture    # re-render captions only
```

Needs `bin\Release\FastMediaSorter_LITE.exe` - run `.\build.ps1` first. The run
launches the app twice and takes the foreground for about half a minute; don't
touch the machine while it works.

## What is in the picture

A real capture of the running app (`PrintWindow`), showing a generated cat
picture from `C:\Users\Public\Pictures\Cats`, under a caption block in the
locale's own language.

Three deliberate choices, each because the alternative leaks or misleads:

- **The cats are drawn by [../make-cat-samples.ps1](../make-cat-samples.ps1), not photographed.** A listing image
  must be free of third-party rights, and a generated picture provably is. It is
  also flat and simple on purpose - the subject of the screenshot is the app.
- **The sample folder is `C:\Users\Public\Pictures\Cats`.** The app shows its
  current folder in the toolbar and the full file path in the status bar, and
  both end up in the image. A folder under `%TEMP%` would publish the
  developer's Windows account name to the Store.
- **The recipients overlay and the info HUD are forced off during the shoot**
  and restored afterwards. The overlay draws the developer's real
  destination-folder list - including UNC paths - on top of the picture.

## The app in the capture really is in that language

Both the caption **and the app interface inside the capture** are in the
locale's language: the viewer ships 13 UI languages (block A of
[013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md](../../../docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md)),
so the script sets `UiLanguage` and launches the app once per locale - thirteen
captures, not one reused twelve times.

Two things to eyeball when re-shooting, because no test covers them:

- **Arabic and Urdu** get right-to-left text but the window is deliberately
  **not** mirrored (`RightToLeftLayout` stays off - the media geometry,
  the Ambilight bars and the OCR overlay all derive from the PictureBox
  rectangle). Check the picture and its bars sit exactly where they do in the
  other twelve. One known cosmetic artefact: the "1 from 5" counter is still
  built by concatenation, so bidi reorders it to "from 5 1". Harmless, and it
  goes away when that string moves to `Localization.TF` with a placeholder.
- **Hindi, Bengali and Chinese** switch the caption font to Nirmala UI /
  Microsoft YaHei UI. Check for empty boxes and for descenders clipped by the
  Designer's fixed control heights.

## Uploading

Partner Center -> *Store listings* -> `<locale>` -> Screenshots. The Store
requires screenshots only on the default locale; the rest are optional but are
what a shopper in that language actually sees. A locale must exist in the
listing before its image can be attached - see
[013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md](../../../docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md)
§5.1 for the export-then-merge order.
