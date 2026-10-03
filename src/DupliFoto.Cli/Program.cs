using System.Globalization;
using System.Text;
using DupliFoto.Accel;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Imaging;
using DupliFoto.Core.Reporting;
using DupliFoto.Core.Scanning;
using DupliFoto.Cli;

Console.OutputEncoding = Encoding.UTF8;
// La lingua: quella di Windows (italiano su un Windows italiano, altrimenti inglese), oppure --lingua/--language o la
// variabile DUPLIFOTO_LANG. Vale per i messaggi, le domande, l'aiuto e il report. Comandi e opzioni hanno un nome
// italiano e uno inglese, e funzionano entrambi in tutte e due le lingue.
args = ApplyLanguage(args);
// Progress<T> su console è asincrono: per messaggi in ordine usiamo un reporter sincrono.
IProgress<string> progress = new SyncProgress(m => Console.WriteLine(m));

// Aperto con un doppio clic, o trascinando una cartella sull'icona: la finestra appartiene solo a noi
// e si chiuderebbe subito, prima che si possa leggere qualcosa. In quel caso si aspetta Invio.
bool ownsConsole = Ui.OwnsConsole();
int exitCode = await Run(args);
if (ownsConsole)
{
    Console.Write(T("\nPremi Invio per chiudere...", "\nPress Enter to close..."));
    Console.ReadLine();
    // Aperto con un doppio clic, alla chiusura toglie anche la copia di sé scompattata in %TEMP%: sul PC non resta
    // niente. Da un terminale o da uno script no: i comandi uno dopo l'altro partono subito, senza riscompattarsi
    // ogni volta (alla fine basta «Pulisci DupliFoto.bat /si»).
    AppFiles.RemoveExtractionAtExit();
}
return exitCode;

// ======================================================================

static string T(string italian, string english) => Lang.T(italian, english);

/// <summary>Sceglie la lingua (opzione, poi variabile DUPLIFOTO_LANG, poi Windows) e toglie l'opzione dagli argomenti.</summary>
static string[] ApplyLanguage(string[] a)
{
    string? choice = Environment.GetEnvironmentVariable("DUPLIFOTO_LANG");
    var rest = new List<string>();
    for (int i = 0; i < a.Length; i++)
    {
        if (a[i].ToLowerInvariant() is "--lingua" or "--language")
        {
            if (i + 1 < a.Length) choice = a[++i];
            continue;
        }
        rest.Add(a[i]);
    }
    Lang.Set(choice?.Trim().ToLowerInvariant() switch
    {
        "it" or "ita" or "italiano" or "italian" => Lang.Italian,
        "en" or "eng" or "english" or "inglese" => Lang.English,
        _ => Lang.Auto,
    });
    return rest.ToArray();
}

async Task<int> Run(string[] a)
{
    if (a.Length == 0 || a[0].ToLowerInvariant() is "-h" or "--help" or "--aiuto" or "aiuto" or "help")
    {
        PrintHelp();
        return 0;
    }
    if (a[0].ToLowerInvariant() is "--versione" or "--version" or "-v")
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        return 0;
    }

    try
    {
        return a[0].ToLowerInvariant() switch
        {
            "annulla" or "undo" => Undo(a),
            "hardware" => await Hardware(),
            "analizza" or "analyze" or "analyse" or "scan" => await Analyze(a[1..]),
            _ => await Analyze(a),
        };
    }
    catch (ArgumentException ex)
    {
        Ui.Color(ConsoleColor.Red, ex.Message);
        Console.WriteLine(T("Usa 'duplifoto-cli aiuto' per l'elenco delle opzioni.", "Use 'duplifoto-cli help' for the list of options."));
        return 2;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine(T("Interrotto.", "Stopped."));
        return 130;
    }
    catch (Exception ex) when (ownsConsole)
    {
        // Senza terminale un errore imprevisto chiuderebbe la finestra all'istante: lo si mostra prima.
        Ui.Color(ConsoleColor.Red, T($"Errore imprevisto: {ex}", $"Unexpected error: {ex}"));
        return 1;
    }
}

