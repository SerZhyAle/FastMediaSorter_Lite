# What's New / Changelog

Versions use the `YY.M.D.HHmm` format (see [BUILD_AND_RELEASE.md](docs/guides/BUILD_AND_RELEASE.md)).
The `[Unreleased]` section accumulates during regular **builds**; at **release** time it moves
into a new versioned section with a date and becomes the "What's New" text for the GitHub Release / site.

Categories: `Added`, `Changed`, `Fixed`, `Removed`.

## [Unreleased]

### Added
- **The viewer now comes as two programs in one folder**, so it runs well on both new and old machines:
  - **Fast Media Sorter (`FastMediaSorter_LITE.exe`)** - the new 64-bit build on .NET 10. It replaces the previous program in place and keeps all your settings. What it fixes right away: **animated WEBP images open everywhere** (the app decodes them itself, instead of relying on the Windows "WebP Image Extensions" codec that server editions of Windows don't have), and **video always plays through the built-in VLC engine** - the retired Internet Explorer component is gone, so playback behaves the same on systems that no longer ship it. Nothing to install: the .NET runtime is inside the program.
  - **`FastMediaSorter_x86.exe`** - the same viewer as a lean 32-bit build, for older and 32-bit versions of Windows that the new one cannot run on.
  
  Both are built from the same sources, look and behave identically, and share one set of settings and one set of adjacent libraries (codecs, OCR and translation models, folder sharing) - each automatically picks the right ones for its own bitness. They are one application: starting one while the other is open just brings the open window forward with your file, instead of opening a second window.

## [26.7.15.2200] - 2026-07-15

### Added
- **Recipients panel over the media.** A narrow floating list of your destination folders can now sit in the top-left corner over the image or video: click a row to move or copy the current file into that folder, or click the bottom row to delete it - so you can sort a whole folder one-handed with the mouse, without reaching for the keyboard. A click does exactly what the matching hotkey (DEL, 0..9) would. It is off by default; turn it on in Settings ("Files and system") with the "Show recipients table over the media file" checkbox - the same checkbox that used to keep the Settings window on top, given back its original purpose.
- **A limit on simultaneous connections** in the Share Manager ("Max simultaneous connections", default 10, adjustable from 1 to 99999). It caps how many devices can be connected to your shared folders at once and lets the server shrug off a flood of connections instead of bogging down.
- **A "Type" column** in the Share Manager folder list, showing each folder's profile (Audio library, Video library, Photo storage, Documents, All files) at a glance - the same profile the "Options.." dialog sets. A newly added folder now starts as "All files".
- **An install-time option to start sharing right away:** during an all-users installation you can tick "Turn on folder sharing and open the Share Manager right after installation" - setup adds the firewall rule and opens the Share Manager when it finishes, so you can pick a folder and start serving in one go.

### Changed
- **Folder sharing is now noticeably more robust and safer by default**, following a security review:
  - "LAN only" now genuinely keeps the share off the internet - it stops the program from opening any router port (UPnP/NAT-PMP) or advertising an external address, not just hiding it from the QR code. It is re-applied automatically when a share is restored at startup.
  - Abandoned connections are closed after a few hours of inactivity, and a connection that stalls during the handshake is dropped within seconds - both free up the server for real use. (An updated Android app reconnects on its own on the next tap, so an open folder just keeps working.)
  - Failed logins and rejected connections are written to a local security log next to the sharing data, so you can see if something is knocking.
  - Symbolic links or junctions inside a shared folder can no longer be used to reach files outside it.
- **A per-user install (without administrator rights) is now the lightweight viewer only.** The offline video codecs (VLC), the OCR and translation models, and Android folder sharing are set up only in an all-users (administrator) install - the first two are the bulk of the download, and sharing needs a firewall rule that requires administrator rights anyway. The install-mode screen now explains this choice up front, and the components that need administrator rights are shown greyed out in a per-user install. The codecs and OCR models skipped this way still download on demand later; folder sharing, however, is only added by re-running setup with administrator rights.
- **A calm note near the share switch**: while a share is running, other devices on your current network - including a public Wi-Fi - can reach this PC on the share port (protected by the password). Switch the share off when you do not need it. This is stated in the app and in the privacy policy.
- The Share Manager's phone-access panel is now labelled "Android access", and the "Share Manager.." button in Settings opens the Share Manager's own window (its management view) rather than the share-this-folder wizard.
- The Settings window no longer forces itself to stay on top of other windows.

### Fixed
- **The folder assigned to the "0" hotkey is no longer forgotten on restart**, and in the Settings grid, double-clicking a destination row now files into that exact folder instead of the neighbouring one (the last row no longer does nothing).
- Router detection no longer mistakes a chatty smart-TV or media box on the network (for example a Vestel TV) for your router when it offers to open the router page or detect the model.
- In the sharing dialogs, the access-code buttons and the per-folder options no longer overflow or force a scrollbar on displays scaled above 100%.

## [26.7.14.1801] - 2026-07-14

### Added
- **Folder sharing has moved into a separate program - "Fast Media Sorter: Share Manager"** (a tray icon). Everything about sharing - the folder list, starting/stopping a share, the QR code and the .fmscfg file, statistics, autostart - now lives there. The viewer keeps just one command, "Share this folder with a phone.." (right-click a folder row), and a "Share Manager.." button in Settings ("Files and system") - both open the Share Manager on the current folder. The companion program sits in the tray, keeps sharing after the viewer is closed, and, if autostart is enabled, comes up at logon. Upgrading from a previous version is seamless: settings and the phone pairing (host key) are preserved, and autostart migrates to the Share Manager on its own.
- **Connection statistics** in the Share Manager: the tray item "Current status.." shows whether a share is running, the port and address, the number of connections (total and since start), the time of the last connection, and how many files were served. The counters are stored only on your PC - this is not telemetry, nothing leaves the machine.
- **Mutual launch from the tray**: "Open Fast Media Sorter" from the Share Manager, and the reverse call to the Share Manager from the viewer - both go through the same single-instance mechanism (the window of the already-open program simply comes to the front).
- **Opt-in server features and a firewall rule**: sharing over SFTP with an open port is an explicit, deliberate action. You can enable it with a checkbox during installation or with a button in the Share Manager - a single UAC prompt adds a Windows Firewall rule for the worker module. Until it is enabled, the program works only as a viewer/sorter and the server does not start.
- Per-folder share parameters for the phone (.fmscfg schema v2): in the Share Manager you can set resource parameters for each folder - the name shown on the phone, the type (audio library, video library, photo storage, documents, all files), the exact set of media types, scan conditions (subfolders, hidden files), a comment, a PIN, and a slideshow interval. The PIN locks the resource on the phone and always goes into the file/QR when set - if you would rather not transmit it in the code, leave the field empty and set the PIN on the phone. The file stays in the old format (v1) until a parameter is changed, so older Android app versions keep understanding it; configured parameters require an updated app.
- If there are so many settings that the QR code no longer fits, the program honestly offers to save and send the .fmscfg file instead of scanning (previously the QR-code area simply went blank).
- A "Do not include the password in the file/QR" checkbox: the access file goes out without a password, the phone asks for it on import, and the password itself is shown in the status bar - share it with the recipient separately.
- Clicking the QR code opens it in a separate window 4x larger - handy for scanning from a distance. Close it by clicking the code, pressing Esc, or using the regular close button.
- A "Show barcode" item was added to the tray icon menu - it shows the current share's QR code large, in a separate window, so you can scan it with a phone without opening the main window.
- Android sharing, one code for everything: the QR code and the .fmscfg file carry all of the PC's addresses at once - local (Wi-Fi), IPv6, and the address from the internet - and the phone connects over whichever is reachable (preferring the fast local one). A single scan works both at home and away; no need to pick a network in advance. The "Local network only" checkbox leaves only the local address in the code.
- The IPv6 address (if the PC has one) is added to the code/file and works even behind CGNAT, where ordinary port forwarding is powerless.
- A phone on the same Wi-Fi finds the PC on its own, by the host key fingerprint (mDNS announcement) - so it reconnects even if the PC's local address changed after scanning (a new DHCP lease, a Wi-Fi reconnect).
- Honest external-access check: after opening a port (UPnP), the program checks from outside whether it actually answers, and reports the real state ("works from the internet" / "port does not answer - check forwarding on the router") instead of promising access that does not exist. A short reachability note is added to the file/code - the phone shows it if it cannot connect.
- A "Login/password" button copies the SFTP login and password to the clipboard - so you can pass them separately from the file/QR.

### Changed
- The installer (offline .exe) now shows the new name "Fast Media Sorter for Windows" on every screen, in shortcuts, and in the file properties. In "Add or remove programs" the entry deliberately stays "FastMediaSorter LITE" - so that winget/Microsoft Store and updates (winget upgrade) keep working as before.
- The viewer no longer minimizes to the tray or holds the sharing worker module - the Share Manager is fully responsible for that. Closing the viewer during an active share closes it immediately, while the Share Manager tray icon stays, and the phone keeps browsing the files.
- The share is restored when the Share Manager starts, if you have used it before - previously marked folders come up automatically, without waiting for the window to open.

### Removed
- The "Share" tab was removed from the viewer's Settings window - its functions moved to the Share Manager. The "Share" toolbar button and the global Shift+S key are also gone; the folder context-menu command and the Settings button remain.

### Fixed
- On a config rebuild, the QR code can no longer show a stale snapshot when the new configuration failed to encode.
- On displays scaled larger than 100% (for example, 175%), the share window's elements (the QR code and the buttons) no longer run off the edge - the window expands to the needed size and everything is visible.
- The local (Wi-Fi) address no longer disappears from the exported code/file: if the primary address-detection method returned nothing (an active VPN, a virtual adapter, an IPv6-only gateway), the program now reliably finds the real network adapter. Previously, in rare cases only the internet address made it into the QR/file, and a phone on the same Wi-Fi could not connect.
- A folder marked as writable (an ordinary folder or a receiving folder) is no longer shared to the phone as read-only: previously the phone showed "Move"/"Delete" while the server rejected the file deletion. Now the write permission the phone sees always matches what the SFTP server actually allows.

## [26.7.11.1610] - 2026-07-11

### Added
- A quick "Share this folder" wizard for sharing with an Android phone over the local network: a single window with a QR code and .fmscfg file save, based on the folder currently open in the program. Opened with the "Share" toolbar button, the Shift+S key, or by right-clicking a folder row. A "+ Current folder" button was added to the "Share" settings tab.
- The "Share" settings tab now has two inner tabs - "Local network" and "From the internet" - each with its own QR code and its own .fmscfg file. Local is for a phone on the same Wi-Fi (nothing to configure). Internet has the external address added right into the QR/file (a single scan works both at home and away). Each folder can be enabled/disabled individually with a checkbox; the local address (host:port) is shown with a "Copy" button, along with the host key.
- While folder sharing is on, a recognizable icon (blue arrows pointing in all directions) appears in the system tray. Menu: "Open Fast Media Sorter", "About" (the site page about publishing folders for Android), "Configure" (settings straight to the "Share" tab), "Turn off sharing", and "Exit". Double-clicking the icon opens the program window.
- Sharing can now be managed from the tray even after the window is closed: while a share is running, closing the window does not exit the program but minimizes it to the tray (the worker keeps sharing, the icon stays). You can fully exit via "Exit" in the icon menu; if you stop the share ("Turn off"), the program closes itself. When no share is running, the window closes as usual.
- The .fmscfg access file can be emailed right away - a "By email" button attaches it to a new message in the default mail client.
- Port forwarding: an "Open router" button, router-address detection, and router-model detection (via UPnP) with a "Router guide" button that opens a search for instructions specific to your model. Honestly warns about CGNAT.
- A built-in offline port-forwarding guide (RU/EN/UK, a language switch, full screen width): your ready-made values, how to sign in to the router, menu paths, and support links for popular routers (TP-Link, ASUS, Keenetic, Netgear, etc.), plus portforward.com. Also a separate site page, "How to publish your folders for Android" (three languages) - linked right inside the program.
- A file (image or video) can now be dropped straight onto the viewing area - onto the image itself, onto the video, or onto an empty screen: the file opens, the program switches to its folder, and playback begins. Works even when the program runs as administrator (for example, from a file manager with admin rights).
- OCR and translation settings moved to a dedicated "OCR & translation" tab in the Settings window (F2). Right-clicking the "Translate" button opens the settings straight to it.

### Changed
- The program was renamed to **Fast Media Sorter for Windows**: a new window title, and an updated site, README, and documentation. In the Microsoft Store and winget the app is still published as "FastMediaSorter LITE" - installation (`winget install SerZhyAle.FastMediaSorter`) and updates work as before, and settings and associations are preserved.
- The Settings window can now be reopened and brought to the front if it is already open (needed so it can be invoked both modally via F2 and non-modally from the OCR settings).
- Refreshed the interface hints and messages (RU/EN) - they are livelier and more human.
- Added cross-links to the FastMediaSorter for Android app (by language: EN/RU/UK) - in the "Share" tab and wizard UI, in the built-in guide, on the site, and in the README.

### Fixed
- Proofread the interface texts against actual behavior: the help had swapped the slideshow keys (now S - slideshow, I - random slideshow) and the up/down arrows (-10/+10), and described rotation as R/Shift+R (T rotates only while OCR translation is off). On the "Share" tab and wizard, the misleading "press Start" was removed - sharing turns on immediately once a folder is checked. In Settings, fixed the language button hint (it switches to Russian) and the OCR slider label ("Opacity" instead of "Transparency").
- Clicking the "Folder:" label copies the folder path again (as its hint promises), not the file path - an extra handler was overwriting the clipboard.
- The "Share" tab in Settings was no longer empty for an installed program: the sharing worker module (companion\fms-share-worker.exe) is now copied next to the exe during the build, so the tab's buttons are active rather than grayed out.

### Removed
- Removed the separate "OCR & translation" window and its launch button on the "Files and system" tab - everything moved to the new tab.

<!--
Template for a versioned section (at release time, copy [Unreleased] here, set the version and date,
remove empty subheadings):

## [26.6.27.1600] - 2026-06-27
### Added
- ...
### Fixed
- ...
-->
