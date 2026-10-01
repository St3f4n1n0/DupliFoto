# Prova i programmi pubblicati (gli exe portabili di DupliFoto e duplifoto-cli) su foto vere, su Windows.
# Usato da .github/workflows/build.yml; funziona anche a mano:
#   powershell -File tools\prova-windows.ps1 -Gui dist\DupliFoto-0.1.0-x64.exe -Cli dist\duplifoto-cli-0.1.0-x64.exe -Full
param(
    [Parameter(Mandatory = $true)] [string] $Gui,
    [Parameter(Mandatory = $true)] [string] $Cli,
    [string] $Work = $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }),
    [string] $Screenshot = '',
    # Anche semi-automatica, annulla e Windows ML (serve Python per creare il modello di prova)
    [switch] $Full
)
$ErrorActionPreference = 'Stop'
$cli = Resolve-Path $Cli
$gui = Resolve-Path $Gui
function Run { & $cli @args; if ($LASTEXITCODE -ne 0) { throw "duplifoto-cli $args -> codice $LASTEXITCODE" } }
function Check($ok, $msg) { if (-not $ok) { throw "FALLITO: $msg" } else { Write-Host "ok: $msg" } }

Write-Host "Sistema: $((Get-CimInstance Win32_OperatingSystem).Caption) build $([Environment]::OSVersion.Version.Build)"
Run aiuto
Run hardware

# Foto di prova: una scena, una copia identica, una versione "WhatsApp" rimpicciolita, un PNG, una foto diversa.
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $Work 'foto'
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$dir\WhatsApp" | Out-Null
function New-Scene([int]$seed) {
    $rnd = New-Object System.Random $seed
    $bmp = New-Object System.Drawing.Bitmap 1600, 1200
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb($rnd.Next(256), $rnd.Next(256), $rnd.Next(256)))
    for ($i = 0; $i -lt 10; $i++) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($rnd.Next(256), $rnd.Next(256), $rnd.Next(256)))
        $g.FillEllipse($brush, $rnd.Next(1400), $rnd.Next(1000), $rnd.Next(100, 800), $rnd.Next(100, 600))
    }
    $g.Dispose()
    return $bmp
}
$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
function Save-Jpeg($bmp, $path, [long]$quality) {
    $p = New-Object System.Drawing.Imaging.EncoderParameters 1
    $p.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), $quality
    $bmp.Save($path, $jpeg, $p)
}
$mare = New-Scene 1
Save-Jpeg $mare "$dir\mare.jpg" 92
Copy-Item "$dir\mare.jpg" "$dir\mare (1).jpg"
Save-Jpeg (New-Object System.Drawing.Bitmap $mare, 800, 600) "$dir\WhatsApp\IMG-20260810-WA0001.jpg" 55
$mare.Save("$dir\mare.png", [System.Drawing.Imaging.ImageFormat]::Png)
Save-Jpeg (New-Scene 2) "$dir\montagna.jpg" 92

# 1) Riga di comando, sola lettura: report HTML + CSV, nessun file toccato.
$reportDir = Join-Path $Work 'report'
New-Item -ItemType Directory -Force $reportDir | Out-Null
Run $dir --non-interattivo --no-cache --report "$reportDir\report.html"
$rows = Import-Csv "$reportDir\report.csv" -Delimiter ';' -Encoding UTF8
$rows | Format-Table gruppo, ruolo, tipo, affidabilita, percorso -AutoSize | Out-String -Width 250 | Write-Host
Check ($rows | Where-Object { $_.ruolo -eq 'doppione' -and $_.tipo -eq 'Identici al byte' -and $_.percorso -like '*\mare (1).jpg' }) 'copia identica trovata'
Check ($rows | Where-Object { $_.ruolo -eq 'doppione' -and $_.percorso -like '*\IMG-20260810-WA0001.jpg' }) 'versione WhatsApp trovata'
Check (-not ($rows | Where-Object { $_.percorso -like '*\montagna.jpg' })) 'la foto diversa non e'' un doppione'
Check ((Get-ChildItem $dir -Recurse -File).Count -eq 5) 'sola lettura: nessun file spostato'