async Task<int> Analyze(string[] a)
{
    var o = new ScanOptions { CachePath = AnalysisCache.DefaultPath };
    string? model = null, report = null;
    var accel = AcceleratorPreference.Auto;
    bool interactive = true;

    for (int i = 0; i < a.Length; i++)
    {
        string arg = a[i];
        string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException(T($"Manca il valore dopo {arg}", $"Missing value after {arg}"));
        switch (arg.ToLowerInvariant())
        {
            case "--modo" or "--mode":
                o.Mode = Next().ToLowerInvariant() switch
                {
                    "sola-lettura" or "lettura" or "read-only" or "readonly" => RunMode.ReadOnly,
                    "assistita" or "assisted" => RunMode.Assisted,
                    "semi-auto" or "semiautomatica" or "semi-automatica" or "semi-automatic" => RunMode.SemiAutomatic,
                    "auto" or "automatica" or "automatic" => RunMode.Automatic,
                    var v => throw new ArgumentException(T($"Modo sconosciuto: {v}", $"Unknown mode: {v}")),
                };
                break;
            case "--soglia" or "--threshold":
                o.AutoThreshold = Number(arg, Next(), ScanOptions.AutoThresholdFloor, 100);
                break;
            case "--azione" or "--action":
                o.Disposal = Next().ToLowerInvariant() switch
                {
                    "quarantena" or "quarantine" => DisposalMethod.Quarantine,
                    "cestino" or "recycle-bin" or "recyclebin" or "bin" => DisposalMethod.RecycleBin,
                    var v => throw new ArgumentException(T($"Azione sconosciuta: {v}", $"Unknown action: {v}")),
                };
                break;
            case "--quarantena" or "--quarantine": o.QuarantineRoot = Path.GetFullPath(Next()); break;
            case "--verifica-completa" or "--full-check": o.VerifyBeforeMove = true; break;
            case "--report": report = Path.GetFullPath(Next()); break;
            case "--modello" or "--model": model = Path.GetFullPath(Next()); break;
            case "--acceleratore" or "--accelerator":
                accel = Next().ToLowerInvariant() switch
                {
                    "auto" => AcceleratorPreference.Auto,
                    "npu" => AcceleratorPreference.Npu,
                    "gpu" => AcceleratorPreference.Gpu,
                    "cpu" => AcceleratorPreference.Cpu,
                    var v => throw new ArgumentException(T($"Acceleratore sconosciuto: {v}", $"Unknown accelerator: {v}")),
                };
                break;
            case "--no-sottocartelle" or "--no-subfolders": o.Recursive = false; break;
            case "--nascosti" or "--hidden": o.IncludeHidden = true; break;
            case "--raffica" or "--burst": o.BurstWindowSeconds = Number(arg, Next(), 0.1, 3600); break;
            case "--no-raffiche" or "--no-bursts": o.DetectBursts = false; break;
            case "--preferisci" or "--keep" or "--prefer": o.PreferredFolders.Add(Path.GetFullPath(Next())); break;
            case "--solo-tra-cartelle" or "--across-folders": o.CrossFolderOnly = true; break;
            case "--non-interattivo" or "--non-interactive": interactive = false; break;
            case "--no-cache": o.CachePath = null; break;
            case "--thread" or "--threads": o.MaxDegreeOfParallelism = (int)Number(arg, Next(), 1, 1024); break;
            default:
                if (arg.StartsWith("--")) throw new ArgumentException(T($"Opzione sconosciuta: {arg}", $"Unknown option: {arg}"));
                o.Roots.Add(Path.GetFullPath(arg));
                break;
        }
    }
    if (o.Roots.Count == 0) throw new ArgumentException(T("Indica almeno una cartella da analizzare.", "Give at least one folder to scan."));
    if (o.CrossFolderOnly && o.Roots.Count < 2)
        throw new ArgumentException(T("--solo-tra-cartelle confronta cartelle diverse: indicane almeno due.",
                                      "--across-folders compares different folders: give at least two."));
    if (o.Mode == RunMode.Assisted && !interactive)
        throw new ArgumentException(T("La modalità assistita richiede di rispondere alle domande: togli --non-interattivo.",
                                      "Assisted mode needs you to answer questions: remove --non-interactive."));

    Ui.Color(ConsoleColor.Cyan, $"{AppInfo.Name} {AppInfo.Version} · {T("modalità", "mode")}: {ModeLabel(o)}");
    // La cartella dei file di lavoro, con «Pulisci DupliFoto.bat»; le copie scompattate delle versioni precedenti
    // si tolgono intanto, in sottofondo (se il programma finisce prima, si riprende la volta dopo).
    AppFiles.Prepare();
    new Thread(() => AppFiles.RemoveOldExtractions()) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
    // Chiavette, schede di memoria e dischi di rete non hanno un Cestino: lo si dice subito e si usa la quarantena.
    if (o.Disposal == DisposalMethod.RecycleBin && o.Roots.FirstOrDefault(r => !RecycleBin.IsAvailableFor(r)) is { } lacking)
    {
        o.Disposal = DisposalMethod.Quarantine;
        Ui.Color(ConsoleColor.Yellow, T(
            $"{lacking} è su un'unità senza Cestino (chiavetta, scheda di memoria o disco di rete): da lì eliminare un file " +
            $"vorrebbe dire cancellarlo per sempre. I doppioni andranno in quarantena, in {o.QuarantineRoot}. " +
            "Quando hai controllato, se vuoi liberare spazio, cancellali tu a mano da lì.",
            $"{lacking} is on a drive with no Recycle Bin (USB stick, memory card or network drive): there, deleting a file " +
            $"would mean erasing it for good. The duplicates will go to quarantine, in {o.QuarantineRoot}. " +
            "Once you have checked, if you want to free up space, delete them from there yourself."));
    }
    if (o.CrossFolderOnly)
        Console.WriteLine(T("Solo tra cartelle diverse: i doppioni dentro la stessa cartella vengono ignorati.",
                            "Only across different folders: duplicates within the same folder are ignored."));
    foreach (var keep in o.PreferredFolders) Console.WriteLine(T($"Copie da tenere: quelle in {keep}", $"Copies to keep: those in {keep}"));

    // --- Acceleratore (facoltativo) ---
    IEmbeddingProvider? embeddings = null;
    if (model is not null)
    {
        try
        {
            embeddings = await WindowsMlEmbeddingProvider.CreateAsync(model, accel, progress);
            Console.WriteLine(T($"Rete neurale: {embeddings.DeviceDescription}", $"Neural network: {embeddings.DeviceDescription}"));
        }
        catch (Exception ex)
        {
            Ui.Color(ConsoleColor.Yellow, T($"Modello non caricato ({ex.Message}). Proseguo con i soli algoritmi classici.",
                                            $"Model not loaded ({ex.Message}). Carrying on with the classic algorithms only."));
        }
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var engine = new DedupEngine(new MagickImageDecoder(), new ExifMetadataReader(), embeddings);
    var result = engine.Run(o, progress, cts.Token);
    embeddings?.Dispose();

    // --- Report (sempre, in ogni modalità) ---
    // Da Esplora risorse la cartella corrente può essere C:\Windows\System32: meglio DupliFoto-dati\Report accanto all'exe.
    string reportName = $"DupliFoto-report-{DateTime.Now:yyyyMMdd-HHmmss}.html";
    report ??= ownsConsole ? Path.Combine(AppFiles.ReportFolder, reportName) : Path.GetFullPath(reportName);
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    ReportWriter.WriteHtml(result, report);
    ReportWriter.WriteCsv(result, Path.ChangeExtension(report, ".csv"));

    PrintSummary(result, o);
    Console.WriteLine($"Report: {report}");
    if (ownsConsole) ConsolePrompt.Open(report); // senza terminale, il report si apre da solo nel browser

    if (o.Mode == RunMode.ReadOnly || result.Groups.Count == 0) return 0;

    // Una conferma iniziale prima di qualunque azione automatica, se c'è qualcuno davanti allo schermo.
    // Sì è "s" in italiano e "y" in inglese: mai l'una per l'altra.
    if (interactive && o.Mode is RunMode.SemiAutomatic or RunMode.Automatic)
    {
        string destination = o.Disposal == DisposalMethod.Quarantine ? o.QuarantineRoot : T("Cestino", "the Recycle Bin");
        Console.Write(T($"\nProcedo? I file verranno spostati in {destination} [s/N] ", $"\nGo ahead? The files will be moved to {destination} [y/N] "));
        string answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
        bool yes = Lang.IsEnglish ? answer is "y" or "yes" : answer is "s" or "si" or "sì";
        if (!yes) return 0;
    }

    var executor = new ActionExecutor(o, interactive ? new ConsolePrompt() : null, progress);
    var summary = executor.Execute(result);

    Console.WriteLine();
    Ui.Color(ConsoleColor.Green, T(
        $"Spostati {summary.Moved:N0} file ({ReportWriter.FormatBytes(summary.BytesFreed)}): {summary.AutomaticActions:N0} automatici, {summary.ConfirmedActions:N0} confermati.",
        $"Moved {summary.Moved:N0} files ({ReportWriter.FormatBytes(summary.BytesFreed)}): {summary.AutomaticActions:N0} automatic, {summary.ConfirmedActions:N0} confirmed."));
    if (summary.SkippedByUser > 0) Console.WriteLine(T($"Saltati da te: {summary.SkippedByUser:N0}", $"Skipped by you: {summary.SkippedByUser:N0}"));
    if (summary.AwaitingReview > 0) Console.WriteLine(T($"Da rivedere (non toccati): {summary.AwaitingReview:N0}", $"To review (left alone): {summary.AwaitingReview:N0}"));
    foreach (var w in summary.Warnings) Ui.Color(ConsoleColor.Yellow, "  " + w);
    if (summary.JournalPath is not null && summary.Moved > 0)
        Console.WriteLine(T($"Per annullare tutto:  duplifoto-cli annulla \"{summary.JournalPath}\"",
                            $"To undo everything:  duplifoto-cli undo \"{summary.JournalPath}\""));
    return 0;
}

int Undo(string[] a)
{
    if (a.Length < 2) throw new ArgumentException(T("Uso: duplifoto-cli annulla <registro.jsonl>", "Usage: duplifoto-cli undo <journal.jsonl>"));
    var r = ActionJournal.Undo(a[1]);
    Ui.Color(ConsoleColor.Green, T($"Ripristinati {r.Restored:N0} file, {r.Skipped:N0} non ripristinati.",
                                   $"Restored {r.Restored:N0} files, {r.Skipped:N0} not restored."));
    foreach (var m in r.Messages) Console.WriteLine("  " + m);
    return r.Skipped == 0 ? 0 : 1;
}

async Task<int> Hardware()
{
    // Qui sì che si scarica: Windows ML prende i componenti per la NPU o la GPU di questo PC, come alla prima
    // ricerca con il modello. I pallini dell'app invece guardano solo cosa c'è già.
    var engines = await DeviceInventory.DetectAsync(download: true);
    string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    Console.WriteLine(T($"Motori di calcolo per la rete neurale ({arch}):", $"Compute engines for the neural network ({arch}):"));
    foreach (var e in engines)
        Console.WriteLine($"  {e.Engine.ToString().ToUpperInvariant()}  {(e.Usable ? T("sì", "yes") : "no")}  {e.Detail}");
    Console.WriteLine(T("Dispositivi visti da ONNX Runtime:", "Devices seen by ONNX Runtime:"));
    foreach (var d in WindowsMlEmbeddingProvider.ListDevices()) Console.WriteLine("  " + d);
    return 0;
}

/// <summary>Un numero da riga di comando, con il punto o la virgola, entro i limiti: altrimenti un messaggio chiaro.</summary>
static double Number(string option, string text, double min, double max)
{
    if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        throw new ArgumentException(T($"{option}: «{text}» non è un numero.", $"{option}: “{text}” is not a number."));
    if (value < min || value > max)
        throw new ArgumentException(T($"{option}: il valore deve essere tra {min} e {max}.", $"{option}: the value must be between {min} and {max}."));
    return value;
}

static string ModeLabel(ScanOptions o) => o.Mode switch
{
    RunMode.ReadOnly => T("sola lettura (nessun file verrà toccato)", "read-only (no file will be touched)"),
    RunMode.Assisted => T("assistita (ogni azione viene chiesta)", "assisted (every action is asked)"),
    RunMode.SemiAutomatic => T("semi-automatica (automatica solo sui file identici al 100%)", "semi-automatic (automatic only on 100% identical files)"),
    RunMode.Automatic => T($"automatica fino al {o.EffectiveAutoThreshold:0}% (gli scatti multipli vengono sempre chiesti)",
                           $"automatic from {o.EffectiveAutoThreshold:0}% up (burst shots are always asked)"),
    _ => o.Mode.ToString(),
};

static void PrintSummary(ScanResult r, ScanOptions o)
{
    string groups = T("gruppi", "groups"), reclaimable = T("recuperabili", "to reclaim");
    Console.WriteLine();
    Console.WriteLine(T($"Foto analizzate: {r.Files.Count:N0} in {r.Elapsed:mm\\:ss}  ·  illeggibili: {r.UnreadableFiles:N0}",
                        $"Photos analysed: {r.Files.Count:N0} in {r.Elapsed:mm\\:ss}  ·  unreadable: {r.UnreadableFiles:N0}"));
    foreach (var grp in r.Groups.GroupBy(g => g.Kind).OrderBy(g => g.Key))
        Console.WriteLine($"  {ReportWriter.KindLabel(grp.Key),-18} {grp.Count(),6:N0} {groups}   {ReportWriter.FormatBytes(grp.Sum(g => g.ReclaimableBytes)),10} {reclaimable}");
    Console.WriteLine($"  {T("Totale", "Total"),-18} {r.Groups.Count,6:N0} {groups}   {ReportWriter.FormatBytes(r.Groups.Sum(g => g.ReclaimableBytes)),10}");
}

static void PrintHelp() => Console.WriteLine(Lang.IsEnglish ? Help.English : Help.Italian);

internal sealed class SyncProgress(Action<string> report) : IProgress<string>
{
    private readonly object _lock = new();
    public void Report(string value) { lock (_lock) report(value); }
}
