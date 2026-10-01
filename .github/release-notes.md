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

- **Leaving no trace.** Settings, cache, error log and reports go to a `DupliFoto-dati` folder next to the `.exe`, so on a USB stick they travel with it. At start the program unpacks itself into `%TEMP%\.net`, which takes a few seconds, and removes that copy when you close it. `Pulisci DupliFoto.bat`, in `DupliFoto-dati`, removes what earlier versions left on the PC.
- **SmartScreen.** The executables are not code-signed yet, so Windows may show *"Windows protected your PC"*. Click **More info**, then **Run anyway**.
- **Checking a download (optional).** The output of `Get-FileHash .\DupliFoto-{{VERSION}}-x64.exe` must match the line in `SHA256SUMS.txt`.

## Requirements

- Windows 10 version 1809 or later, or Windows 11, on x64 or ARM64.
- The optional neural model uses the NPU or GPU automatically on Windows 11 24H2 or later.

DupliFoto never deletes files. Duplicates are moved to a quarantine folder, which can be restored with one click, or to the Recycle Bin.

[Documentation](https://github.com/St3f4n1n0/DupliFoto#readme) · [Changelog](https://github.com/St3f4n1n0/DupliFoto/blob/main/CHANGELOG.md) · [Third-party notices](https://github.com/St3f4n1n0/DupliFoto/blob/main/THIRD-PARTY-NOTICES.md)
