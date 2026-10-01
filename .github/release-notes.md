{{CHANGES}}

## Download

| File | For |
|---|---|
| **`DupliFoto-{{VERSION}}-x64.exe`** | **The app, for almost every PC** (Intel or AMD). |
| `DupliFoto-{{VERSION}}-arm64.exe` | The app for Windows on ARM (Snapdragon / Copilot+ PCs). |
| `duplifoto-cli-{{VERSION}}-x64.exe` | The command-line tool, x64. |
| `duplifoto-cli-{{VERSION}}-arm64.exe` | The command-line tool, ARM64. |
| `DupliFoto-{{VERSION}}-licenses.zip` | License texts of the bundled third-party components. |
| `SHA256SUMS.txt` | SHA-256 checksums of all the files above. |

DupliFoto is **portable**: download the `.exe` and run it. There is nothing to install, since .NET and every library are included.

- **First start.** The first start of a new version takes a few seconds while the program unpacks itself into `%TEMP%\.net`; copies of older versions are removed automatically.
- **Leaving no trace.** `Pulisci DupliFoto.bat`, in `%LOCALAPPDATA%\DupliFoto`, removes every working file DupliFoto has created, for all versions. For a portable copy, put an empty `DupliFoto.portable` file next to the `.exe`.
- **SmartScreen.** The executables are not code-signed yet, so Windows may show *"Windows protected your PC"*. Click **More info**, then **Run anyway**.
- **Checking a download (optional).** The output of `Get-FileHash .\DupliFoto-{{VERSION}}-x64.exe` must match the line in `SHA256SUMS.txt`.

## Requirements

- Windows 10 version 1809 or later, or Windows 11, on x64 or ARM64.
- The optional neural model uses the NPU or GPU automatically on Windows 11 24H2 or later.

DupliFoto never deletes files. Duplicates are moved to a quarantine folder, which can be restored with one click, or to the Recycle Bin.

[Documentation](https://github.com/St3f4n1n0/DupliFoto#readme) · [Changelog](https://github.com/St3f4n1n0/DupliFoto/blob/main/CHANGELOG.md) · [Third-party notices](https://github.com/St3f4n1n0/DupliFoto/blob/main/THIRD-PARTY-NOTICES.md)
