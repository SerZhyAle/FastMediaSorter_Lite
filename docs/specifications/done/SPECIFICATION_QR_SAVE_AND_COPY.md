# Specification - Save the QR code as an image and put it on the clipboard

> Status: **implemented, 2026-08-11**. Owner decisions of 2026-08-08 are folded in (§1). Implementation notes are at the end (§12).
>
> Scope: this repository only, and inside it the **Share Manager** (Companion) - [Qr_Zoom_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Qr_Zoom_Form.vb) is the one window every QR passes through, so the whole feature lands in one file plus its strings. LITE and the net48 x86 fallback are untouched.
>
> Related: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](SPECIFICATION_ANDROID_FOLDER_SHARE.md) (what the QR carries), [SPECIFICATION_SHARE_COMPANION_APP.md](SPECIFICATION_SHARE_COMPANION_APP.md), [SPECIFICATION_STREAMSPLAYER_BROADCAST_RESOURCE.md](../SPECIFICATION_STREAMSPLAYER_BROADCAST_RESOURCE.md) (a third QR that inherits this behaviour for free).

---

## 0. Outcome

Today a QR code can only be **looked at**. To get it to another person the user has to photograph the screen or save the `.fmscfg` and explain what to do with it.

After this change, **one left click on the code** hands it over as a picture:

1. It is written to a PNG file in the user's Pictures folder.
2. The same picture goes on the clipboard - as a bitmap **and** as a file.
3. `Ctrl+V` in Telegram, WhatsApp, Word, or an email pastes the code as an image; a paste in Explorer or in an email's attachment area drops the PNG file itself.

The click keeps doing what it already does - stepping the zoom - so nothing is taken away.

---

## 1. Owner decisions taken on 2026-08-08

