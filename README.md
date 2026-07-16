# Fast Media Sorter for Windows

Fast Media Sorter for Windows (formerly FastMediaSorter LITE - it is still published under that name in the Microsoft Store and winget) is a Windows Forms application for quickly sorting, viewing, and managing image and video files - because that folder full of 4000 photos is not going to triage itself. It supports a wide range of media formats and provides features such as:

- Fast navigation through large folders of images and videos (even the embarrassingly large ones)
- Slideshow and random file viewing modes
- Recent files and folders tracking, so you don't have to remember where you left off
- File operations: move, copy, rename, and delete
- Recipients panel over the media: a floating list of your hotkey destination folders - click a row to move/copy the current file there (or delete it), so you can sort one-handed with the mouse (off by default; toggle in Settings)
- Image panel for quick visual navigation
- Automatic EXIF orientation correction, for when the camera was held sideways
- High-quality (bicubic) scaling for sharp downscaling of large images
- Optional on-image overlay with the file name and position (handy in full-screen)
- Configurable slideshow interval
- Register the app as the default image viewer and/or video player (per-user, no admin rituals)
- Android Folder Share: turn this PC into an SFTP server for your own media and file folders and open them from the Android app - over your home LAN or across the internet, paired by scanning a QR code (a built-in copilot for the mobile version)
- OCR + on-image translation overlay (local Ollama / LibreTranslate) - for pictures that insist on speaking another language
- Customizable keyboard shortcuts, because reaching for the mouse is so last decade
- Multi-language support (English/Russian; first run follows the Windows display language)
- Broad video format support: everything (H.264/MP4, AVI, ZMBV, VP9, MKV, WMV, ..)
  plays in-window through the bundled **LibVLC** engine - no external codecs, no
  plugin scavenger hunt, and nothing that depends on Internet Explorer still being
  installed

## Mobile Version 📱

Want to sort media from the comfort of the couch? Check out **FastMediaSorter v2** - a powerful Android application with support for local files, network drives (SMB, SFTP, FTP), and cloud storage (Google Drive, OneDrive, Dropbox):

