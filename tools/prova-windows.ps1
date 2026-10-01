# Prova i programmi pubblicati (gli exe portabili di DupliFoto e duplifoto-cli) su foto vere, su Windows.
# Usato da .github/workflows/build.yml; funziona anche a mano:
#   powershell -File tools\prova-windows.ps1 -Gui dist\DupliFoto-0.1.0-x64.exe -Cli dist\duplifoto-cli-0.1.0-x64.exe -Full
# Gli exe vengono copiati in una cartella di prova e lanciati da lì, come da una chiavetta: la cartella
# "DupliFoto-dati" nasce accanto a loro, e la cartella da cui li si prende resta com'è.
# -Pulizia prova anche il passaggio dai file delle versioni precedenti, la pulizia alla chiusura e
# «Pulisci DupliFoto.bat»: alla fine toglie da questo PC impostazioni, cache e copie scompattate di DupliFoto
# (non la quarantena né i report).
param(
    [Parameter(Mandatory = $true)] [string] $Gui,
    [Parameter(Mandatory = $true)] [string] $Cli,
    [string] $Work = $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }),
    [string] $Screenshot = '',
    # Anche semi-automatica, annulla e Windows ML (serve Python per creare il modello di prova)
    [switch] $Full,
    [switch] $Pulizia
)
$ErrorActionPreference = 'Stop'
$appDir = Join-Path $Work 'app'
Remove-Item $appDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $appDir | Out-Null
Copy-Item (Resolve-Path $Gui), (Resolve-Path $Cli) $appDir
$gui = Join-Path $appDir (Split-Path $Gui -Leaf)
$cli = Join-Path $appDir (Split-Path $Cli -Leaf)
$data = Join-Path $appDir 'DupliFoto-dati'
$oldLocal = Join-Path $env:LOCALAPPDATA 'DupliFoto'
$netBase = Join-Path ([IO.Path]::GetTempPath()) '.net'
$guiName = [IO.Path]::GetFileNameWithoutExtension($gui); $cliName = [IO.Path]::GetFileNameWithoutExtension($cli)
function Run { & $cli @args; if ($LASTEXITCODE -ne 0) { throw "duplifoto-cli $args -> codice $LASTEXITCODE" } }
function Check($ok, $msg) { if (-not $ok) { throw "FALLITO: $msg" } else { Write-Host "ok: $msg" } }
# Le copie scompattate di DupliFoto (riconosciute da DupliFoto.Core.dll) sotto %TEMP%\.net; con un nome, quelle di quell'exe.
function Get-Extracted($name) {
    Get-ChildItem $netBase -Directory -ErrorAction SilentlyContinue | Get-ChildItem -Directory |
        Where-Object { (Test-Path (Join-Path $_.FullName 'DupliFoto.Core.dll')) -and (-not $name -or $_.Parent.Name -eq $name) }
}
# Aspetta che l'app chiusa tolga la sua copia scompattata (la toglie un Prompt dei comandi nascosto, appena può).
function Wait-ExtractionGone {
    for ($s = 0; $s -lt 30 -and @(Get-Extracted $guiName).Count; $s++) { Start-Sleep -Seconds 1 }
    Check (@(Get-Extracted $guiName).Count -eq 0) "alla chiusura l'app toglie la sua copia scompattata in %TEMP%\.net (dopo $s secondi)"
}

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
Check (Test-Path (Join-Path $data 'Pulisci DupliFoto.bat')) 'i file di lavoro stanno in DupliFoto-dati accanto all''exe'
Check (-not (Test-Path $oldLocal)) 'niente in %LOCALAPPDATA%\DupliFoto'

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

    # Con -Pulizia: una copia scompattata di una versione vecchia e i file dove li tenevano la 0.3.0 (in Local)
    # e la 0.2 (le impostazioni in Roaming). L'app deve togliere la prima e portare gli altri in DupliFoto-dati.
    $legacySettings = Join-Path $env:APPDATA 'DupliFoto\gui.json'
    if ($Pulizia) {
        $oldCopy = Join-Path $netBase 'DupliFoto-0.0.1-x64\vecchia'
        New-Item -ItemType Directory -Force $oldCopy | Out-Null
        Set-Content (Join-Path $oldCopy 'DupliFoto.Core.dll') 'versione vecchia'
        New-Item -ItemType Directory -Force (Split-Path $legacySettings) | Out-Null
        Set-Content $legacySettings '{"Folders":[],"Mode":0}' -Encoding UTF8
        New-Item -ItemType Directory -Force $oldLocal | Out-Null
        Set-Content (Join-Path $oldLocal 'gui.json') '{"Folders":[],"Mode":0,"Threshold":97}' -Encoding UTF8
        Set-Content (Join-Path $oldLocal 'errori.log') 'registro della 0.3.0' -Encoding UTF8
        Set-Content (Join-Path $oldLocal 'Pulisci DupliFoto.bat') 'script della 0.3.0'
    }

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

    # Alla chiusura l'app salva le impostazioni (JSON, accanto all'exe): devono ricordare la cartella aperta.
    # E toglie la copia di sé scompattata in %TEMP%\.net.
    $app.CloseMainWindow() | Out-Null
    if (-not $app.WaitForExit(10000)) { $app.Kill() }
    $settings = Join-Path $data 'gui.json'
    Check ((Test-Path $settings) -and (Get-Item $settings).LastWriteTime -ge $launched) 'impostazioni salvate alla chiusura, in DupliFoto-dati'
    Check (@((Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json).Folders.Path) -contains $dir) 'le impostazioni ricordano la cartella'
    Wait-ExtractionGone

    # Secondo avvio: l'exe si scompatta di nuovo e parte normalmente; quanto ci mette è il prezzo della pulizia.
    $app = Start-Process $gui -PassThru
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep -Seconds 1
        $app.Refresh()
        if ($app.HasExited -or $app.MainWindowHandle -ne 0) { break }
    }
    Check ($app.MainWindowHandle -ne 0) "secondo avvio, scompattandosi di nuovo: la finestra compare dopo $i secondi"
    $app.CloseMainWindow() | Out-Null
    if (-not $app.WaitForExit(10000)) { $app.Kill() }
    Wait-ExtractionGone
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill() }
    if ($sync.Handle) { [DupliFotoProva.Win32]::PostMessage($sync.Handle, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    if ($backdropRun.AsyncWaitHandle.WaitOne(5000)) { $backdrop.Dispose(); $backdropSpace.Dispose() }
}
if ($Pulizia) {
    # 5) All'avvio: via la copia scompattata vecchia, e i file delle versioni precedenti passati in DupliFoto-dati.
    Check (-not (Test-Path (Join-Path $netBase 'DupliFoto-0.0.1-x64'))) 'la copia scompattata della versione vecchia e'' stata tolta'
    Check (-not (Test-Path $legacySettings)) 'via le impostazioni della 0.2 da %APPDATA%'
    Check (-not (Test-Path $oldLocal)) 'via i file della 0.3.0 da %LOCALAPPDATA%'
    Check ((Get-Content (Join-Path $data 'errori.log') -Raw -Encoding UTF8) -match 'registro della 0.3.0') 'il registro della 0.3.0 e'' passato in DupliFoto-dati'
    Check (@(Get-Extracted $cliName).Count -ge 1) 'la riga di comando, lanciata da uno script, tiene la sua copia scompattata'
    $bat = Join-Path $data 'Pulisci DupliFoto.bat'
    Check ((Get-Content $bat -Raw) -match 'DupliFoto.Core.dll') 'Pulisci DupliFoto.bat aggiornato in DupliFoto-dati'

    # 6) Un'altra cartella, come una chiavetta: i file di lavoro nascono accanto a quell'exe, senza fare niente.
    $stick = Join-Path $Work 'chiavetta'
    Remove-Item $stick -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stick | Out-Null
    Copy-Item $cli (Join-Path $stick 'duplifoto-cli.exe')
    & (Join-Path $stick 'duplifoto-cli.exe') $dir --non-interattivo --report "$reportDir\chiavetta.html" | Out-Null
    Check ($LASTEXITCODE -eq 0) 'da un''altra cartella: analisi riuscita'
    Check (Test-Path (Join-Path $stick 'DupliFoto-dati\cache-v1.json')) 'da un''altra cartella: la cache sta accanto a quell''exe'
    Check (Test-Path (Join-Path $stick 'DupliFoto-dati\Pulisci DupliFoto.bat')) 'da un''altra cartella: lo script di pulizia sta accanto a quell''exe'
    New-Item -ItemType Directory -Force (Join-Path $stick 'DupliFoto-dati\Report') | Out-Null
    Set-Content (Join-Path $stick 'DupliFoto-dati\Report\report.html') 'un report'

    # 7) Pulisci DupliFoto.bat (senza conferma): via copie scompattate e file di lavoro, di tutte le versioni.
    $ErrorActionPreference = 'Continue'
    cmd.exe /c "`"$bat`" /si" 2>&1 | Out-String | Write-Host
    cmd.exe /c "`"$(Join-Path $stick 'DupliFoto-dati\Pulisci DupliFoto.bat')`" /si" 2>&1 | Out-String | Write-Host
    $ErrorActionPreference = 'Stop'
    Check (@(Get-Extracted).Count -eq 0) 'pulizia: nessuna copia scompattata di DupliFoto in %TEMP%\.net'
    Check (-not (Test-Path $data)) 'pulizia: tolta la cartella dei file di lavoro'
    Check (-not (Test-Path $oldLocal)) 'pulizia: niente in %LOCALAPPDATA%\DupliFoto'
    Check (-not (Test-Path (Join-Path $env:APPDATA 'DupliFoto'))) 'pulizia: niente in %APPDATA%\DupliFoto'
    $left = @(Get-ChildItem (Join-Path $stick 'DupliFoto-dati') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object Name) -join ','
    Check ($left -eq 'report.html') "pulizia: accanto all'altro exe restano solo i report (trovato: '$left')"
    Check ((Test-Path (Join-Path $stick 'duplifoto-cli.exe')) -and (Test-Path $cli) -and (Test-Path $gui)) 'pulizia: gli exe restano'
    Check (Test-Path "$reportDir\report.html") 'pulizia: i report restano'
}
Write-Host 'Tutte le prove sono passate.'
