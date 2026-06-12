# FastMediaSorter LITE

FastMediaSorter LITE is a Windows Forms application for quickly sorting, viewing, and managing image and video files. It supports a wide range of media formats and provides features such as:

- Fast navigation through large folders of images and videos
- Slideshow and random file viewing modes
- Recent files and folders tracking
- File operations: move, copy, rename, and delete
- Image panel for quick visual navigation
- Customizable keyboard shortcuts for efficient workflow
- Multi-language support (English/Russian)
- Broad video format support: common H.264/MP4 plays in-window, and anything the
  built-in player can't decode (AVI, ZMBV, VP9, MKV, WMV, …) automatically falls
  back to a bundled **LibVLC** engine — no external codecs or players required

## Mobile Version 📱

Looking for a mobile solution? Check out **FastMediaSorter v2** - a powerful Android application with support for local files, network drives (SMB, SFTP, FTP), and cloud storage (Google Drive, OneDrive, Dropbox):

🔗 **[FastMediaSorter v2 for Android](https://github.com/SerZhyAle/FastMediaSorter_mob_v2)**

Features include:
- Unified interface for all file sources (local, network, cloud)
- Built-in media player and EPUB e-book reader
- Image editing (rotate, flip, filters, adjustments)
- Auto-translation with OCR support
- Favorites system and PIN protection
- Advanced gestures and keyboard navigation

## Usage

1. Select a folder containing your media files.
2. Navigate files using keyboard, mouse, or on-screen buttons.
3. Use the panel and recent files features for quick access.
4. Move, copy, or delete files as needed.
5. Enjoy fast and efficient media sorting!

## Requirements
- Windows 7/10/11
- .NET Framework 4.8

## Installation

### winget

```powershell
winget install --id SerZhyAle.FastMediaSorter
```

The winget package uses the Inno Setup installer in silent per-user mode, so it
does not show dialogs during installation.

### Manual release

Download the latest assets from the
[Releases page](https://github.com/SerZhyAle/FastMediaSorter_Lite/releases):

- `FastMediaSorter-<version>-windows-x64-setup.exe` for the easiest install
- `FastMediaSorter-<version>-windows-x64.zip` for a portable offline bundle

The interactive setup can optionally register FastMediaSorter LITE for common
image formats. On Windows 10/11, the system may still ask you to confirm the
choice once in Default Apps.

Both GitHub release assets are offline-ready: they already include VLC runtimes
and OCR language packs, so no first-run download is needed for media playback or
OCR recognition.

Note: machine translation still depends on the provider you configure. For
example, Ollama requires a separate local install/model, while cloud providers
need network access.

## Companion App

For EPUB, PDF, FB2, MOBI, TXT, Markdown, and HTML document conversion, use the
companion project **doc-html-translate**:

```powershell
winget install SerZhyAle.DocHtmlTranslate
```

Project page: [doc-html-translate](https://github.com/SerZhyAle/doc-html-translate)

## Author
- Website: [sza.od.ua](https://sza.od.ua)
- Email: [sza@ukr.net](mailto:sza@ukr.net)

## License
MIT — see [LICENSE](LICENSE).
