# Changelog

All notable changes to DupliFoto are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-10-03

### Added

- **English.** The app follows the language of Windows: Italian on an Italian Windows, English everywhere else. **More options → Language** switches at once, without restarting, and the choice is saved. Messages, reasons, reports and the clean-up script follow the language too. The command-line tool stays in Italian.
- **CPU, GPU and NPU at a glance.** Three dots at the bottom right of the window: green where DupliFoto is working, yellow where it could work, red where the device is missing or has no compatible driver or Windows ML component. Hovering over a dot shows the device, the component and how many photos the neural model examined in the last search. At start nothing is downloaded: DupliFoto only looks at what is installed. `duplifoto-cli hardware` shows the same information.

### Fixed

- **The neural model now really runs on the NPU.** The model had a variable batch size, and NPUs such as Intel AI Boost only accept fixed shapes: ONNX Runtime quietly ran the whole model on the CPU. On the NPU the model now gets fixed shapes and runs one photo at a time. DupliFoto also picks the device itself, so the report and the dots say where the model actually ran. If a device refuses the model, DupliFoto says why and moves on to the next one.

- **Number boxes and check boxes twice as tall.** Since 0.3.1 a style meant for the main layout also hit the check boxes, which have an inner part with the same name: the row of options grew to twice its height, and the threshold box was stretched with its number stuck at the top. Both are back to the height of the other controls.

- **A device that crashes is not tried again.** An error inside a graphics or NPU driver closes the program at once, with nothing to catch. DupliFoto now notes each device before trying it and, if the program closed, skips that device from then on and says so (`rete-neurale-esclusi.txt` in `DupliFoto-dati` lists them; delete it to try again). Each device also runs one blank picture before it is used, and Windows' software display adapter (virtual machines without a graphics card) no longer counts as a GPU.

### Changed

- **Progress while hashing.** The full hashes, the longest phase on large archives, now show how many files are done out of how many, and how much data has been read: "Hash completi: 1.234 di 12.959 file (8,1 GB di 85 GB)". The visual analysis, the pixel comparison and the neural network show the same kind of count.
- **Automatic** now means NPU, then GPU, then CPU. It used to leave the choice to Windows ML, which usually picked the GPU.
- **A more compact top panel.** *Add folder* is a discreet grey button right after the folders, and the row of options is lower: more room for the photos, especially on small screens.

## [0.3.1] - 2026-10-01

### Changed

- **Everything next to the `.exe`.** Settings, analysis cache, error log, reports and `Pulisci DupliFoto.bat` now always go to a `DupliFoto-dati` folder next to the `.exe`, wherever it runs from: a USB stick, the Desktop, a tools folder. The `DupliFoto.portable` file is no longer needed, and an existing one is ignored. Only when that folder cannot be written (a CD, or a protected folder such as Program Files) do the files go to `%LOCALAPPDATA%\DupliFoto`, and the reports to `Documents\DupliFoto`.
- **Files of earlier versions move along.** At start, the settings, cache and error log that earlier versions left in `%LOCALAPPDATA%\DupliFoto` and `%APPDATA%\DupliFoto` move into `DupliFoto-dati`, and those folders are removed. Only files that DupliFoto itself wrote are touched.
- **Nothing left in `%TEMP%`.** When the app closes, it removes the copy of itself that the `.exe` unpacked into `%TEMP%\.net` at start. The next start unpacks again and takes a few seconds longer; *Altre opzioni → File di DupliFoto* can keep the copy for quicker starts. The command-line tool does the same when it is started with a double-click. From a terminal or a script it keeps its copy, so that consecutive commands start at once.
- `Pulisci DupliFoto.bat` leaves the `Report` folder alone.

### Fixed

- **Small screens.** On a 1366×768 screen, or with 125% scaling, the middle of the comparison ran over the buttons and the counters, and the photos were tiny. Below about 860 points of usable height the window now switches to a compact layout. When even that does not fit, the window scrolls instead of stacking parts on top of each other. *Sposta automatici*, *Annulla spostamenti* and *Report* moved above the list of pairs.

## [0.3.0] - 2026-10-01

### Added

- **`Pulisci DupliFoto.bat`** in the folder of the working files removes them from the PC, for all versions (also those of 0.2 and earlier), together with the unpacked copies of the program. It never touches the quarantine, the reports or the `.exe` files. **Altre opzioni → File di DupliFoto** shows the folder.
- **Portable version.** With an empty `DupliFoto.portable` file next to the `.exe`, settings, cache and error log go to a `DupliFoto-dati` folder next to the `.exe`.