1. **The left click does everything at once**: step the zoom **and** save the file **and** refresh the clipboard. Not a second gesture, not a double click, not a button the user has to find.
2. **The file goes to `Pictures\Fast Media Sorter\`**, written straight away with no Save-as dialog.
3. **The clipboard carries the picture and the file together**, so the receiving application takes whichever form it understands.

---

## 2. Where it applies

Every QR in the product already opens through `Qr_Zoom_Form`, so all of these gain the behaviour from one change and none of them needs its own code:

| Entry point | How the window opens |
|---|---|
| Package wizard ("one-shot package") | `OnShowQr` -> `Qr_Zoom_Form.ShowImage` ([PackageWizardForm.vb:570-573](../../../src/FastMediaSorterCompanion/Forms/PackageWizardForm.vb#L570-L573)) |
| Tray "Show the code" | `Qr_Zoom_Form.ShowImage` with a hidden owner (the tray-resident stand-alone modal path) |
| Any QR PictureBox | `Qr_Zoom_Form.ShowZoomed` |
| **Future**: the broadcast resource QR | inherits this with no further work |

Consequence to honour: `ShowImage` **clones** the image it is given because the async status poll may rebuild and dispose the original under the modal loop. The save path must use the window's own clone, never reach back to the caller's image.

---

## 3. The gesture model

| Gesture | Today | After |
|---|---|---|
| **Left click on the code** | Step the zoom (x2, x2, .., full working area, back to entry size) | **Same zoom step, plus save the PNG, plus put it on the clipboard** |
| Right / middle click | Close | Unchanged |
| `Esc` / `Enter` | Close | Unchanged |
| Drag the frame | Resize, kept square | Unchanged |
| `Ctrl+C` | - | Clipboard only (no file) - the keyboard equivalent, for anyone who does not want a file |
| `Ctrl+S` | - | Save only (no clipboard change) |

**Repeated clicks must not litter.** The output file name is decided **once per window**, at the first click, and every later click in the same window **overwrites that same file**. Five clicks to enlarge the code leave one PNG, not five. Reopening the window starts a new name (a new timestamp), so two different codes never collide.

The window title carries the new contract, since the title is the only chrome this window has:
`"QR code - click to enlarge, save and copy; Esc closes"` (through `Localization.T`, Russian source string as the key, as everywhere else).

---

## 4. The saved image

- **Format**: PNG. The source bytes already are a PNG produced by `PngByteQRCode.GetGraphic(10)` with quiet zones - **10 pixels per module, ECC level M** ([ShareConfigBuilder.vb:335-338](../../../src/FastMediaSorterCompanion/Core/ShareConfigBuilder.vb#L335-L338)) - which for a real `.fmscfg` payload lands around 600-1000 px square. That is already a good size to send.
- **Save the code's own pixels, not the zoomed view.** The file must not depend on how far the user happened to have zoomed the window; the whole point is a clean, exactly-square, black-and-white code.
- **Floor at 512 px.** If the source image is smaller than 512 px on a side, upscale by an integer factor with **nearest-neighbour** interpolation (the same reason `QrBox` overrides `OnPaint`: smoothing turns hard modules into grey gradients, and a messenger's own re-compression on top of that is what makes a code unreadable). Never upscale by a non-integer factor, and never downscale.
- **Keep the quiet zone.** A code pasted edge-to-edge against a dark chat bubble does not scan. The generator already includes it; nothing may crop it.
- **Folder**: `%USERPROFILE%\Pictures\Fast Media Sorter\`, resolved through `Environment.SpecialFolder.MyPictures` and created on demand. If it cannot be created or written, fall back to `%TEMP%\FastMediaSorter\` and say so in the hint - never fail silently, never throw into the modal loop.
- **Name**: `fms-qr-<yyyyMMdd-HHmm>.png`, in local time. An optional caller-supplied base name (`ShowImage(owner, img, baseName)`) lets a future broadcast QR write `fms-qr-dune-20260808-2153.png`; the base name is sanitized to a safe file-name subset and truncated, and an empty one falls back to the plain form.

---

## 5. The clipboard payload

One `DataObject` carrying every form, set with `Clipboard.SetDataObject(data, copy:=True)` so the content survives the Share Manager closing:

| Format | Consumed by |
|---|---|
| `CF_BITMAP` / `CF_DIB` (`DataFormats.Bitmap`) | Telegram, WhatsApp Desktop, Word, most chats - pastes as a picture |
| `"PNG"` (a `MemoryStream` of the exact PNG bytes) | Applications that prefer lossless PNG over a DIB |
| `CF_HDROP` (`DataFormats.FileDrop`, one entry - the file written in §4) | Explorer, mail clients, upload fields - pastes the file itself |

Notes that decide whether this actually works:

- The DIB form must be a **plain opaque bitmap on white**. A QR has no transparency, and a DIB with an alpha channel is exactly what turns a pasted image black in some receivers.
- The file entry is what makes "paste into an email as an attachment" work, and it is the reason the file is written **before** the clipboard is set: `CF_HDROP` naming a file that does not exist yet is a broken paste.
- `Ctrl+C` uses the same payload minus nothing - the file already exists by then if a click preceded it; if not, the keyboard path writes it first, same as a click.

---

## 6. Feedback and failure

- **Never a MessageBox.** It would interrupt a flow whose whole shape is "click, click, click". Feedback is a short-lived `ToolTip` near the cursor plus the window title switching to the result line for ~2 seconds, then back.
- Success: `"Saved and copied: {0}"` with the file name (through `Localization.TF` - a value in a sentence never gets concatenated).
- The Pictures folder is not writable: fall back per §4 and report the fallback, still copying to the clipboard.
- The clipboard is locked by another process (a real and common Windows failure): retry once after a short delay, then report `"Could not copy to the clipboard"` - and keep the saved file, which is still useful.
- Every failure path is caught inside the handler. A modal window over a tray-resident app must not die on a full disk.

---

## 7. Security and privacy - one thing the user must be told

**The QR is a credential.** A `.fmscfg` code carries the SFTP user name and password and grants access to every shared folder; the product already warns "do not publish the QR code or the `.fmscfg` file" ([ShareText.vb:13](../../../src/FastMediaSorterCompanion/Core/ShareText.vb#L13)). Writing it as a picture into `Pictures\` changes its exposure in two concrete ways:

1. **`Pictures` is commonly synchronized** - OneDrive backs it up by default on many Windows installations, so the code may leave the machine without the user doing anything.
2. A picture in `Pictures` is what a phone-backup or a photo app happily indexes.

This is the owner's chosen location and it ships as chosen. The obligation this specification puts on the implementation is that the user is **told once, where it matters**: the first save in a session appends one sentence to the hint - `"The image contains access to your folders - do not publish it."` The wording is a variant of the existing warning, not a new claim, and it is a hint line, not a dialog.

Two things that follow and are not negotiable:

- **No automatic cleanup, no hidden deletion.** The file is the user's; the app never removes it behind their back.
- **The QR contents are never logged**, and the log line for this action records the file name only - never the payload, never the password.

The privacy page needs no change: nothing here contacts the network, and no new data is collected.

---

## 8. Localization

New strings land in all 13 languages in the same change (invariant 17): the window title, the success line (`TF`, one placeholder), the fallback-folder line, the clipboard-failure line, and the credential warning. `LocalizationParityTests` and `LocalizationCoverageTests` must stay green; no smart double quotes in any literal.

---

## 9. Files

- [src/FastMediaSorterCompanion/Forms/Qr_Zoom_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Qr_Zoom_Form.vb) - the click handler, the save + clipboard code, `Ctrl+C` / `Ctrl+S`, the per-window file-name latch, the tooltip, the optional `baseName` parameter on `ShowImage`/`ShowZoomed`.
- `src/FastMediaSorterCompanion/Localization/*` - the new keys.
- Callers pass a base name where they have a meaningful one (package wizard, and later the broadcast page). Nothing else changes.
- `CHANGELOG.md` - one line under `[Unreleased]`, in English.

No new project reference, no new dependency, no new file - the whole feature is one window plus strings.

---

## 10. Acceptance criteria

1. A left click on the code enlarges it **and** leaves a PNG in `Pictures\Fast Media Sorter\` **and** refreshes the clipboard - all three, from one click.
2. `Ctrl+V` in Telegram Desktop pastes the code as an image, and the pasted image **scans** with a phone from the screen.
3. `Ctrl+V` in Explorer (or an email attachment area) drops the PNG file.
4. Five clicks in one window leave exactly **one** file; closing and reopening produces a second, differently-named one.
5. The saved image is identical in pixel size regardless of the zoom step the window is on, is at least 512 px square, and keeps its quiet zone.
6. With `Pictures` made read-only, the click still copies to the clipboard, writes to the `%TEMP%` fallback, and says which.
7. With the clipboard held by another process, the file is still saved and the failure is reported without an exception dialog.
8. The credential warning appears on the first save in a session and is not repeated on every click.
9. The tray "Show the code" path (hidden owner, stand-alone modal) behaves identically to the package-wizard path.
10. Both localization test suites stay green; the new strings exist in all 13 languages.

---

## 11. Out of scope

- **Sending the picture anywhere.** No mail, no upload, no "share to.." - the clipboard and the file are the handoff. (The existing `.fmscfg`-by-mail action is untouched.)
- **A QR with a logo, a caption, or a frame.** A decorated code is a code that some scanner refuses; the picture stays the raw code plus its quiet zone.
- **Other image formats** (JPEG, SVG, BMP). JPEG in particular is the wrong format for hard black-and-white modules.
- **Printing.**
- **LITE and the net48 x86 fallback** - neither has a QR surface.

---

## 12. Implementation notes (2026-08-11)

Everything above shipped as written, with two points worth recording:

1. **`Ctrl+C` and the file entry - §3 wins over the last sentence of §5.** The two sections disagreed: §3's gesture table says `Ctrl+C` is "clipboard only (no file)", while §5 ends with "if not, the keyboard path writes it first, same as a click". Writing a file behind a copy-only gesture is exactly what §3 promises not to do, so `Ctrl+C` never creates one - and, to honour §5's real constraint ("`CF_HDROP` naming a file that does not exist yet is a broken paste"), it includes the `CF_HDROP` entry only when this window has already written the file (a preceding click or `Ctrl+S`). Left click and `Ctrl+S` are unaffected: they write the file first, then set the clipboard.
2. **The base name the package wizard passes** is the folder the code is for, when the code is for exactly one folder (its per-recipient label, else the folder name). With several folders there is no honest name, so it passes an empty one and the plain timestamped form is used.

Where it lives: the window is [Qr_Zoom_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Qr_Zoom_Form.vb) (the "Save and clipboard" region), the strings are in [Localization.Wizard.vb](../../../src/FastMediaSorterCompanion/Localization/Localization.Wizard.vb), and the size/name rules that no manual click can check reliably - the 512 px floor, the whole-number nearest-neighbour factor, the never-downscale rule, the kept quiet zone, the file-name shape - are covered by [QrImageOutputTests.vb](../../../tests/Companion.Tests/QrImageOutputTests.vb).