if ($Full) {
    # 2) Semi-automatica: sposta da sola solo la copia identica; poi "annulla" la riporta al suo posto.
    $q = Join-Path $Work 'quarantena'
    Run $dir --modo semi-auto --non-interattivo --no-cache --quarantena $q --report "$reportDir\semi.html"
    Check (-not (Test-Path "$dir\mare (1).jpg")) 'semi-auto: copia identica spostata'
    Check (Test-Path "$dir\mare.jpg") 'semi-auto: originale al suo posto'
    Check (Test-Path "$dir\WhatsApp\IMG-20260810-WA0001.jpg") 'semi-auto: le copie non identiche restano'
    $journal = Get-ChildItem $q -Filter 'registro-*.jsonl' | Select-Object -First 1
    Run annulla $journal.FullName
    Check (Test-Path "$dir\mare (1).jpg") 'annulla: copia ripristinata'

    # 3) Catena Windows ML / ONNX Runtime con un modello minuscolo (colore medio) sulla CPU.
    python -m pip install --quiet onnx
    if ($LASTEXITCODE -ne 0) { throw 'pip install onnx fallito' }
    Push-Location $Work
    python (Join-Path $PSScriptRoot 'crea_modello_prova.py')
    Pop-Location
    $out = & $cli $dir --non-interattivo --no-cache --modello "$Work\modello-prova.onnx" --acceleratore cpu --report "$reportDir\modello.html" | Out-String
    Write-Host $out
    Check ($LASTEXITCODE -eq 0) 'analisi con modello: codice di uscita 0'
    Check ($out -match 'Rete neurale: Windows ML') 'modello caricato con Windows ML'
    Check ($out -match 'Embedding neurali su') 'embedding calcolati'
    Check ($out -notmatch 'Embedding non disponibili') 'nessun errore di inferenza'
}

# 4) Interfaccia grafica: si apre con la cartella passata come argomento e resta aperta.
#    Al primo avvio l'exe portabile si scompatta: si aspetta la finestra fino a un minuto.
#    Dietro l'app, in fondo a tutte le finestre, un pannello magenta a tutto schermo: se la finestra fosse
#    trasparente (com'era su Windows 10, dove Mica non c'e') il magenta si vedrebbe attraverso l'app.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace DupliFotoProva -Name Win32 -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
"@
$HWND_TOP = [IntPtr]0; $HWND_BOTTOM = [IntPtr]1; $SWP_NOSIZE_NOMOVE_NOACTIVATE = 0x13; $WM_CLOSE = 0x10

