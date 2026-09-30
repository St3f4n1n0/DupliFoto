# DupliFoto 2026

Trova e gestisce le foto doppie su Windows. Riconosce le copie identiche, le stesse immagini ricompresse o ridimensionate (per esempio passate da WhatsApp) e gli scatti multipli della stessa scena. Se il PC ha una NPU o una GPU, le usa per la parte di riconoscimento neurale.

Si usa con una finestra (`DupliFoto2026.exe`) oppure dalla riga di comando (`duplifoto.exe`). Entrambi usano lo stesso motore e le stesse regole di sicurezza: nessun file viene mai cancellato.

## Scaricare

L'ultima versione è nella pagina [Releases](https://github.com/St3f4n1n0/DupliFoto/releases):

- `DupliFoto-win-x64-….zip` per quasi tutti i PC;
- `DupliFoto-win-arm64-….zip` per i PC con processore Snapdragon (Copilot+).

Si estrae lo zip in una cartella qualsiasi e si avvia `DupliFoto2026.exe`. Non serve installare niente: la cartella contiene già .NET e Windows App SDK.

I programmi non sono firmati digitalmente. Al primo avvio Windows può mostrare "Windows ha protetto il PC": si sceglie "Ulteriori informazioni", poi "Esegui comunque". Il file `SHA256SUMS.txt` della release permette di verificare gli zip scaricati.

## Requisiti

- Windows 10 (versione 1809 o successiva, quindi anche la 22H2) oppure Windows 11, x64 o ARM64.
- La selezione automatica di NPU e GPU richiede Windows 11 24H2 o successivo. Su Windows 10 la rete neurale facoltativa usa i dispositivi che ONNX Runtime trova (CPU, o GPU con DirectML); se non ce n'è nessuno, restano gli algoritmi classici.

## Uso della finestra

1. **Cartelle.** In alto si aggiungono una o più cartelle, con il pulsante oppure trascinandole da Esplora risorse. La stella segna una cartella come preferita: tra due doppioni si tiene la copia che sta lì.
2. **Modalità.** Si sceglie la modalità (vedi la tabella più sotto), dove mettere i doppioni (quarantena o Cestino), e si preme **Avvia ricerca**.
3. **Confronto.** Al centro compaiono le due foto affiancate: a sinistra quella da tenere, a destra il doppione, con l'affidabilità in mezzo.
   - **Sposta il doppione** sposta la foto a destra nella quarantena o nel Cestino.
   - **Tieni entrambe** lascia tutto com'è.
   - **Scambia** fa diventare "da tenere" la foto a destra.
   - Si passa da sola alla coppia successiva da decidere.
4. **Elenco.** Sotto ci sono i contatori e l'elenco di tutte le coppie, filtrabile. **Annulla spostamenti** rimette a posto tutto ciò che è stato spostato in quarantena nella sessione; **Report** salva il report HTML e il CSV.

In semi-automatica e in automatica, finita la ricerca, la finestra propone di spostare subito i doppioni che la modalità può gestire da sola. Tutti gli altri si decidono uno per uno. La modalità si può cambiare anche dopo la ricerca, senza rifarla. Le anteprime passano da Magick.NET, quindi si vedono anche i file HEIC e RAW, già raddrizzati.

## Uso dalla riga di comando

```powershell
duplifoto "D:\Foto"                                  # sola lettura: solo report
duplifoto "D:\Foto" --modo assistita                 # chiede tutto
duplifoto "D:\Foto" "E:\Telefono" --modo semi-auto --preferisci "D:\Foto"
duplifoto "D:\Foto" --modo auto --soglia 98 --modello dinov2-small.onnx --non-interattivo
duplifoto annulla "...\DupliFoto-Quarantena\registro-20260930-101500-3fa2c1.jsonl"
duplifoto hardware                                   # NPU/GPU/CPU disponibili
duplifoto --versione
```

`duplifoto aiuto` elenca tutte le opzioni: `--azione quarantena|cestino`, `--quarantena`, `--preferisci`, `--report`, `--modello`, `--acceleratore`, `--raffica`, `--no-raffiche`, `--no-sottocartelle`, `--nascosti`, `--no-cache`, `--thread`, `--non-interattivo`.

