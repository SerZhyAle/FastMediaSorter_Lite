FastMediaSorter
==============

Overview
--------
FastMediaSorter is a Windows Forms application designed for quick sorting and management of media files (images, videos, and web formats). It allows users to browse, view, move, copy, delete, rename, and organize media files into destination folders using keyboard shortcuts or a graphical interface. The application supports slideshows, random file selection, and advanced sorting options.

Features
--------
- **Media Support**: Handles images (.jpg, .png, .gif, .bmp, etc.), videos (.mp4, .avi, .mkv, etc.), and web formats (.webp, .heic, .avif, .svg, requires WebView2).
- **File Operations**: Move, copy, delete, or rename files with keyboard shortcuts (1-9, 0) or buttons.
- **Navigation**: Browse files using arrows, PgUp/PgDn, or mouse scroll; jump +10/-10 or +100/-100 files.
- **Slideshow**: Sequential or random slideshow with configurable intervals.
- **Sorting**: Sort files by name (ascending/descending), size, modification time, or randomly.
- **Undo**: Revert the last move or copy operation.
- **Rotation**: Rotate images (90° clockwise or counterclockwise).
- **Destination Table**: Assign destination folders to keys (1-9, 0) for quick sorting.
- **Multilingual**: English and Russian interface support.

Requirements
------------
- **OS**: Windows 7 or later.
- **.NET Framework**: Version 4.0 or higher.
- **WebView2 Runtime**: Required for .webp, .heic, .avif, and .svg support (optional, auto-detected).
- **Disk Space**: Minimal (~1 MB for the executable).
- **Hardware**: Standard PC capable of running Windows Forms applications.

Installation
------------
1. Clone or download the repository from https://github.com/SerZhyAle/FastMediaSorter.
2. Open the solution (`FastMediaSorter.sln`) in Visual Studio.
3. Build the project in Release mode.
4. (Optional) Install Microsoft Edge WebView2 Runtime for web format support: https://developer.microsoft.com/en-us/microsoft-edge/webview2/.
5. Run `FastMediaSorter.exe` from the build output folder (e.g., `bin/Release`).

Usage
-----
1. **Launch**: Run `FastMediaSorter.exe` or open a media file/folder via command line (e.g., `FastMediaSorter.exe "C:\path\to\image.jpg"`).
2. **Select Source Folder**: Use the "Select Folder" button or enter a path to load media files.
3. **Configure Destinations**: Open the "Dest Folders Table" (F2) and assign folders to keys 1-9, 0.
4. **Navigate Files**:
   - Next/Previous: PgDn/PgUp, N/P, Right/Left arrows, or mouse scroll.
   - Jump: Up/Down (+10/-10), Shift+PgDn/PgUp (+100/-100), Home/End (first/last).
   - Random: Y (single), S (random slideshow), I (sequential slideshow).
5. **Sort Files**:
   - Move/Copy: Press 1-9, 0 to move/copy to assigned folders.
   - Delete: Del or D key.
   - Rename: F6.
   - Undo: U key.
6. **Rotate Images**: R (90° clockwise), T (90° counterclockwise).
7. **Sorting Options**: Use the sort dropdown (abc, xyz, rnd, >size, <size, >time, <time).
8. **Toggle Language**: Switch between English/Russian with the "EN/RU" button.

Command Line
------------
- Open a folder: `FastMediaSorter.exe "C:\path\to\folder"`
- Open a file: `FastMediaSorter.exe "C:\path\to\image.jpg"`

Known Issues
------------
- WebView2 Runtime absence disables .webp, .heic, .avif, .svg support (warning displayed).
- Large folders may cause delays during initial file loading.

Contributing
------------
Contributions are welcome! Fork the repository, make changes, and submit a pull request. Report issues or suggest features via GitHub Issues.

License
-------
MIT License. See LICENSE file for details.

Contact
-------
Author: SerZhyAle@ukr.net
GitHub: https://github.com/SerZhyAle