🔗 **[FastMediaSorter for Android website](https://serzhyale.github.io/FastMediaSorter_mob_v2/)** · **[GitHub](https://github.com/SerZhyAle/FastMediaSorter_mob_v2)**

Get it on **[Google Play](https://play.google.com/store/apps/details?id=com.sza.fastmediasorter)**, **[IzzyOnDroid](https://apt.izzysoft.de/fdroid/index/apk/com.sza.fastmediasorter)**, or as a direct **[APK from GitHub Releases](https://github.com/SerZhyAle/FastMediaSorter_mob_v2/releases)**.

Features include:
- Unified interface for all file sources (local, network, cloud)
- Built-in media player and EPUB e-book reader
- Image editing (rotate, flip, filters, adjustments)
- Auto-translation with OCR support
- Favorites system and PIN protection
- Advanced gestures and keyboard navigation

**Desktop as copilot:** Fast Media Sorter for Windows can act as the mobile app's server. Its **Android Folder Share** feature runs a built-in SFTP server for your own media and file folders, so the Android app browses this PC directly - on your home network or over the internet - after a one-time QR pairing. Folder sharing is managed by a bundled tray companion, **Fast Media Sorter: Share Manager** (opened from the viewer's folder right-click or from Settings &rarr; Files and system). Sharing is an explicit opt-in: enable the **server features** (a checkbox during installation, or a one-time button in Share Manager) to add the Windows Firewall rule - until then the app is a pure viewer/sorter. See the step-by-step [guide to publishing your folders for Android](https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html).

## Usage

1. Select a folder containing your media files (yes, that one).
2. Navigate files using keyboard, mouse, or on-screen buttons - whatever your hands prefer.
3. Use the panel and recent files features for quick access.
4. Move, copy, or delete files as needed. Press a digit, watch the chaos shrink.
5. Enjoy fast and efficient media sorting, and maybe finally reclaim that disk space.

## Two programs, one app

The package ships **two viewers side by side in the same folder**, and the installer
sets up both. You never have to choose: the shortcut and file associations
automatically point at the one that actually runs on your machine.

- **`FastMediaSorter_LITE.exe`** - the main program, now 64-bit and built on
  .NET 10. It replaces the previous version in place and keeps all your settings.
  **Nothing to install** - the .NET runtime lives inside the program itself, so the
  old ".NET Framework 4.8 required" line is gone for good. Needs Windows 10 version
  1607 or newer, Windows 11, or Windows Server 2016+.
- **`FastMediaSorter_x86.exe`** - the same viewer as a small 32-bit program (~3 MB)
  for Windows 7/8.1 and 32-bit Windows, where the modern runtime simply cannot run.
  It needs .NET Framework 4.8, which those Windows versions either already have or
  can get. It is not a cut-down edition - OCR, translation and folder sharing are
  all still in there.

They are **one application**, not two rivals: one set of settings, one window. Start
one while the other is open and it just brings the open window forward with your
file - no second window, no squabbling over whose settings get saved. Both also
share the same adjacent libraries (codecs, OCR/translation models, folder sharing),
each automatically picking the ones matching its own bitness.

### What the 64-bit program fixes

- **Animated WEBP now opens everywhere.** The app decodes WEBP itself instead of
  depending on the Windows "WebP Image Extensions" codec, which server editions of
  Windows don't ship - animated WEBP used to simply fail there.
- **Video always plays through the built-in VLC engine.** The retired Internet
  Explorer component is no longer used, so playback works the same on systems where
  IE has been shown the door. (The 32-bit program keeps the classic path.)

## Requirements

Short version: the installer works this out for you (see above). Long version:

| | `FastMediaSorter_LITE.exe` (main, 64-bit) | `FastMediaSorter_x86.exe` (32-bit) |
|---|---|---|
| **Windows** | 10 version 1607+, 11, Server 2016+ | 7, 8.1, 10, 11 - including 32-bit |
| **Runtime** | none - it is inside the program | .NET Framework 4.8 |

## Installation

### winget

```powershell
winget install --id SerZhyAle.FastMediaSorter
```

The winget package uses the Inno Setup installer in silent per-user mode, so it
does not show dialogs during installation - it just quietly gets on with it.

### Manual release

Download the latest assets from the
[Releases page](https://github.com/SerZhyAle/FastMediaSorter_Lite/releases):

- `FastMediaSorter-<version>-windows-x64-setup.exe` for the easiest install
- `FastMediaSorter-<version>-windows-x64.zip` for a portable offline bundle

Both assets contain **both programs** - the asset names have not changed.

The interactive setup can optionally register the app for common
image formats. On Windows 10/11, the system may still ask you to confirm the
choice once in Default Apps.

Both GitHub release assets are offline-ready: they already include VLC runtimes
and OCR language packs, so no first-run download is needed for media playback or
OCR recognition.

### What's in the package (and why it's large)

The main program is a single ~110 MB exe because it carries its own .NET runtime -
that heft is exactly why there is nothing to install: the runtime comes along for
the ride instead of being demanded from your machine. (The 32-bit program beside it
is ~3 MB.) Most of the rest of the download is optional, **offline-ready** payload,
bundled so the app works with no first-run download:

- **Video codecs (VLC)** (~100 MB) - offline playback of AVI, MKV, VP9 and other
  formats the built-in Windows player can't decode.
- **OCR & translation models** (~45 MB) - on-image text recognition without a
  network round-trip.
- **Android Folder Share companion** (~120 MB) - a self-contained .NET app that
  carries its own runtime (Windows does not ship the modern .NET it needs), used
  to share folders to your phone.

The bundled libraries are the 64-bit ones, for the main program. The 32-bit program
downloads the pieces it needs on first use.

The interactive installer exposes these as **selectable components** and shows each
one's size: uncheck what you don't need and those parts download later on demand,
or the feature is simply left out. A viewer-only ("Compact") install is a fraction
of the full package. (Silent/winget installs always take the full set.)

Note: machine translation still depends on the provider you configure - the app
is generous, but it won't translate by sheer willpower. Ollama requires a
separate local install/model, while cloud providers need network access.

## Companion App

For EPUB, PDF, FB2, MOBI, TXT, Markdown, and HTML document conversion, use the
companion project **doc-html-translate**:

```powershell
winget install SerZhyAle.DocHtmlTranslate
```

Project page: [doc-html-translate](https://github.com/SerZhyAle/doc-html-translate)

## Universal Agent Kit

A handy collection of AI-agent presets and tooling by the same author:

🔗 **[Universal Agent Kit](https://serzhyale.github.io/universal-agent-kit/)**

## Author
- Website: [sza.od.ua](https://sza.od.ua)
- Email: [sza@ukr.net](mailto:sza@ukr.net)

## License
MIT - see [LICENSE](LICENSE).