Si può anche trascinare una cartella sull'icona di `duplifoto.exe`: parte un'analisi in sola lettura, il report finisce in Documenti\DupliFoto e si apre nel browser. La finestra resta aperta finché non si preme Invio.

Ogni analisi produce un report HTML con le anteprime e un CSV da aprire in Excel. Il browser non mostra le anteprime dei file HEIC e RAW, ma i percorsi restano cliccabili.

## Come funziona: l'imbuto

| Livello | Cosa trova | Come | Affidabilità |
|---|---|---|---|
| 0 | inventario | nome, peso, data (costo quasi nullo) | — |
| 1 | file identici | stesso peso, poi hash parziale (64 KB iniziali e finali), poi xxHash128 completo, poi confronto byte per byte prima di agire | 100% |
| 2 | stessi pixel, metadati diversi | hash dei pixel decodificati, calcolato solo sui candidati | 99% |
| 3 | stessa immagine ricompressa, ridimensionata o ruotata | pHash DCT nelle 8 orientazioni, dHash, BK-tree | 90–98% |
| 4 | scatti multipli | data EXIF vicina e stessa fotocamera, somiglianza visiva (o neurale con un modello) | 60–89% |

Alcune regole valgono sempre:

- Una coppia RAW+JPEG dello stesso scatto non è mai un doppione.
- Le versioni "modificate" (`-edited`, `(modificata)`) sono limitate all'80%.
- Due foto con date di scatto diverse non vengono mai considerate "stessa immagine", ma al massimo scatti multipli.

## Modalità

| | 100% | 99% | 90–98% | 60–89% |
|---|---|---|---|---|
| sola lettura | report | report | report | report |
| assistita | chiede | chiede | chiede | chiede |
| semi-automatica | **automatico** | chiede | chiede | chiede |
| automatica (soglia predefinita 99, minimo 90) | **automatico** | **automatico** | sopra la soglia | chiede sempre |

Gli scatti multipli si chiedono sempre, in ogni modalità. Dalla riga di comando con `--non-interattivo`, ciò che richiederebbe una conferma non viene toccato e il riepilogo finale lo conta come "da rivedere".

## Sicurezza

Queste regole valgono in tutte le modalità:

- **Nessuna cancellazione.** I file vengono solo spostati: in quarantena (predefinita) o nel Cestino.
- **Cestino solo se c'è davvero.** Sulle unità di rete o rimovibili Windows non ha un Cestino, e "eliminare" vorrebbe dire cancellare: lì il file non viene toccato. Se il Cestino è disattivato o troppo piccolo, Windows chiede conferma invece di cancellare in silenzio.
- **Registro JSON Lines.** Viene scritto dopo ogni singolo spostamento. `duplifoto annulla` e **Annulla spostamenti** rimettono a posto tutto ciò che è in quarantena; dal Cestino si ripristina con Windows.
- **File cambiati dopo la scansione.** Se un file è cambiato (peso o data), non viene toccato.
- **Copia da tenere mancante.** Se la copia da tenere non esiste più o è cambiata, i suoi doppioni non vengono toccati.
- **Verifica finale.** I file "identici" vengono riconfrontati byte per byte subito prima dello spostamento.
- **OneDrive e collegamenti.** I file "solo online" di OneDrive vengono saltati, per non scaricarli. I collegamenti simbolici e le giunzioni non vengono seguiti: niente cicli, niente foto contate due volte.

## Quale copia tenere

- **Doppioni:** vince la cartella preferita. A parità:
  1. la risoluzione più alta;
  2. i metadati più completi;
  3. il nome senza "(1)" o "Copia";
  4. l'originale rispetto alla versione modificata;
  5. la copia più vecchia;
  6. il percorso più breve.
- **Scatti multipli:** vince lo scatto più nitido (varianza del Laplaciano), poi la risoluzione.

## Accelerazione NPU/GPU

La rete neurale serve solo per il livello 4, ed è facoltativa. Senza un modello il programma funziona interamente con algoritmi classici.