$sync = [hashtable]::Synchronized(@{})
$backdropSpace = [runspacefactory]::CreateRunspace()
$backdropSpace.ApartmentState = 'STA'
$backdropSpace.Open()
$backdropSpace.SessionStateProxy.SetVariable('sync', $sync)
$backdrop = [PowerShell]::Create()
$backdrop.Runspace = $backdropSpace
[void]$backdrop.AddScript({
    Add-Type -AssemblyName System.Windows.Forms
    $f = New-Object System.Windows.Forms.Form
    $f.BackColor = [System.Drawing.Color]::Magenta
    $f.FormBorderStyle = 'None'
    $f.WindowState = 'Maximized'
    $f.ShowInTaskbar = $false
    $f.Add_Shown({ $sync.Handle = $f.Handle })
    [System.Windows.Forms.Application]::Run($f)
})
$backdropRun = $backdrop.BeginInvoke()
try {
    for ($i = 0; $i -lt 40 -and -not $sync.Handle; $i++) { Start-Sleep -Milliseconds 250 }
    Check ([bool]$sync.Handle) 'pannello magenta dietro l''app'
    [DupliFotoProva.Win32]::SetWindowPos($sync.Handle, $HWND_BOTTOM, 0, 0, 0, 0, $SWP_NOSIZE_NOMOVE_NOACTIVATE) | Out-Null

    $launched = Get-Date
    $app = Start-Process $gui -ArgumentList "`"$dir`"" -PassThru
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        $app.Refresh()
        if ($app.HasExited -or $app.MainWindowHandle -ne 0) { break }
    }
    Check (-not $app.HasExited) "l'interfaccia grafica resta aperta (nessun errore all'avvio, codice $(if ($app.HasExited) { $app.ExitCode }))"
    Check ($app.MainWindowHandle -ne 0) "la finestra principale compare dopo $i secondi"
    Start-Sleep -Seconds 5  # il tempo di disegnare il contenuto
    Write-Host "Finestra principale: '$($app.MainWindowTitle)'"

    # L'app sopra, il magenta sotto; poi si contano i punti magenta dentro la finestra dell'app.
    # Mica mostra lo sfondo del desktop, non le finestre dietro: con Mica o con uno sfondo pieno non ce ne sono.
    [DupliFotoProva.Win32]::SetWindowPos($sync.Handle, $HWND_BOTTOM, 0, 0, 0, 0, $SWP_NOSIZE_NOMOVE_NOACTIVATE) | Out-Null
    [DupliFotoProva.Win32]::SetWindowPos($app.MainWindowHandle, $HWND_TOP, 0, 0, 0, 0, $SWP_NOSIZE_NOMOVE_NOACTIVATE) | Out-Null
    Start-Sleep -Seconds 1
    $r = New-Object DupliFotoProva.Win32+RECT
    [DupliFotoProva.Win32]::GetWindowRect($app.MainWindowHandle, [ref]$r) | Out-Null
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $left = [Math]::Max($r.Left, $b.Left); $top = [Math]::Max($r.Top, $b.Top)
    $width = [Math]::Min($r.Right, $b.Right) - $left; $height = [Math]::Min($r.Bottom, $b.Bottom) - $top
    $area = New-Object System.Drawing.Bitmap $width, $height
    [System.Drawing.Graphics]::FromImage($area).CopyFromScreen($left, $top, 0, 0, $area.Size)
    $magenta = 0; $total = 0
    for ($y = 0; $y -lt $height; $y += 8) {
        for ($x = 0; $x -lt $width; $x += 8) {
            $p = $area.GetPixel($x, $y); $total++
            if ($p.R -gt 200 -and $p.G -lt 80 -and $p.B -gt 200) { $magenta++ }
        }
    }
    $percent = [Math]::Round(100.0 * $magenta / $total, 1)
    if ($Screenshot) {
        try {
            $shot = New-Object System.Drawing.Bitmap $b.Width, $b.Height
            [System.Drawing.Graphics]::FromImage($shot).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
            New-Item -ItemType Directory -Force (Split-Path $Screenshot) | Out-Null
            $shot.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "Screenshot: $Screenshot ($($b.Width)x$($b.Height))"
        }
        catch { Write-Host "Screenshot non disponibile su questo runner: $_" }
    }
    Check ($percent -lt 2) "la finestra non e' trasparente (magenta visto attraverso l'app: $percent% dei punti)"

    # Alla chiusura l'app salva le impostazioni (JSON): devono ricordare la cartella aperta.
    $app.CloseMainWindow() | Out-Null
    if (-not $app.WaitForExit(10000)) { $app.Kill() }
    $settings = Join-Path $env:APPDATA 'DupliFoto\gui.json'
    Check ((Test-Path $settings) -and (Get-Item $settings).LastWriteTime -ge $launched) 'impostazioni salvate alla chiusura'
    Check (@((Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json).Folders.Path) -contains $dir) 'le impostazioni ricordano la cartella'
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill() }
    if ($sync.Handle) { [DupliFotoProva.Win32]::PostMessage($sync.Handle, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    if ($backdropRun.AsyncWaitHandle.WaitOne(5000)) { $backdrop.Dispose(); $backdropSpace.Dispose() }
}
Write-Host 'Tutte le prove sono passate.'
