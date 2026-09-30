# DupliFoto 2026

Trova e gestisce le foto doppie su Windows. Riconosce le copie identiche, le stesse immagini ricompresse o ridimensionate (per esempio passate da WhatsApp) e gli scatti multipli della stessa scena. Se il PC ha una NPU o una GPU, le usa per la parte di riconoscimento neurale.

Si usa con una finestra (`DupliFoto2026.exe`) oppure dalla riga di comando (`duplifoto.exe`). Entrambi usano lo stesso motore e le stesse regole di sicurezza.

## Requisiti

- Windows 10 (versione 1809 o successiva, quindi anche la 22H2) oppure Windows 11, x64 o ARM64.
- La selezione automatica di NPU e GPU richiede Windows 11 24H2 o successivo. Su Windows 10 la rete neurale gira sulla CPU, altrimenti restano gli algoritmi classici.
- Per usarlo: niente. La cartella pubblicata contiene già il runtime .NET e Windows App SDK.
- Per compilarlo: [.NET 10 SDK](https://dotnet.microsoft.com/download), oppure Visual Studio 2026.

## Eseguibile pronto

Ogni push compila e prova il programma su Windows con GitHub Actions (`.github/workflows/build.yml`). L'eseguibile si scarica dalla pagina dell'esecuzione, sezione **Artifacts**: `DupliFoto-win-x64` oppure `DupliFoto-win-arm64` (PC Copilot+ con Snapdragon). Si estrae lo zip in una cartella qualsiasi e si avvia `DupliFoto2026.exe`. Nella stessa cartella c'è anche `duplifoto.exe` per la riga di comando.

## Compilazione

```powershell
dotnet build DupliFoto.slnx -c Release
dotnet test  DupliFoto.slnx

# cartella autonoma con i due programmi (x64; per i PC Copilot+ con Snapdragon usa win-arm64)
dotnet publish src/DupliFoto.Cli -c Release -r win-x64 -o publish
dotnet publish src/DupliFoto.Gui -c Release -f net10.0-windows10.0.26100.0 -r win-x64 -o publish
```

La soluzione compila anche da Linux o macOS, per esempio negli ambienti di sviluppo nel cloud. `Directory.Build.props` abilita la compilazione per Windows, salta i file PRI e sostituisce `mt.exe` con `tools/mt-linux.sh`. L'eseguibile da distribuire resta quello compilato su Windows.

L'interfaccia grafica ha anche un target `net10.0` senza la parte Windows. Serve per provarla e fotografarla da Linux: i test in `tests/DupliFoto.Gui.Tests` la disegnano senza schermo e, con la variabile `DUPLIFOTO_SCREENSHOTS`, salvano le immagini della finestra.

## Uso della finestra

1. **Cartelle.** In alto si aggiungono una o più cartelle, con il pulsante oppure trascinandole da Esplora risorse. La stella segna una cartella come preferita: tra due doppioni si tiene la copia che sta lì.
2. **Modalità.** Si sceglie la modalità (vedi la tabella più sotto), dove mettere i doppioni (quarantena o Cestino), e si preme **Avvia ricerca**.
3. **Confronto.** Al centro compaiono le due foto affiancate: a sinistra quella da tenere, a destra il doppione, con l'affidabilità in mezzo.
   - **Sposta il doppione** mette in quarantena la foto a destra.
   - **Tieni entrambe** lascia tutto com'è.
   - **Scambia** fa diventare "da tenere" la foto a destra.
   - Si passa da sola alla coppia successiva da decidere.
4. **Elenco.** Sotto ci sono i contatori e l'elenco di tutte le coppie, filtrabile. **Annulla spostamenti** rimette a posto tutto ciò che è stato spostato nella sessione; **Report** salva il report HTML e il CSV.

In semi-automatica e in automatica, finita la ricerca, la finestra propone di spostare subito i doppioni che la modalità può gestire da sola. Tutti gli altri si decidono uno per uno. Le anteprime passano da Magick.NET, quindi si vedono anche i file HEIC e RAW, già raddrizzati.

## Uso dalla riga di comando

```powershell
duplifoto "D:\Foto"                                  # sola lettura: solo report
duplifoto "D:\Foto" --modo assistita                 # chiede tutto
duplifoto "D:\Foto" "E:\Telefono" --modo semi-auto --preferisci "D:\Foto"
duplifoto "D:\Foto" --modo auto --soglia 98 --modello dinov2-small.onnx --non-interattivo
duplifoto annulla "...\DupliFoto-Quarantena\registro-20260930-101500.jsonl"
duplifoto hardware                                   # NPU/GPU/CPU disponibili
```

Si può anche trascinare una cartella sull'icona di `duplifoto.exe`: parte un'analisi in sola lettura, il report finisce in Documenti\DupliFoto e si apre nel browser. La finestra resta aperta finché non si preme Invio.

Ogni analisi produce un report HTML con le anteprime e un CSV da aprire in Excel. Il browser non mostra le anteprime dei file HEIC e RAW, ma i percorsi restano cliccabili.

## Come funziona: l'imbuto

| Livello | Cosa trova | Come | Affidabilità |
|---|---|---|---|
| 0 | inventario | nome, peso, data (costo quasi nullo) | — |
| 1 | file identici | stesso peso, poi hash parziale (64 KB iniziali e finali), poi xxHash128 completo, poi confronto byte per byte prima di agire | 100% |
| 2 | stessi pixel, metadati diversi | hash dei pixel decodificati, calcolato solo sui candidati | 99% |
| 3 | stessa immagine ricompressa, ridimensionata o ruotata | pHash DCT nelle 8 orientazioni, dHash, BK-tree | 90–98% |
| 4 | scatti multipli | data EXIF vicina e stessa fotocamera, somiglianza visiva (o neurale con `--modello`) | 60–89% |

Alcune regole valgono sempre. Una coppia RAW+JPEG dello stesso scatto non è mai un doppione. Le versioni "modificate" (`-edited`, `(modificata)`) sono limitate all'80%. Due foto con date di scatto diverse non vengono mai considerate "stessa immagine", ma al massimo scatti multipli.

## Modalità

| | 100% | 99% | 90–98% | 60–89% |
|---|---|---|---|---|
| sola-lettura | report | report | report | report |
| assistita | chiede | chiede | chiede | chiede |
| semi-auto | **automatico** | chiede | chiede | chiede |
| auto (`--soglia`, minimo 90) | **automatico** | **automatico** | sopra la soglia | chiede sempre |

Con `--non-interattivo`, ciò che richiederebbe una conferma non viene toccato e compare nel report come "da rivedere".

## Sicurezza

Queste regole valgono in tutte le modalità:

- **Nessuna cancellazione.** I file vengono solo spostati in quarantena (predefinita) o nel Cestino.
- **Registro JSON Lines.** Viene scritto dopo ogni singolo spostamento, e `duplifoto annulla` ripristina tutto.
- **File cambiati dopo la scansione.** Se un file è cambiato (peso o data), non viene toccato.
- **Copia da tenere mancante.** Se la copia da tenere non esiste più, il gruppo viene saltato.
- **Verifica finale.** I file "identici" vengono riconfrontati byte per byte subito prima dello spostamento.

## Quale copia tenere

- **Doppioni:** vince la cartella preferita, poi la risoluzione più alta, poi i metadati più completi, poi il nome senza "(1)" o "Copia", poi la copia più vecchia.
- **Scatti multipli:** vince lo scatto più nitido (varianza del Laplaciano), poi la risoluzione.

## Accelerazione NPU/GPU

La rete neurale serve solo per il livello 4, ed è facoltativa. Senza `--modello` il programma funziona interamente con algoritmi classici.

- **Il modello.** Con `tools/export_dinov2.py` si genera `dinov2-small.onnx`. Per provare solo la catena Windows ML, `tools/crea_modello_prova.py` crea un modello minuscolo che restituisce il colore medio della foto.
- **Execution provider.** Windows ML scarica tramite Windows Update i provider certificati per l'hardware presente: Qualcomm QNN, Intel OpenVINO, AMD VitisAI/MIGraphX, NVIDIA TensorRT-RTX. Include inoltre DirectML per qualunque GPU DirectX 12.
- **Scelta del dispositivo.** `--acceleratore auto|npu|gpu|cpu` indica la preferenza. Se un dispositivo non è disponibile si ricade sul successivo, senza errori.
- **Solo dove serve.** Gli embedding vengono calcolati solo sulle foto candidate, non sull'intero archivio.

Il resto del lavoro, cioè lettura dei file e decodifica, va in parallelo su tutti i core della CPU. È il collo di bottiglia reale, insieme al disco. Una seconda scansione è molto più veloce grazie alla cache in `%LOCALAPPDATA%\DupliFoto`.

## Struttura

```
src/DupliFoto.Core    motore (net10.0): scansione, hash, punteggi, azioni, report
src/DupliFoto.Accel   Windows ML / ONNX Runtime: embedding su NPU/GPU/CPU
src/DupliFoto.Cli     riga di comando (duplifoto.exe)
src/DupliFoto.Gui     interfaccia grafica (DupliFoto2026.exe), Avalonia
tests/                test xUnit del motore e della finestra, con immagini generate al momento
tools/                modelli ONNX (DINOv2, modello di prova), prova su Windows, sostituto di mt.exe per Linux
.github/workflows/     build, test e prova dell'exe su Windows
```

## Stato della verifica

Compilato con i pacchetti NuGet reali: Magick.NET 14.17, MetadataExtractor 2.9, System.IO.Hashing 10 e Windows ML (Microsoft.WindowsAppSDK.ML 1.8). Contro le API vere è servita una sola correzione, in `ExifMetadataReader.cs`.

I test del motore (46) e della finestra (7) passano su Windows e su Linux. Tra questi, i test d'integrazione usano Magick.NET e MetadataExtractor veri su JPEG e PNG generati al momento: EXIF con sottosecondi e GPS, orientamento EXIF, scala di grigi, trasparenza, e l'intero imbuto su file reali.

A ogni push, GitHub Actions prova anche `duplifoto.exe` pubblicato, su Windows:

- **Comandi di base.** `aiuto` e `hardware` funzionano; sul runner vengono rilevati CPU e GPU tramite DirectML.
- **Sola lettura.** Su foto JPEG e PNG trova la copia identica e la versione "WhatsApp" ricompressa, e non tocca nessun file.
- **Semi-automatica e annulla.** Sposta in quarantena solo la copia identica; `annulla` la rimette al suo posto.
- **Windows ML.** Registra i provider certificati e calcola gli embedding con un modello ONNX di prova sulla CPU.
- **Interfaccia grafica.** `DupliFoto2026.exe` si apre e resta aperto. Lo screenshot finisce tra gli artifact.
- **Base Windows 10.** Le stesse prove, senza Windows ML, girano anche su Windows Server 2022. È costruito sulla base di Windows 10 21H2: niente Mica, build precedente a Windows 11. GitHub non offre macchine con Windows 10 vero e proprio.

I test della finestra ripetono il flusso completo su JPEG e PNG veri: ricerca, sposta, scambia, tieni entrambe, annulla, semi-automatica e sola lettura.

Restano da provare su un PC vero: le NPU e le GPU dedicate, i file HEIC e RAW, archivi grandi. L'eseguibile ARM64 viene compilato ma non provato, perché il runner è x64.

## Prossimi passi

1. **Interfaccia.** Zoom sincronizzato sulle due foto, scorciatoie da tastiera, icona del programma, griglia di anteprime per i gruppi con molte foto.
2. **Occhi aperti e sorrisi** per scegliere il miglior scatto di gruppo (rilevamento volti via ONNX).
3. **ID di raffica nativi** (BurstUUID di Apple, Samsung) e coppie Live Photo (HEIC+MOV).
4. **Lettura della MFT NTFS** per l'inventario istantaneo di dischi con milioni di file.
5. **Pre-compilazione del modello per la NPU,** per un avvio a freddo più rapido.