- **Il modello.** Con `tools/export_dinov2.py` si genera `dinov2-small.onnx`. Nella finestra lo si sceglie in **Altre opzioni**; dalla riga di comando con `--modello`. Per provare solo la catena Windows ML, `tools/crea_modello_prova.py` crea un modello minuscolo che restituisce il colore medio della foto.
- **Execution provider.** Windows ML scarica tramite Windows Update i provider certificati per l'hardware presente: Qualcomm QNN, Intel OpenVINO, AMD VitisAI/MIGraphX, NVIDIA TensorRT-RTX. Include inoltre DirectML per qualunque GPU DirectX 12.
- **Scelta del dispositivo.** Automatico, NPU, GPU o CPU (`--acceleratore auto|npu|gpu|cpu`). Se un dispositivo non è disponibile si ricade sul successivo, senza errori.
- **Solo dove serve.** Gli embedding vengono calcolati solo sulle foto candidate, non sull'intero archivio.

Il resto del lavoro, cioè lettura dei file e decodifica, va in parallelo su tutti i core della CPU. È il collo di bottiglia reale, insieme al disco. Una seconda scansione è molto più veloce grazie alla cache.

## File e cartelle del programma

| Cosa | Dove |
|---|---|
| Quarantena (predefinita) e registri per annullare | `Immagini\DupliFoto-Quarantena` |
| Cache delle analisi | `%LOCALAPPDATA%\DupliFoto\cache-v1.json` |
| Impostazioni della finestra | `%APPDATA%\DupliFoto\gui.json` |
| Errori imprevisti della finestra | `%LOCALAPPDATA%\DupliFoto\errori.log` |
| Report (finestra, o riga di comando avviata con un doppio clic) | `Documenti\DupliFoto` |

Per disinstallare basta cancellare la cartella del programma e, se non servono più, queste cartelle.

## Privacy

DupliFoto lavora solo sul PC e non invia dati. Due casi usano la rete, e solo tramite Windows:

- la rete neurale facoltativa, se si sceglie un modello;
- il comando `hardware`.

In entrambi i casi Windows ML può scaricare da Windows Update i provider per NPU e GPU. I componenti Microsoft inclusi (Windows App SDK, Windows ML) possono inviare dati diagnostici a Microsoft secondo le impostazioni di privacy di Windows.

## Compilazione

