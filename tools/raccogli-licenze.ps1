# Copia nella cartella pubblicata le licenze dei componenti inclusi (vedi THIRD-PARTY-NOTICES.md).
# Si esegue dopo "dotnet publish", sulla stessa macchina (i testi vengono dalla cache dei pacchetti NuGet).
#   powershell -File tools\raccogli-licenze.ps1 -Publish publish\DupliFoto-win-x64 -Rid win-x64
param(
    [Parameter(Mandatory = $true)] [string] $Publish,
    [Parameter(Mandatory = $true)] [string] $Rid
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$packages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$dest = Join-Path $Publish 'licenze'
New-Item -ItemType Directory -Force $dest | Out-Null

function Copy-FromPackage([string]$package, [string]$file, [string]$name) {
    $dir = Get-ChildItem (Join-Path $packages $package) -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $dir) { throw "Pacchetto non trovato nella cache NuGet: $package" }
    $source = Join-Path $dir.FullName $file
    if (-not (Test-Path $source)) { throw "File di licenza mancante: $source" }
    Copy-Item $source (Join-Path $dest $name)
}

Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $dest 'LICENSE-DupliFoto.txt')
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $dest
Copy-FromPackage "microsoft.netcore.app.runtime.$Rid" 'LICENSE.TXT' 'dotnet-LICENSE.txt'
Copy-FromPackage "microsoft.netcore.app.runtime.$Rid" 'THIRD-PARTY-NOTICES.TXT' 'dotnet-THIRD-PARTY-NOTICES.txt'
Copy-FromPackage 'microsoft.windowsappsdk.foundation' 'license.txt' 'WindowsAppSDK-license.txt'
Copy-FromPackage 'microsoft.windowsappsdk.ml' 'license.txt' 'WindowsML-license.txt'
Copy-FromPackage 'microsoft.windowsappsdk.ml' 'ThirdPartyNotices.txt' 'WindowsML-ThirdPartyNotices.txt'
Copy-FromPackage 'magick.net-q8-anycpu' 'Notice.txt' 'Magick.NET-Notice.txt'
Write-Host "Licenze copiate in $dest"
Get-ChildItem $dest | ForEach-Object { Write-Host "  $($_.Name)" }
