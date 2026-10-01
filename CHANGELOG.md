# Changelog

All notable changes to DupliFoto are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

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

[0.1.1]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.1.1
[0.1.0]: https://github.com/St3f4n1n0/DupliFoto/releases/tag/v0.1.0