Serve il [.NET 10 SDK](https://dotnet.microsoft.com/download) (oppure Visual Studio 2026); `global.json` accetta qualunque versione 10.0.

```powershell
dotnet build DupliFoto.slnx -c Release
dotnet test  DupliFoto.slnx

# cartella autonoma con i due programmi (x64; per i PC Copilot+ con Snapdragon usa win-arm64)
dotnet publish src/DupliFoto.Cli -c Release -r win-x64 -o publish
dotnet publish src/DupliFoto.Gui -c Release -f net10.0-windows10.0.26100.0 -r win-x64 -o publish
powershell -File tools\raccogli-licenze.ps1 -Publish publish -Rid win-x64
```

Le versioni dei pacchetti sono fissate in `Directory.Packages.props`; Dependabot propone gli aggiornamenti come pull request.

Durante la compilazione, Avalonia invia statistiche anonime d'uso (progetto, versione, piattaforma). Per disattivarle si imposta la variabile d'ambiente `AVALONIA_TELEMETRY_OPTOUT=1`; nella CI è già così.

La soluzione compila anche da Linux o macOS, per esempio negli ambienti di sviluppo nel cloud. `Directory.Build.props` abilita la compilazione per Windows, salta i file PRI e sostituisce `mt.exe` con `tools/mt-linux.sh`. L'eseguibile da distribuire resta quello compilato su Windows.

L'interfaccia grafica ha anche un target `net10.0` senza la parte Windows. Serve per provarla e fotografarla da Linux: i test in `tests/DupliFoto.Gui.Tests` la disegnano senza schermo e, con la variabile `DUPLIFOTO_SCREENSHOTS`, salvano le immagini della finestra.

## Pubblicare una release

1. Aggiornare `CHANGELOG.md` con la sezione della nuova versione, per esempio `## [0.2.0] - AAAA-MM-GG`.
2. Aggiornare `<Version>` in `Directory.Build.props`.
3. Creare il tag, in uno dei due modi:
   - dalla pagina di GitHub: **Releases → Draft a new release**, tag `v0.2.0` su `main`, poi **Publish release** (le note si possono lasciare vuote);
   - oppure da terminale:
     ```powershell
     git tag v0.2.0
     git push origin v0.2.0
     ```

GitHub Actions compila ed esegue tutte le prove: Linux, Windows 11 e base Windows 10. Solo se passano, allega alla release gli zip x64 e ARM64 e i checksum. Se le note della release sono vuote, usa la sezione del CHANGELOG. La versione dei programmi viene presa dal tag.

## Struttura

```
src/DupliFoto.Core    motore (net10.0): scansione, hash, punteggi, azioni, report
src/DupliFoto.Accel   Windows ML / ONNX Runtime: embedding su NPU/GPU/CPU
src/DupliFoto.Cli     riga di comando (duplifoto.exe)
src/DupliFoto.Gui     interfaccia grafica (DupliFoto2026.exe), Avalonia
tests/                test xUnit del motore e della finestra, con immagini generate al momento
tools/                modelli ONNX, prove su Windows, raccolta delle licenze, sostituto di mt.exe per Linux
.github/              build, test, prove e release (workflows), aggiornamenti delle dipendenze (Dependabot)
```

## Stato della verifica

A ogni push GitHub Actions compila ed esegue tutti i test su Linux e su Windows. I test del motore coprono:

- tutti i livelli dell'imbuto, le modalità e la verifica byte per byte;
- l'annullamento e la cache;
- le regole di sicurezza: collegamenti, cartella preferita, registro, Cestino (su Windows).

I test d'integrazione usano Magick.NET e MetadataExtractor veri su JPEG e PNG generati al momento: EXIF con sottosecondi e GPS, orientamento, scala di grigi, trasparenza. I test della finestra ripetono il flusso completo su foto vere: ricerca, sposta, scambia, tieni entrambe, annulla, semi-automatica e sola lettura.

Poi prova i programmi pubblicati su Windows:

- **Comandi di base.** `aiuto` e `hardware` funzionano; sul runner vengono rilevati CPU e GPU tramite DirectML.
- **Sola lettura.** Su foto JPEG e PNG trova la copia identica e la versione "WhatsApp" ricompressa, e non tocca nessun file.
- **Semi-automatica e annulla.** Sposta in quarantena solo la copia identica; `annulla` la rimette al suo posto.
- **Windows ML.** Registra i provider certificati e calcola gli embedding con un modello ONNX di prova sulla CPU.
- **Interfaccia grafica.** `DupliFoto2026.exe` si apre e resta aperto. Lo screenshot finisce tra gli artifact.
- **Base Windows 10.** Le stesse prove, senza Windows ML, girano anche su Windows Server 2022. È costruito sulla base di Windows 10 21H2: niente Mica, build precedente a Windows 11. GitHub non offre macchine con Windows 10 vero e proprio.

Restano da provare su un PC vero: Windows 10, le NPU e le GPU dedicate, i file HEIC e RAW, le cartelle OneDrive, archivi grandi. L'eseguibile ARM64 viene compilato ma non provato, perché il runner è x64. `tools/export_dinov2.py` non è stato eseguito.

## Prossimi passi

1. **Interfaccia.** Zoom sincronizzato sulle due foto, scorciatoie da tastiera, icona del programma, griglia di anteprime per i gruppi con molte foto.
2. **Occhi aperti e sorrisi** per scegliere il miglior scatto di gruppo (rilevamento volti via ONNX).
3. **ID di raffica nativi** (BurstUUID di Apple, Samsung) e coppie Live Photo (HEIC+MOV).
4. **Lettura della MFT NTFS** per l'inventario istantaneo di dischi con milioni di file.
5. **Pre-compilazione del modello per la NPU,** per un avvio a freddo più rapido.

## Licenza

Il codice è distribuito con [licenza MIT](LICENSE). I programmi pubblicati includono componenti di terze parti con le proprie licenze: l'elenco è in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), e i testi completi sono nella cartella `licenze/` di ogni release.