### Changed

- **Working files in one folder.** The app settings moved from `%APPDATA%\DupliFoto` to `%LOCALAPPDATA%\DupliFoto`, next to the cache and the error log; existing settings are carried over and the old folder is removed.
- **No more leftovers in `%TEMP%`.** At every start, DupliFoto removes the unpacked copies of earlier versions (about 130 MB each). Copies of a version that is still open are left alone.

## [0.2.0] - 2026-10-01

### Added

- **Choose the folder whose copies to keep.** With two or more folders, *Copia da tenere* lists *Scelta automatica* and each folder. When a folder is chosen, its copies always stay and only copies elsewhere are moved. This replaces the star for "preferred" folders, which was easy to miss.
- **Compare folders only against each other.** *Cerca i doppioni → solo tra cartelle diverse* (`--solo-tra-cartelle` on the command line) compares each folder only with the others, for example to find what in a download folder is already in the catalogued one. Duplicates inside the same folder are left alone.

### Changed

- **Clearer comparison.** The right-hand photo is now labelled *Da spostare* (to move) and the button reads *Sposta quella a destra* (move the right-hand one). Each photo shows the folder it comes from, and the reason the left-hand copy is kept is always visible.
- **Nested folders.** When both *Foto* and *Foto\Catalogate* are added, a photo in *Catalogate* belongs to *Catalogate*: choosing to keep *Foto* no longer claims it.
- Choosing another copy to keep from the command line re-scores the group, as the app does.

## [0.1.1] - 2026-10-01

### Fixed

- **The same file reached through two paths is no longer taken for two copies.** If the same folder was added twice by different routes (for example `Z:\Foto` and `\\NAS\Foto`, a SUBST drive, or a junction), each photo looked like an identical copy of itself, and moving "the duplicate" moved the only copy into quarantine, where *Undo* could still restore it. DupliFoto now recognises a file by its identity on disk and counts it once. As a further safeguard, after every move it checks that the copy to keep is still in place, and if it is not, it puts the file back at once.
- **See-through window on Windows 10.** Windows 10 has no Mica, and in that case the window was left fully transparent: the desktop showed through the app. The window now gets the solid background of the light or dark theme. Windows 11 keeps Mica.

### Changed

- **Lighter executables, quicker first start.** The executables no longer carry parts that Windows never uses (the Linux and macOS back ends of the user interface), nor the parts of .NET that DupliFoto does not use. On the first start of each version, the app now unpacks 162 files (134 MB) instead of 312 (220 MB), so there is about half as much for the antivirus to check.

### Added

- A start that takes longer than 8 seconds is noted in `%LOCALAPPDATA%\DupliFoto\errori.log`, with the time spent unpacking and the time spent opening the window, to help track down slow starts.

## [0.1.0] - 2026-09-30

First public release.

### Added

- **The app, `DupliFoto.exe`.** Add or drag in folders, then compare each pair side by side with its confidence score. For each pair you can move the duplicate, keep both, or swap which copy to keep. Counters, a filterable list of pairs, one-click undo, and HTML/CSV reports. Windows 11 style, with light and dark theme.
- **The command-line tool, `duplifoto-cli.exe`,** with read-only, assisted, semi-automatic and automatic modes, and an `annulla` command to undo from the journal.
- **Duplicate detection** from the cheapest check to the most expensive: byte-identical files, identical pixels with different metadata, the same picture recompressed, resized or rotated, and burst shots.
- **Formats:** JPEG, PNG, HEIC, AVIF, WebP, TIFF, JPEG XL and the common RAW formats.
- **Optional neural model** (DINOv2 in ONNX format) running on the NPU, GPU or CPU through Windows ML.
- **Safety first.**
  - Files are never deleted: they are only moved to a quarantine folder or to the Recycle Bin, and to the Recycle Bin only when Windows can really put them there.
  - Identical files are checked byte by byte before being moved.
  - An undo journal records every move.
- **Portable, self-contained executables** for Windows 10 (1809 or later) and Windows 11, on x64 and ARM64.

### Known limitations

- The user interface is in Italian only.
- Tested automatically on Windows Server 2025 and Windows Server 2022 (the Windows 10 base); not yet on physical Windows 10 PCs, dedicated NPUs, or large HEIC/RAW archives.
- The ARM64 executables are built but not run in CI, which only has x64 machines.

[0.3.0]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.3.0
[0.2.0]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.2.0
[0.1.1]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.1.1
[0.1.0]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.1.0
