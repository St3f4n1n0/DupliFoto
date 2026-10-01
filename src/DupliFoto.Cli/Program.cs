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
// Progress<T> su console è asincrono: per messaggi in ordine usiamo un reporter sincrono.
IProgress<string> progress = new SyncProgress(m => Console.WriteLine(m));

// Aperto con un doppio clic, o trascinando una cartella sull'icona: la finestra appartiene solo a noi
// e si chiuderebbe subito, prima che si possa leggere qualcosa. In quel caso si aspetta Invio.
bool ownsConsole = Ui.OwnsConsole();
int exitCode = await Run(args);
if (ownsConsole)
{
    Console.Write("\nPremi Invio per chiudere...");
    Console.ReadLine();
}
return exitCode;

// ======================================================================

async Task<int> Run(string[] a)
{
    if (a.Length == 0 || a[0] is "-h" or "--help" or "aiuto")
    {
        PrintHelp();
        return 0;
    }
    if (a[0] is "--versione" or "--version" or "-v")
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        return 0;
    }

    try
    {
        return a[0].ToLowerInvariant() switch
        {
            "annulla" => Undo(a),
            "hardware" => await Hardware(),
            "analizza" => await Analyze(a[1..]),
            _ => await Analyze(a),
        };
    }
    catch (ArgumentException ex)
    {
        Ui.Color(ConsoleColor.Red, ex.Message);
        Console.WriteLine("Usa 'duplifoto-cli aiuto' per l'elenco delle opzioni.");
        return 2;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Interrotto.");
        return 130;
    }
    catch (Exception ex) when (ownsConsole)
    {
        // Senza terminale un errore imprevisto chiuderebbe la finestra all'istante: lo si mostra prima.
        Ui.Color(ConsoleColor.Red, $"Errore imprevisto: {ex}");
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
        string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException($"Manca il valore dopo {arg}");
        switch (arg.ToLowerInvariant())
        {
            case "--modo":
                o.Mode = Next().ToLowerInvariant() switch
                {
                    "sola-lettura" or "lettura" => RunMode.ReadOnly,
                    "assistita" => RunMode.Assisted,
                    "semi-auto" or "semiautomatica" => RunMode.SemiAutomatic,
                    "auto" or "automatica" => RunMode.Automatic,
                    var v => throw new ArgumentException($"Modo sconosciuto: {v}"),
                };
                break;
            case "--soglia":
                o.AutoThreshold = Number(arg, Next(), ScanOptions.AutoThresholdFloor, 100);
                break;
            case "--azione":
                o.Disposal = Next().ToLowerInvariant() switch
                {
                    "quarantena" => DisposalMethod.Quarantine,
                    "cestino" => DisposalMethod.RecycleBin,
                    var v => throw new ArgumentException($"Azione sconosciuta: {v}"),
                };
                break;
            case "--quarantena": o.QuarantineRoot = Path.GetFullPath(Next()); break;
            case "--report": report = Path.GetFullPath(Next()); break;
            case "--modello": model = Path.GetFullPath(Next()); break;
            case "--acceleratore":
                accel = Next().ToLowerInvariant() switch
                {
                    "auto" => AcceleratorPreference.Auto,
                    "npu" => AcceleratorPreference.Npu,
                    "gpu" => AcceleratorPreference.Gpu,
                    "cpu" => AcceleratorPreference.Cpu,
                    var v => throw new ArgumentException($"Acceleratore sconosciuto: {v}"),
                };
                break;
            case "--no-sottocartelle": o.Recursive = false; break;
            case "--nascosti": o.IncludeHidden = true; break;
            case "--raffica": o.BurstWindowSeconds = Number(arg, Next(), 0.1, 3600); break;
            case "--no-raffiche": o.DetectBursts = false; break;
            case "--preferisci": o.PreferredFolders.Add(Path.GetFullPath(Next())); break;
            case "--solo-tra-cartelle": o.CrossFolderOnly = true; break;
            case "--non-interattivo": interactive = false; break;
            case "--no-cache": o.CachePath = null; break;
            case "--thread": o.MaxDegreeOfParallelism = (int)Number(arg, Next(), 1, 1024); break;
            default:
                if (arg.StartsWith("--")) throw new ArgumentException($"Opzione sconosciuta: {arg}");
                o.Roots.Add(Path.GetFullPath(arg));
                break;
        }
    }
    if (o.Roots.Count == 0) throw new ArgumentException("Indica almeno una cartella da analizzare.");
    if (o.CrossFolderOnly && o.Roots.Count < 2)
        throw new ArgumentException("--solo-tra-cartelle confronta cartelle diverse: indicane almeno due.");
    if (o.Mode == RunMode.Assisted && !interactive)
        throw new ArgumentException("La modalità assistita richiede di rispondere alle domande: togli --non-interattivo.");

    Ui.Color(ConsoleColor.Cyan, $"{AppInfo.Name} {AppInfo.Version} · modalità: {ModeLabel(o)}");
    if (o.CrossFolderOnly) Console.WriteLine("Solo tra cartelle diverse: i doppioni dentro la stessa cartella vengono ignorati.");
    foreach (var keep in o.PreferredFolders) Console.WriteLine($"Copie da tenere: quelle in {keep}");

    // --- Acceleratore (facoltativo) ---
    IEmbeddingProvider? embeddings = null;
    if (model is not null)
    {
        try
        {
            embeddings = await WindowsMlEmbeddingProvider.CreateAsync(model, accel, progress);
            Console.WriteLine($"Rete neurale: {embeddings.DeviceDescription}");
        }
        catch (Exception ex)
        {
            Ui.Color(ConsoleColor.Yellow, $"Modello non caricato ({ex.Message}). Proseguo con i soli algoritmi classici.");
        }
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var engine = new DedupEngine(new MagickImageDecoder(), new ExifMetadataReader(), embeddings);
    var result = engine.Run(o, progress, cts.Token);
    embeddings?.Dispose();

    // --- Report (sempre, in ogni modalità) ---
    // Da Esplora risorse la cartella corrente può essere C:\Windows\System32: meglio Documenti\DupliFoto.
    string reportName = $"DupliFoto-report-{DateTime.Now:yyyyMMdd-HHmmss}.html";
    report ??= ownsConsole
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DupliFoto", reportName)
        : Path.GetFullPath(reportName);
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    ReportWriter.WriteHtml(result, report);
    ReportWriter.WriteCsv(result, Path.ChangeExtension(report, ".csv"));

    PrintSummary(result, o);
    Console.WriteLine($"Report: {report}");
    if (ownsConsole) ConsolePrompt.Open(report); // senza terminale, il report si apre da solo nel browser

    if (o.Mode == RunMode.ReadOnly || result.Groups.Count == 0) return 0;

    // Una conferma iniziale prima di qualunque azione automatica, se c'è qualcuno davanti allo schermo.
    if (interactive && o.Mode is RunMode.SemiAutomatic or RunMode.Automatic)
    {
        Console.Write($"\nProcedo? I file verranno spostati in {(o.Disposal == DisposalMethod.Quarantine ? o.QuarantineRoot : "Cestino")} [s/N] ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "s", StringComparison.OrdinalIgnoreCase)) return 0;
    }

    var executor = new ActionExecutor(o, interactive ? new ConsolePrompt() : null, progress);
    var summary = executor.Execute(result);

    Console.WriteLine();
    Ui.Color(ConsoleColor.Green, $"Spostati {summary.Moved:N0} file ({ReportWriter.FormatBytes(summary.BytesFreed)}): " +
                                 $"{summary.AutomaticActions:N0} automatici, {summary.ConfirmedActions:N0} confermati.");
    if (summary.SkippedByUser > 0) Console.WriteLine($"Saltati da te: {summary.SkippedByUser:N0}");
    if (summary.AwaitingReview > 0) Console.WriteLine($"Da rivedere (non toccati): {summary.AwaitingReview:N0}");
    foreach (var w in summary.Warnings) Ui.Color(ConsoleColor.Yellow, "  " + w);
    if (summary.JournalPath is not null && summary.Moved > 0)
        Console.WriteLine($"Per annullare tutto:  duplifoto-cli annulla \"{summary.JournalPath}\"");
    return 0;
}

int Undo(string[] a)
{
    if (a.Length < 2) throw new ArgumentException("Uso: duplifoto-cli annulla <registro.jsonl>");
    var r = ActionJournal.Undo(a[1]);
    Ui.Color(ConsoleColor.Green, $"Ripristinati {r.Restored:N0} file, {r.Skipped:N0} non ripristinati.");
    foreach (var m in r.Messages) Console.WriteLine("  " + m);
    return r.Skipped == 0 ? 0 : 1;
}

async Task<int> Hardware()
{
    Console.WriteLine($"CPU: {Environment.ProcessorCount} thread logici, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
    await WindowsMlEmbeddingProvider.RegisterCertifiedProvidersAsync(progress);
    Console.WriteLine("Dispositivi di calcolo per la rete neurale:");
    foreach (var d in WindowsMlEmbeddingProvider.ListDevices()) Console.WriteLine("  " + d);
    return 0;
}

/// <summary>Un numero da riga di comando, con il punto o la virgola, entro i limiti: altrimenti un messaggio chiaro.</summary>
static double Number(string option, string text, double min, double max)
{
    if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        throw new ArgumentException($"{option}: «{text}» non è un numero.");
    if (value < min || value > max)
        throw new ArgumentException($"{option}: il valore deve essere tra {min} e {max}.");
    return value;
}

static string ModeLabel(ScanOptions o) => o.Mode switch
{
    RunMode.ReadOnly => "sola lettura (nessun file verrà toccato)",
    RunMode.Assisted => "assistita (ogni azione viene chiesta)",
    RunMode.SemiAutomatic => "semi-automatica (automatica solo sui file identici al 100%)",
    RunMode.Automatic => $"automatica fino al {o.EffectiveAutoThreshold:0}% (gli scatti multipli vengono sempre chiesti)",
    _ => o.Mode.ToString(),
};

static void PrintSummary(ScanResult r, ScanOptions o)
{
    Console.WriteLine();
    Console.WriteLine($"Foto analizzate: {r.Files.Count:N0} in {r.Elapsed:mm\\:ss}  ·  illeggibili: {r.UnreadableFiles:N0}");
    foreach (var grp in r.Groups.GroupBy(g => g.Kind).OrderBy(g => g.Key))
        Console.WriteLine($"  {ReportWriter.KindLabel(grp.Key),-18} {grp.Count(),6:N0} gruppi   {ReportWriter.FormatBytes(grp.Sum(g => g.ReclaimableBytes)),10} recuperabili");
    Console.WriteLine($"  {"Totale",-18} {r.Groups.Count,6:N0} gruppi   {ReportWriter.FormatBytes(r.Groups.Sum(g => g.ReclaimableBytes)),10}");
}

static void PrintHelp() => Console.WriteLine("""
    DupliFoto — trova foto doppie, copie ricompresse e scatti multipli

    USO
      duplifoto-cli [analizza] <cartella> [<cartella>...] [opzioni]
      duplifoto-cli annulla <registro.jsonl>    riporta i file dalla quarantena
      duplifoto-cli hardware                    mostra NPU/GPU/CPU disponibili
      duplifoto-cli --versione

    MODALITÀ  (--modo)
      sola-lettura   predefinita: solo report HTML/CSV, nessun file toccato
      assistita      chiede conferma per ogni gruppo
      semi-auto      sposta da sola SOLO i file identici al byte (verificati), chiede il resto
      auto           sposta da sola fino a --soglia (predefinita 99, minimo 90); il resto lo chiede

    OPZIONI
      --azione quarantena|cestino   dove spostare i doppioni (predefinita: quarantena)
      --quarantena <cartella>       cartella di quarantena (predefinita: Immagini\DupliFoto-Quarantena)
      --preferisci <cartella>       tieni sempre le copie che stanno in questa cartella (ripetibile)
      --solo-tra-cartelle           confronta ogni cartella solo con le altre: i doppioni dentro
                                    la stessa cartella vengono ignorati (servono almeno due cartelle)
      --report <file.html>          dove salvare il report (accanto viene creato anche il .csv)
      --modello <file.onnx>         modello neurale per riconoscere gli scatti multipli (es. DINOv2)
      --acceleratore auto|npu|gpu|cpu   dispositivo preferito per il modello (predefinito: auto)
      --raffica <secondi>           distanza massima tra scatti multipli (predefinita: 10)
      --no-raffiche                 non cercare scatti multipli
      --no-sottocartelle  --nascosti  --no-cache  --thread <n>
      --non-interattivo             non fare domande: ciò che richiede conferma resta da rivedere

    ESEMPI
      duplifoto-cli "D:\Foto"
      duplifoto-cli "D:\Foto" "E:\Backup telefono" --modo semi-auto --preferisci "D:\Foto"
      duplifoto-cli "D:\Catalogate" "D:\Da sistemare" --solo-tra-cartelle --preferisci "D:\Catalogate"
      duplifoto-cli "D:\Foto" --modo auto --soglia 98 --modello dinov2-small.onnx --non-interattivo
    """);

internal sealed class SyncProgress(Action<string> report) : IProgress<string>
{
    private readonly object _lock = new();
    public void Report(string value) { lock (_lock) report(value); }
}
