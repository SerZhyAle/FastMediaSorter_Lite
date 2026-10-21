# Fast Media Sorter & Sharing for Windows

Fast Media Sorter & Sharing for Windows is a Windows Forms application for quickly sorting, viewing, and managing image and video files - because that folder full of 4000 photos is not going to triage itself. **Sharing** means giving your media to someone else: the folders you pick can be opened from the Android app, from another copy of this program, or from any SFTP client (see [Your folders over SFTP](#your-folders-over-sftp), [publishing folders for Android](https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html)).

The name changed from "Fast Media Sorter for Windows" (earlier still, "FastMediaSorter LITE"). The Microsoft Store, Add/Remove Programs and winget still list the package as "FastMediaSorter LITE" - that name is frozen on purpose, so upgrades keep correlating with installed copies.

**Three optional parts.** A media viewer and sorter, a local SFTP folder-sharing service, and a Media Exchange Server (in development, not released yet) - start with the viewer or with the folder-sharing server, and add the rest only if you want it.

| Part | You want to .. | Where it comes from | Notes |
| --- | --- | --- | --- |
| **Media viewer and sorter** | sort and look at photos and videos | the **full package** (one installer, per user, no administrator rights), the portable ZIP, winget or the Microsoft Store | the core of the full package; an optional, unticked component of the Server edition. Sorting needs no server |
| **Local SFTP folder sharing** | give this PC's folders to your phone, another copy of this program or any SFTP client | the full package with sharing ticked (off until you switch it on; the **Compact** type leaves it out) - or the **Server edition** for a machine that must serve with nobody signed in (a Windows service, administrator rights; [server page](https://serzhyale.github.io/FastMediaSorter_Lite/server.html)) | FileDO disk hosting is a capability of sharing: unticked in the full package, ticked in the Server edition. The Microsoft Store version carries the Share Manager but no Windows service and no FileDO disk hosting |
| <img src="assets/exchange-mark/svg/exchange-mark-small.svg" width="24" height="24" alt="Exchange server"> **Media Exchange Server** | *in development, not released yet* | in no package - nothing to download | a separate server for devices that cannot reach each other directly; neither the viewer nor local sharing needs it |

The full package is the one download that carries the viewer and the folder-sharing service together. The Server edition is a focused package in which the folder-sharing service leads and the media viewer rides along as an optional, unticked component. What each package installs by default, which parts are optional, whether hosting runs interactively or unattended and where elevation is needed is the [availability matrix](https://serzhyale.github.io/FastMediaSorter_Lite/server.html#matrix) on the server page.

It supports a wide range of media formats and provides features such as:

- Settings keep your last page, expanded groups and approximate scroll position. Language and theme are in General; use Search settings or `Ctrl+F` to find a caption, explanation or choice, and Expand all / Collapse all for the current page (64-bit program).
- Storage owns the cache budgets and cleanup actions. Thumbnail size uses four real pixel tiers; General offers a backed-up reset of additional preferences.
- Fast navigation through large folders of images and videos (even the embarrassingly large ones)
- Slideshow and random file viewing modes
- Recent files and folders tracking, so you don't have to remember where you left off
- Pick up where you stopped: switch on **"Resume the last file"** and a plain start reopens the exact file that was on screen last time, with its folder listed behind it - and a video carries on from the position it was left at. Off unless you ask for it; a file that has since gone simply leaves the window empty (64-bit program)
- File operations: move, copy, rename, and delete
- Deleting is no longer the one thing you cannot take back: `DEL` sends the file to the **Recycle Bin** where one exists, `Shift+DEL` goes past it on purpose, and every permanent deletion names its reason - a network drive and a memory stick have no bin at all, and the app says so instead of pretending. `U` walks back through the **last 50 operations**, renames and deletions included, each file returning to its folder and to its place in the list (64-bit program)
- Move or copy, decided as you go: a digit `1..9`/`0` moves the current file into that folder, **Shift + the same top-row digit copies it there** - no mode to switch on beforehand and forget about. The same pair is on the recipients panel, in both media menus, and in the recipients table (Shift + double-click). Whether a copy then jumps to the next file is one checkbox in Settings, on by default (64-bit program)
- Recipients panel over the media: a floating list of your hotkey destination folders - click a row to move the current file there, the narrow zone beside it to copy it (or delete it), so you can sort one-handed with the mouse (off by default; toggle in Settings)
- Image panel for quick visual navigation
- **A ZIP, CBZ, 7z, RAR or CBR opens like a folder** - a comic or a folder of scans browses, slideshows, zooms and translates exactly like loose files, with nothing unpacked in advance and nothing left behind afterwards (64-bit program)
- **Mark up and crop a picture in its own window** - brush, rectangle and ellipse, colour and thickness, a live crop frame with handles, `Ctrl+Z`, then save over the original or beside it in any of six formats. Your EXIF comes with it (64-bit program)
- **Turn an animation into a real video** - an animated WEBP, APNG or GIF becomes an ordinary MP4 next to it, with a seek bar, a pause, hardware decoding and full colour, and replaces the original. The encoder (FFmpeg, GPL, ~110 MB) is not bundled: the first conversion asks whether to download it. Because the original is deleted permanently, the app says so first, names both files, and warns when transparency is about to be flattened onto black (64-bit program)
- **A slow format opens once, not once per view** - animated WEBP, AVIF and HEIC have no Windows codec, so the app decodes them itself; that work is now kept on disk and reused while the file is unchanged, so the "Loading.." badge appears once per file instead of every time you come back to it. Visible, clearable and capped in Settings; `0` switches it off (64-bit program)
- Automatic EXIF orientation correction, for when the camera was held sideways
- High-quality (bicubic) scaling for sharp downscaling of large images
- Optional on-image overlay with the file name and position (handy in full-screen)
- Configurable slideshow interval
- Register the app as the default image viewer and/or video player (per-user, no admin rituals)
- Folder sharing over SFTP ("Android Folder Share"): turn this PC into an SFTP server for the folders you pick, and open them from the Android app - or from any other SFTP client - over your home LAN or across the internet, paired by scanning a QR code. Ships switched off; see [Your folders over SFTP](#your-folders-over-sftp). 64-bit program only
- OCR + on-image translation overlay (local Ollama / LibreTranslate) - for pictures that insist on speaking another language
- Perspective ("Ambilight-style") background: the bars beside a photo are filled with colour drawn from its own edges, so it sits in its own light instead of on black. Optionally as a halo that fades out into the background (64-bit program)
- Zoom at the mouse cursor on the grey NumPad keys (or `=` and `-` on the main row), with drag-to-pan (64-bit program)
- A video control bar - seek, time, mute, volume - plus audio-track and subtitle picking remembered by language, and "Open URL.." to play a video straight off `smb://`, `sftp://`, `http(s)://` and friends (64-bit program)
- **A music file gets a screen, not a black rectangle** - MP3, WAV, M4A, WMA and OGG show their embedded album cover, or a wave-and-particle animation when there is none, with the title, artist, album, codec, bitrate and length along the bottom. Its own Settings section decides what happens when the track ends, whether the transport panel stays up, and a sleep timer of up to three hours. Video and audio can each be left out of paging entirely, so a folder of photographs stays a folder of photographs (64-bit program)
- Menus right on the media: middle-click a picture, right-click a video (64-bit program)
- Customizable keyboard shortcuts, because reaching for the mouse is so last decade
- **In-app context help on every window** - press `F1` or click the `?` button in the top-right corner to open offline, privacy-respecting documentation with screenshots and element explanations in all 13 languages. The viewer's F1 shortcut card links directly to the full guide (64-bit program and Share Manager)
- **Interface in 13 languages** - English, Russian, Ukrainian, German, Italian, Spanish, French, Portuguese, Arabic, Hindi, Bengali, Urdu and Chinese. The first run follows your Windows display language; the toolbar button switches it any time, and the Share Manager follows the same choice. Arabic and Urdu are laid out right to left. Only English, Russian and Ukrainian are proofread by the author - **the other ten are machine translations and are not proofread**, so corrections through the [issue tracker](https://github.com/SerZhyAle/FastMediaSorter_Lite/issues) are very welcome. The 32-bit fallback build keeps English and Russian only (Windows 7 has no font for the Indic scripts), and the setup wizard stays English.
- Broad video format support: everything (H.264/MP4, AVI, ZMBV, VP9, MKV, WMV, ..)
  plays in-window through the bundled **LibVLC** engine - no external codecs, no
  plugin scavenger hunt, and nothing that depends on Internet Explorer still being
  installed

## Android version

Want to sort media from the comfort of the couch? Check out **Fast Media Sorter & Organizer** - a powerful Android application with support for local files, network drives (SMB, SFTP, FTP), and cloud storage (Google Drive, OneDrive, Dropbox):

**[FastMediaSorter for Android website](https://serzhyale.github.io/FastMediaSorter_mob_v2/)** · **[GitHub](https://github.com/SerZhyAle/FastMediaSorter_mob_v2)**

Get it on **[Google Play](https://play.google.com/store/apps/details?id=com.sza.fastmediasorter)**, **[IzzyOnDroid](https://apt.izzysoft.de/fdroid/index/apk/com.sza.fastmediasorter)**, or as a direct **[APK from GitHub Releases](https://github.com/SerZhyAle/FastMediaSorter_mob_v2/releases)**.

Features include:
- Unified interface for all file sources (local, network, cloud)
- Built-in media player and EPUB e-book reader
- Image editing (rotate, flip, filters, adjustments)
- Auto-translation with OCR support
- Favorites system and PIN protection
- Advanced gestures and keyboard navigation

**Desktop as copilot:** Fast Media Sorter can act as the Android app's server - its built-in SFTP service publishes the folders you pick, so the Android app browses this PC directly, on your home network or over the internet. That is the next section.

## Your folders over SFTP

**FileDO disks:** with the optional FileDO disk component installed, start Share Manager, then register a file-backed disk from FileDO's Disk Manager using Share with Fast Media Sorter & Sharing.. (`Ctrl+Shift+S`). Choose a root name and read-only or read/write access; sealed disks are read-only. FileDO confirms a clean transfer of an already mounted disk and explicitly asks before disabling its local automatic mounting. Registration, opening, and FMS autostart are separate choices. In Share Manager, open **.fdd disks.. -> Connect .fdd..** to select a container, root name and read-only access. Choose **No letter - sharing only** or a free Windows drive letter; that letter is a local alias for the current Windows session and is removed when the disk closes. The disk list shows state, holder, open files and autostart, with open, clean close, explicit force close and remove actions. FileDO registrations appear here too. **Mounted drive..** publishes an existing drive through the ordinary folder flow; its mounting remains your responsibility. If FileDO already holds a container, save and close it there before connecting. Disk control requires a compatible FileDO component and an administrator-capable worker; the ordinary LocalService host cannot mount containers. Updating the UI alone does not change that service account. Never add the worker's temporary mount path as an ordinary folder. Closing retains registration; stopping sharing also deletes the disk's saved autostart key. Pair clients with the existing QR or `.fmscfg` flow: they see the open disk as an SFTP folder, not a Windows SMB share. Encrypted disks are unlocked on the PC; autostart stores their credential in the host's protected store only after explicit consent. Session mode requires sign-in; the service host works without sign-in. If a transfer fails, refresh FileDO's state before retrying or restoring local mounting. Partition disks and the unverified FileDO Store control path are unavailable in this workflow. [FileDO disk guide](docs/how-to-filedo-disks.html) · [FileDO documentation](https://serzhyale.github.io/FileDO/guides/virtual-disks.html#sharing).

Sorting media does not have to mean copying it somewhere first. **Android Folder Share** turns this PC
into an SFTP server for the folders you choose, so a sorter running somewhere else works on the
files where they already live - nothing is uploaded, no cloud account is involved, and the folders stay
exactly where they are. It is a plain, standard SFTP service, so those folders open in:

- **FastMediaSorter for Android** - the case it was built for: the phone sorts the photos and videos
  sitting on this PC's disks, over your home Wi-Fi or across the internet, after a one-time pairing by
  QR code (or an exported `.fmscfg` file). Both carry the login password (and any folder PIN), so
  hand them over like a key, never post them.
- **Any other SFTP client** - WinSCP, FileZilla, a file manager on Linux or macOS, another PC. The
  address and port, the login, the password and the host-key fingerprint are all shown in the Share
  Manager window, each with a copy button.
  The port is yours to set: it is picked once, on the very first start, and never moves on its own
  afterwards - so a forwarding rule typed into your router, an exported `.fmscfg` and a QR code you
  have already handed out all stay valid.

What it does, and what it deliberately does not:

- **Only the folders you add are visible**, each under a name you give it, and any of them can be
  published read-only.
- **It ships switched off.** The whole sharing surface stays dormant until you enable **server
  features** - a checkbox during installation, or a one-time button in Share Manager that adds the one
  Windows Firewall rule the service needs. Until then nothing listens and the app is a pure
  viewer/sorter.
- **The connection is encrypted and direct** (SSH/SFTP), device to PC. The client pins this PC's host
  key at the first pairing, so a substituted server is noticed rather than trusted.
- **Sharing is managed by a separate program** - the bundled tray companion **Share Manager - Fast
  Media Sorter & Sharing**, opened from the viewer's folder right-click or from Settings &rarr; Server and resources.
  Being a 64-bit program itself, it is why folder sharing is 64-bit only.

Step by step, from the first click to the router: [guide to publishing your folders for Android](https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html).

### Ways to connect your devices

Sharing folders from this PC is one of three ways to connect your devices. The shared portal
**[Media exchange between your devices](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/)** puts the ways side by side, for every program of the family.

- **Your home network - works today.** This PC is the SFTP server: Share Manager serves the folders you pick,
  the phone pairs once by QR code or .fmscfg file and reaches the PC over Wi-Fi at home, or through your router
  with one forwarded port (Share Manager tries to open that port itself and then checks it from the internet).
  Reader's guides: [Home network](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/home-network.html) and [Windows servers](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/windows-servers.html).
- **Live broadcasts on Windows.** Playing a live broadcast on Windows today is done by
  [STREAMS Player](https://serzhyale.github.io/StreamsPlayer/), a separate program. This program does not broadcast.
**Always-on variant (optional):** by default the folders are reachable only while you are signed in and the Share Manager is running - right for a normal PC. If the machine's job is to stay available (a dedicated server, a VPS, an always-on home server), the same worker can be hosted by a Windows service instead: it starts at boot, serves with nobody logged on, and restarts itself if it fails. There are two ways in. An ordinary installation can switch itself over in the Share Manager (**SFTP sharing settings -> Hosting -> Switch sharing to a Windows service..**, one administrator prompt that also moves the state and grants the service access to the folders you share), or you can install the separate **[Folder Share Server edition](https://serzhyale.github.io/FastMediaSorter_Lite/server.html)** (`winget install --id SerZhyAle.FastMediaSorter.Server`), which sets the same service up straight away - for a machine provisioned without ever opening the app. In both cases it takes administrator approval once and is never enabled silently: no regular setup, winget install or Store install registers the service on its own, and the Store build cannot register one at all. SFTP, the QR/`.fmscfg` pairing and the Android app are identical - only the host differs, and switching editions preserves the host key so paired phones need no re-pairing. The Server edition is a focused package: the server leads, and the media viewer is inside as an optional component that stays unticked unless you choose it.

## Broadcasts for StreamsPlayer

In the 64-bit viewer, right-click a video or audio file or its folder and choose Broadcast. Share Manager exports a .spres file, QR code or quick link for import in StreamsPlayer. A folder is one playlist channel with a frozen list; Refresh replaces that list and Revoke stops its link. The HTTP gateway has its own switch, off by default. Each token grants read-only access to one broadcast and normally expires after 24 hours. HTTP is unencrypted: share the document privately and keep internet access off unless you need it.

## Usage

1. Select a folder containing your media files (yes, that one).
2. Navigate files using keyboard, mouse, or on-screen buttons - whatever your hands prefer.
3. Use the panel and recent files features for quick access.
4. Move, copy, or delete files as needed. Press a digit, watch the chaos shrink.
5. Enjoy fast and efficient media sorting, and maybe finally reclaim that disk space.

### HOW TO guides

Short, practical walkthroughs on the site - one per task (English and Russian):

| Guide | What it covers |
| --- | --- |
| [Viewer](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-viewer.html) | Review and sort a media folder without losing your flow |
| [Keys](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-keys.html) | The whole key map, zoom, full screen and the slideshow |
| [Setup](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-setup.html) | Default viewer, interface language, and what happens to a file |
| [OCR](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-ocr.html) | Recognize text on a picture and read it in your language |
| [Editor](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-editor.html) | Draw on a photo, crop it, and write the result back |
| [Archives](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-archives.html) | Page through a ZIP or CBZ as if it were a folder |
| [Resources](https://serzhyale.github.io/FastMediaSorter_Lite/docs/how-to-resources.html) | Open an SFTP server or a network share that needs a login like a folder |
| [Android and SFTP](https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html) | Publish selected PC folders to your Android device: the first run, the router and the internet path, step by step |

## Two programs, one app

The package ships **two viewers side by side in the same folder**, and the installer
sets up both. You never have to choose: the shortcut and file associations
automatically point at the one that actually runs on your machine.

- **`FastMediaSorter_LITE.exe`** - the main program, now 64-bit and built on
  .NET 10. It replaces the previous version in place and keeps all your settings.
  **Nothing to install** - the .NET runtime lives inside the program itself, so the
  old ".NET Framework 4.8 required" line is gone for good. Needs Windows 10 version
  1607 or newer, Windows 11, or Windows Server 2016+.
- **`FastMediaSorter_x86.exe`** - the same sorter as a 32-bit program (~3 MB)
  for Windows 7/8.1 and 32-bit Windows, where the modern runtime simply cannot run.
  It needs .NET Framework 4.8, which those Windows versions either already have or
  can get. Sorting, browsing, OCR and translation are all still in there; what it
  does not have is Android folder sharing (see the table below) and the extras the
  modern runtime made possible - zoom on the grey keys, the video control bar,
  audio/subtitle track picking, "Open URL..", the picture and video menus and the
  perspective halo.

They are **one application**, not two rivals: one set of settings, one window. Start
one while the other is open and it just brings the open window forward with your
file - no second window, no squabbling over whose settings get saved. Both also
share the same adjacent libraries (codecs, OCR/translation models), each
automatically picking the ones matching its own bitness.

### What the 64-bit program adds

- **Animated WEBP now opens everywhere.** The app decodes WEBP itself instead of
  depending on the Windows "WebP Image Extensions" codec, which server editions of
  Windows don't ship - animated WEBP used to simply fail there.
- **HEIC, HEIF and AVIF open too** - the formats iPhones save by default and the web
  increasingly serves. Decoded by the app itself, so no Store extensions and no paid
  "HEVC Video Extensions" codec are needed; sideways phone shots come up the right
  way round.
- **Video always plays through the built-in VLC engine.** The retired Internet
  Explorer component is no longer used, so playback works the same on systems where
  IE has been shown the door. (The 32-bit program keeps the classic path.)
- **A video control bar**: play/pause, seek, running time, mute and volume, along the
  bottom of the picture. It appears on mouse-over and slips away a couple of seconds
  later, and stays put while the video is paused. (The 32-bit program's video runs
  through the IE component, which draws its own controls.)
- **Zoom on the grey keys**: grey `+`/`-` zoom at the mouse cursor, grey `/` fits,
  grey `*` is actual size. Drag to pan. The wheel still flips through files unless
  you switch it to zoom in Settings.
- **Audio track and subtitle picking**: `A` cycles audio, `V` subtitles, and a
  "Tracks" button appears only when the file offers a choice. Your pick is remembered
  by language, so the next episode comes up the same way.
- **"Open URL.."** - play a video straight off `smb://`, `sftp://`, `ftp://`,
  `http(s)://`, `nfs://` or `rtsp://` without downloading it first.
- **Menus on the media**: middle-click a picture for everything you can do to it,
  right-click a video for everything you can do to that. Click a video to
  pause/resume.
- **A halo instead of bars** - "Dynamic perspective", a sub-option of the perspective
  background, fades the bars into the background the further they get from the photo.
  Its own sub-option grows that halo out of the photo's edge on each new picture, at
  one of three speeds (slow, medium - the default - or fast).
- **The Recycle Bin, and an undo that means it.** `DEL` puts the file in the bin,
  `Shift+DEL` deletes past it, and where Windows keeps no bin - a network share, a
  memory stick, a drive with the bin switched off - the question says which of those
  it is rather than a flat "are you sure". `U` steps back through the last 50
  operations: moves, copies, `F6` renames and deletions, the last of these taken
  back out of the bin, into the folder and onto the screen. The question before a
  deletion is its own three-value setting - always, only when the file will not go
  to the bin, or never. (The 32-bit program deletes finally, as it always did.)
- **Archives browse like folders**: a `.zip`, `.cbz`, `.7z`, `.rar` or `.cbr` opens as an ordinary file
  list, one picture extracted at a time into a private folder that is swept away
  when you leave - or on the next archive, if the app was killed. The archive is
  only ever read, so `DEL` and the destination folders say so instead of acting.
  (A solid 7z or RAR is read in one pass with a progress figure - Esc cancels it - and then flips instantly.)
- **A server opens like a folder.** An SFTP server - a NAS, a PC sharing folders through the Share Manager,
  anything that speaks SFTP - or a network share that wants a login is added once (by hand, or by importing the
  `.fmscfg` file or code text the Share Manager makes) and then opens from the folder box's "Resources" menu or
  from a typed `sftp://user@host:port/folder` address. Pictures, video, audio and documents are fetched as you page;
  `RE` asks the server again; `DEL`, `0`..`9`, `Shift`+digit, `F6` and `U` work on the server's own files through a
  transfer that is proven by content before the original is touched. The server's SSH key is pinned - a file from
  the Share Manager brings it, a server added by hand shows its fingerprint and asks once (default: No), and a
  changed key stops the connection and shows both fingerprints. The password is kept only encrypted for your
  Windows account. Not yet: a server as a `0`..`9` destination, QR-image import, and folders the Share Manager
  locked with a PIN (the whole file is refused). 64-bit program only.
- **An editing window that writes to disk.** Brush, rectangle and ellipse, outlined
  or filled, eight colour swatches beside the full picker, thickness in the
  picture's own pixels, `Shift` for a square or circle, `Ctrl+Z`. Cropping is a live
  frame with eight handles - everything outside it dims, and nothing is cut until
  `Enter`. **Save** writes over the original through a temporary file, so a share
  that blinks cannot leave half a photo; **Save as..** offers PNG, JPEG, BMP, TIFF
  and WebP. EXIF is carried across and the stale embedded thumbnail dropped. Adding
  text is the one part not there yet.

## Requirements

Short version: the installer works this out for you (see above). Long version:

| | `FastMediaSorter_LITE.exe` (main, 64-bit) | `FastMediaSorter_x86.exe` (32-bit) |
|---|---|---|
| **Windows** | 10 version 1607+, 11, Server 2016+ | 7, 8.1, 10, 11 - including 32-bit |
| **Runtime** | none - it is inside the program | .NET Framework 4.8 |
| **Sorting, browsing, slideshow** | yes | yes |
| **OCR + on-image translation** | yes | yes |
| **Recipients panel** | yes | yes |
| **Video** | LibVLC, with a control bar | IE component (H.264) + LibVLC fallback |
| **HEIC / HEIF / AVIF** | yes | no - Windows 7/8.1 have no codecs for them |
| **Recycle Bin + 50-step undo** | yes | no - `DEL` is final and undo is one operation deep |
| **ZIP / CBZ / 7z / RAR / CBR as a folder** | yes | no - "Unsupported format", as before |
| **Image editing (draw, crop, save)** | yes | no |
| **Zoom keys, track picking, "Open URL..", media menus, perspective halo** | yes | no |
| **Android folder sharing** | yes | no - the Share Manager is itself a 64-bit program, so it cannot start on the Windows versions this build is for |

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

### Windows will warn you, and here is why

These builds are not code-signed, so SmartScreen shows **"Windows protected your PC"**
the first time you run one. Nothing is wrong: a signing certificate costs several
hundred dollars a year and the program is free, so one has not been bought. Click
**More info**, then **Run anyway** - and do not switch SmartScreen off, which is never
needed here.

[What the warning is, why it appears, exactly what to click, and what the program never
does](https://serzhyale.github.io/FastMediaSorter_Lite/trust.html) - including the two
optional features that ask for administrator rights and the single action that takes
them back. The Microsoft Store build is signed by Microsoft at certification and shows
none of this.

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
  to share folders to your phone. Being a 64-bit .NET 10 app itself, it is what
  makes folder sharing a 64-bit-only feature, and setup skips it on Windows 7/8.1.

The bundled libraries are the 64-bit ones, for the main program. The 32-bit program
downloads the 32-bit codec and OCR pieces it needs on first use.

The interactive installer exposes these as **selectable components** and shows each
one's size: uncheck what you don't need and those parts download later on demand,
or the feature is simply left out. The **Compact** type is the viewer without folder sharing -
a fraction of the full package. (Silent/winget installs always take the full set.)

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

**[Universal Agent Kit](https://serzhyale.github.io/universal-agent-kit/)**

## Author
- Website: [sza.od.ua](https://sza.od.ua)
- Email: [sza@ukr.net](mailto:sza@ukr.net)
- Something misbehaves? **Settings -> About -> "Send the logs to the author"** packs the program logs
  into an archive and opens a mail with it already attached. It shows you what is in the archive first,
  and nothing is sent anywhere until you press Send in your own mail program.

## License
MIT - see [LICENSE](LICENSE).

**Sharing settings (builds with separate settings windows):** use **SFTP sharing settings** for its port, LAN policy and Hosting page. Local exchange-server administration opens in its own settings window; remote-account sign-in, publishing and schedules stay in **Manager settings** and do not require a local exchange server. Links in the lower left switch between installed components without closing either editor. [Settings guide](docs/guides/SHARING_SETTINGS_WINDOWS.md) · [Offline help](docs/help/en/share-settings.html). Earlier builds keep these pages in Manager settings.
