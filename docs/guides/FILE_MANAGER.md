# File manager mode

The modern viewer includes an integrated file manager mode in the Grid panel (F3), turning the thumbnail browsing surface into a full dual-purpose file manager for local folders, configured resources and connected exchange devices. Product names cite [PRODUCT_NAMING](PRODUCT_NAMING.md); component and channel availability cites [POSITIONING](POSITIONING.md).

File manager mode is exclusive to the modern .NET 10 viewer. The legacy .NET Framework 4.8 viewer retains its established triage and thumbnail grid behavior unchanged.

## Overview and mode selection

Opening the Grid panel (F3) starts in triage mode by default, displaying the current folder's media items. A mode dropdown at the left end of the toolbar switches between:

- **Triage mode:** The traditional media sorting surface with digit keys, recipient slot assignments and instant tagging.
- **File manager mode:** Browsing, organizing, filtering and transfer tools with an address bar, navigation history, places list and queue operations.

Switching modes, selecting items or navigating folders in file manager mode does not alter the viewer's current image or navigation list until an item is explicitly opened.

## Navigation and places

File manager mode provides independent directory browsing:

- **Address bar:** Shows the current provider-qualified path, supports direct path entry, and handles breadcrumb traversal.
- **Navigation buttons:** Back, Forward, Up, and Refresh controls maintain per-session browsing history.
- **Places list:** A tree on the left grouping available storage locations:
  - **Drives:** Local fixed, removable and mapped network drives on this PC.
  - **Resources:** Configured SFTP and SMB network shares from the application's resource registry.
  - **Devices:** When an exchange account is connected, paired devices and their shared roots appear automatically.

Opening a remote resource or device place uses existing credentials and secure session tunnels. Browsing does not start a local server or Share Manager worker.

## Content filters

Three content filter modes control file visibility:

| Filter mode | Visible contents | Behavior |
| --- | --- | --- |
| **Media only** | Images, videos, audio and stream playlists | Focuses on viewable media; excludes system junk and unsupported files |
| **Media & documents** | Media plus PDF, text, e-books and office documents | Suitable for mixed collections and reference libraries |
| **All files** | Raw unfiltered directory listing | Displays every file present in the directory |

Folders always remain visible to allow navigation. A separate toggle controls visibility of hidden and system files. Precedence flows from temporary toolbar overrides, to saved per-resource presentation preferences, to global defaults.

## Safe opening and execution guard

Opening a file from the file manager respects typed safety boundaries:

- **Media files:** Explicitly handed off to the main viewer upon selection or Enter.
- **Documents and external formats:** Launched through the default Windows shell handler or system picker. When opening a file from a remote resource or exchange device, the file is materialized into a temporary local cache with a clear notice that edits stay local and are not written back.
- **Blocked executables and scripts:** Executables, scripts, installer packages and shortcuts (`.exe`, `.bat`, `.cmd`, `.com`, `.ps1`, `.vbs`, `.js`, `.msi`, `.msp`, `.lnk`, `.url`) cannot be launched from any file manager surface. Shell activation is blocked with a security explanation.
- **Encrypted containers:** Unopened `.fd-sec` containers are treated as secure containers and handed off to the viewer's password prompt. Plaintext is never extracted or exported to disk.

## Operations on the shared engine

All file operations route through the application's single operation queue and execution engine:

- **Copy and Move:** Transfer files and directories between local folders, configured resources, and exchange devices. Cross-endpoint transfers use verified staging with SHA-256 validation before publication.
- **Rename:** Renames files and directories, including case-only renames on case-preserving file systems.
- **New folder:** Creates directories within the permissions granted by the active location.
- **Delete:** Files can be deleted to the Windows Recycle Bin or deleted permanently (via Shift+Del or settings). Readonly locations refuse deletion with explicit status.
- **Undo:** Supported local operations can be undone (`U` or Ctrl+Z), restoring the original item while refusing occupied or modified targets.
- **Collision resolution:** Configurable collision policies: Ask (interactive prompt), Auto-rename (generates unique numbered suffixes), Overwrite (replaces destination after confirmation), or Skip.

## Verified ZIP archives

File manager mode provides built-in verified archive operations:

- **Create ZIP:** Compresses selected files and folders into a standard ZIP archive. The archive is staged and verified via SHA-256 and CRC readback before publication. The optional "Delete originals after archiving" checkbox only executes after verification succeeds; a failed archive loses no original data.
- **Extract here:** Extracts ZIP, 7z and RAR archives into the active writable directory. Extraction verifies paths to prevent directory traversal (`../`) and zip bomb attacks. Original archives are preserved.

## Settings and preferences

The modern settings window includes a dedicated **File manager** page organized into three groups:

- **Display (`Основные настройки`):**
  - Default view mode (`Плитки` grid or `Список` details list)
  - Default content filter mode (`Медиафайлы` media only, `Медиа и документы` media & documents, or `Все файлы` all files)
  - Default visibility for hidden and system files
- **Opening (`Открытие файлов`):**
  - Non-media file opening action (launch with the default system program or show the Windows "Open with" dialog)
- **Places (`Места`):**
  - Visibility toggles for local drives, configured resources, and connected exchange device folders
  - Per-place sort state memory (remembers sort column and direction across sessions for up to 100 locations)

Delete confirmation (`ConfirmDelete`: Always, Permanent only, Never), Recycle Bin usage, and file collision resolution follow the viewer's shared settings on the **Files and system** page without adding a separate confirmation prompt. File manager preferences persist in `ModernViewerPreferences`, participate in profile export/import (excluding place sort history unless personal data is included), and normalize cleanly on fresh or upgraded installations.
