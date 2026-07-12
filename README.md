# Fast Media Sorter for Windows

Fast Media Sorter for Windows (formerly FastMediaSorter LITE - it is still published under that name in the Microsoft Store and winget) is a Windows Forms application for quickly sorting, viewing, and managing image and video files - because that folder full of 4000 photos is not going to triage itself. It supports a wide range of media formats and provides features such as:

- Fast navigation through large folders of images and videos (even the embarrassingly large ones)
- Slideshow and random file viewing modes
- Recent files and folders tracking, so you don't have to remember where you left off
- File operations: move, copy, rename, and delete
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
- Broad video format support: common H.264/MP4 plays in-window, and anything the
  built-in player can't decode (AVI, ZMBV, VP9, MKV, WMV, ..) automatically falls
  back to a bundled **LibVLC** engine - no external codecs, no plugin scavenger hunt

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

**Desktop as copilot:** Fast Media Sorter for Windows can act as the mobile app's server. Its **Android Folder Share** feature runs a built-in SFTP server for your own media and file folders, so the Android app browses this PC directly - on your home network or over the internet - after a one-time QR pairing. Sharing is an explicit opt-in: enable the **server features** (a checkbox during installation, or a one-time "Install server features" button in Settings &rarr; Share) to add the Windows Firewall rule - until then the app is a pure viewer/sorter. See the step-by-step [guide to publishing your folders for Android](https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html).

## Usage

1. Select a folder containing your media files (yes, that one).
2. Navigate files using keyboard, mouse, or on-screen buttons - whatever your hands prefer.
3. Use the panel and recent files features for quick access.
4. Move, copy, or delete files as needed. Press a digit, watch the chaos shrink.
5. Enjoy fast and efficient media sorting, and maybe finally reclaim that disk space.

## Requirements
- Windows 7/10/11
- .NET Framework 4.8

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

The interactive setup can optionally register the app for common
image formats. On Windows 10/11, the system may still ask you to confirm the
choice once in Default Apps.

Both GitHub release assets are offline-ready: they already include VLC runtimes
and OCR language packs, so no first-run download is needed for media playback or
OCR recognition.

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
