using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Imaging;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Reporting;
using DupliFoto.Core.Scanning;
using DupliFoto.Gui.Services;

namespace DupliFoto.Gui.ViewModels;

public enum PairFilter { All, Pending, Exact, Pixels, Perceptual, Burst, Moved, Skipped }

/// <summary>
/// Tutto lo stato della finestra principale. Le regole (quando si sposta da solo, cosa è sicuro)
/// restano nel motore: qui si decide solo cosa mostrare e quando chiedere.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ImageLoader _images;
    private readonly Func<IImageDecoder> _decoderFactory;
    private readonly Func<IMetadataReader> _metadataFactory;
    private readonly Func<string?, string, IProgress<string>, Task<IEmbeddingProvider?>> _neuralFactory;
    private readonly Func<Task<IReadOnlyList<EngineAvailability>>> _detectEngines;
    private ScanOptions? _options;
    private ScanResult? _result;
    private ActionSession? _session;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _imageCts;
    private Task _imagesTask = Task.CompletedTask;
    private TaskCompletionSource<bool>? _confirm;

    /// <param name="neural">La rete neurale (modello, acceleratore, messaggi): di norma Windows ML, nei test una finta.</param>
    /// <param name="detectEngines">Cosa offre il PC a CPU, GPU e NPU: di norma lo chiede a Windows ML.</param>
    public MainViewModel(SettingsStore store, Func<IImageDecoder>? decoder = null, Func<IMetadataReader>? metadata = null,
        Func<string?, string, IProgress<string>, Task<IEmbeddingProvider?>>? neural = null,
        Func<Task<IReadOnlyList<EngineAvailability>>>? detectEngines = null)
    {
        _detectEngines = detectEngines ?? Neural.DetectEnginesAsync;
        _store = store;
        _decoderFactory = decoder ?? (() => new MagickImageDecoder());
        _metadataFactory = metadata ?? (() => new ExifMetadataReader());
        _neuralFactory = neural ?? Neural.TryCreateAsync;
        _images = new ImageLoader(_decoderFactory());

        _selectedMode = Modes[0];
        _selectedDisposal = Disposals[0];
        _selectedAccelerator = Accelerators[0];
        _selectedFilter = Filters[0];
        _selectedLanguage = Languages[0];
        Lang.Changed += OnLanguageChanged;
        _quarantineRoot = new ScanOptions().QuarantineRoot;
        Folders.CollectionChanged += (_, _) =>
        {
            RefreshFolderNames();
            RefreshState();
        };
        LoadSettings();
        RefreshEngines(); // la CPU subito verde; GPU e NPU appena si sa cosa offre il PC
    }

    // ------------------------------------------------------------------ impostazioni

    public ObservableCollection<FolderItem> Folders { get; } = new();

    /// <summary>Cache delle analisi (in DupliFoto-dati accanto all'exe); <c>null</c> per non usarla.</summary>
    public string? CachePath { get; init; } = AnalysisCache.DefaultPath;

    public IReadOnlyList<Choice<RunMode>> Modes { get; } =
    [
        new(RunMode.ReadOnly, "Sola lettura", "Read-only",
            "Trova i doppioni e mostra il confronto, senza toccare nessun file.",
            "Finds duplicates and shows the comparison, without touching any file."),
        new(RunMode.Assisted, "Assistita", "Assisted",
            "Ti mostra ogni coppia e decidi tu cosa spostare.",
            "Shows you every pair and you decide what to move."),
        new(RunMode.SemiAutomatic, "Semi-automatica", "Semi-automatic",
            "Sposta da sola solo i file identici al byte (riverificati); il resto lo decidi tu.",
            "Moves on its own only byte-identical files (checked again); you decide the rest."),
        new(RunMode.Automatic, "Automatica", "Automatic",
            "Sposta da sola i doppioni sopra la soglia; gli scatti multipli li decidi sempre tu.",
            "Moves on its own the duplicates above the threshold; burst shots are always yours to decide."),
    ];

    public IReadOnlyList<Choice<DisposalMethod>> Disposals { get; } =
    [
        new(DisposalMethod.Quarantine, "Quarantena", "Quarantine",
            "Una cartella da cui si può annullare tutto con un clic.", "A folder from which everything can be undone with one click."),
        new(DisposalMethod.RecycleBin, "Cestino", "Recycle Bin", "Il Cestino di Windows.", "The Windows Recycle Bin."),
    ];

    public IReadOnlyList<Choice<string>> Accelerators { get; } =
    [
        new("auto", "Automatico (NPU, poi GPU, poi CPU)", "Automatic (NPU, then GPU, then CPU)"),
        new("npu", "NPU", "NPU"),
        new("gpu", "GPU", "GPU"),
        new("cpu", "Solo CPU", "CPU only"),
    ];

    public IReadOnlyList<Choice<PairFilter>> Filters { get; } =
    [
        new(PairFilter.All, "Tutte le coppie", "All pairs"),
        new(PairFilter.Pending, "Da decidere", "To decide"),
        new(PairFilter.Exact, "Identici al byte", "Byte-identical"),
        new(PairFilter.Pixels, "Stessi pixel", "Same pixels"),
        new(PairFilter.Perceptual, "Stessa immagine", "Same picture"),
        new(PairFilter.Burst, "Scatti multipli", "Burst shots"),
        new(PairFilter.Moved, "Spostate", "Moved"),
        new(PairFilter.Skipped, "Tenute entrambe", "Kept both"),
    ];

    /// <summary>La lingua: come Windows (italiano se Windows è in italiano, altrimenti inglese), italiano o inglese.</summary>
    public IReadOnlyList<Choice<string>> Languages { get; } =
    [
        new(Lang.Auto, "Automatica (come Windows)", "Automatic (as Windows)"),
        new(Lang.Italian, "Italiano", "Italiano"),
        new(Lang.English, "English", "English"),
    ];

    [ObservableProperty] private bool _includeSubfolders = true;
    /// <summary>Confronta ogni cartella solo con le altre: i doppioni dentro la stessa cartella si ignorano.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompareEverywhere))] private bool _crossFolderOnly;
    [ObservableProperty] private Choice<RunMode> _selectedMode;
    [ObservableProperty] private decimal _threshold = 99;
    [ObservableProperty] private Choice<DisposalMethod> _selectedDisposal;
    [ObservableProperty] private string _quarantineRoot;
    /// <summary>Prima di spostare un file identico, riconfrontarlo per intero (e non solo peso, data, inizio e fine).</summary>
    [ObservableProperty] private bool _verifyBeforeMove;
    [ObservableProperty] private bool _detectBursts = true;
    [ObservableProperty] private decimal _burstSeconds = 10;
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private Choice<string> _selectedAccelerator;
    [ObservableProperty] private Choice<string> _selectedLanguage;

    /// <summary>L'altra scelta di <see cref="CrossFolderOnly"/>, per i pulsanti di scelta.</summary>
    public bool CompareEverywhere
    {
        get => !CrossFolderOnly;
        set => CrossFolderOnly = !value;
    }

    /// <summary>Nessuna cartella da tenere: la copia da tenere la scelgono le regole (risoluzione, metadati, nome...).</summary>
    public bool KeepAutomatic
    {
        get => !Folders.Any(f => f.IsKept);
        set
        {
            if (value) foreach (var f in Folders) f.IsKept = false;
        }
    }

    public FolderItem? KeptFolder => Folders.FirstOrDefault(f => f.IsKept);
    public string KeepHint => KeptFolder is { } k
        ? Lang.T($"Le foto in «{k.Name}» non vengono mai spostate: se ce n'è una copia altrove, si sposta l'altra.",
                 $"The photos in “{k.Name}” are never moved: if there is a copy elsewhere, that one is moved.")
        : Lang.T("Decide DupliFoto, coppia per coppia: nel confronto la copia da tenere è sempre a sinistra.",
                 "DupliFoto decides, pair by pair: in the comparison the copy to keep is always on the left.");
    public bool HasSeveralFolders => Folders.Count >= 2;

    public bool IsReadOnlyMode => SelectedMode.Value == RunMode.ReadOnly;
    public bool IsAutomaticMode => SelectedMode.Value == RunMode.Automatic;
    public bool IsNeuralAvailable => Neural.IsAvailable;
    public string VersionText => Lang.T($"Versione {AppInfo.Version}", $"Version {AppInfo.Version}");
    /// <summary>La cartella dei file di lavoro (impostazioni, cache, registro errori, report e «Pulisci DupliFoto.bat»).</summary>
    public string DataFolder => AppFiles.Folder;
    public bool DataFolderIsNextToExe => AppFiles.IsNextToExe;

    /// <summary>Alla chiusura togliere la copia del programma scompattata in %TEMP%: sul PC non resta niente.</summary>
    [ObservableProperty] private bool _removeTempOnExit = true;

    [RelayCommand]
    private void OpenDataFolder()
    {
        AppFiles.Prepare();
        Shell.Open(AppFiles.Folder);
    }

    // ------------------------------------------------------------------ stato

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusText = ReadyText;

    private static string ReadyText => Lang.T("Aggiungi una o più cartelle e premi «Avvia ricerca».", "Add one or more folders and press “Start search”.");

    /// <summary>Come si riscrive il messaggio nella barra di stato, per tradurlo se si cambia lingua.</summary>
    private Func<string>? _statusMaker = () => ReadyText;
    private bool _saying;

    /// <summary>Un messaggio nella barra di stato che si ritraduce se si cambia lingua.</summary>
    private void Say(Func<string> make)
    {
        _statusMaker = make;
        _saying = true;
        StatusText = make();
        _saying = false;
    }

    partial void OnStatusTextChanged(string value)
    {
        if (!_saying) _statusMaker = null; // un messaggio arrivato da fuori (il motore, un errore): resta com'è
    }

    public bool HasFolders => Folders.Count > 0;
    public bool IsIdle => !IsScanning && !IsWorking;
    public bool CanStart => HasFolders && IsIdle;
    public bool CanChangeDisposal => IsIdle && MovedCount == 0;
    public bool ShowWelcome => !HasResults && !IsScanning;
    public bool ShowNoDuplicates => HasResults && Pairs.Count == 0;
    public bool ShowComparison => HasResults && SelectedPair is not null;
    public bool ShowPickHint => HasResults && Pairs.Count > 0 && SelectedPair is null;
    public bool ShowListEmpty => VisiblePairs.Count == 0;
    public string ListEmptyText => IsScanning ? Lang.T("Ricerca in corso...", "Searching...")
        : !HasResults ? Lang.T("Le coppie trovate compariranno qui.", "The pairs found will appear here.")
        : Pairs.Count == 0 ? Lang.T("Nessun doppione.", "No duplicates.")
        : Lang.T("Nessuna coppia con questo filtro.", "No pairs with this filter.");

    // ------------------------------------------------------------------ contatori

    [ObservableProperty] private int _filesCount;
    [ObservableProperty] private int _groupsCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _movedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private string _reclaimableText = "0 B";
    [ObservableProperty] private string _movedBytesText = "0 B";

    public int DuplicatesCount => Pairs.Count;

    // ------------------------------------------------------------------ coppie

    /// <summary>Tutte le coppie trovate, nell'ordine dei gruppi.</summary>
    public ObservableCollection<PairItem> Pairs { get; } = new();

    /// <summary>Le coppie che passano il filtro scelto.</summary>
    public ObservableCollection<PairItem> VisiblePairs { get; } = new();

    [ObservableProperty] private Choice<PairFilter> _selectedFilter;
    [ObservableProperty] private PairItem? _selectedPair;

    public PhotoPanel Left { get; } = new(isKeeper: true);
    public PhotoPanel Right { get; } = new(isKeeper: false);

    public string ConfidenceText => SelectedPair?.ConfidenceText ?? "—";
    public string KindText => SelectedPair?.KindLabel ?? "";
    public string ReasonText => SelectedPair?.Member.Reason ?? "";
    public string KeeperReasonText => SelectedPair?.Group.KeeperReason is { IsEmpty: false } r
        ? Lang.T($"Si tiene quella a sinistra: {r}", $"The left-hand one is kept: {r}")
        : "";
    public string MoveTip => Lang.T($"La foto a destra va {Destination}; quella a sinistra resta dov'è.",
                                    $"The right-hand photo goes {Destination}; the left-hand one stays where it is.");
    public string PairNoteText => SelectedPair is { Note.Length: > 0 } p ? $"{p.StatusText}: {p.Note}" : SelectedPair?.StatusText ?? "";
    public bool IsHigh => SelectedPair?.IsHigh == true;
    public bool IsMid => SelectedPair?.IsMid == true;
    public bool IsLow => SelectedPair?.IsLow == true;

    public bool CanDecide => IsIdle && !IsReadOnlyMode && SelectedPair?.Status is PairStatus.Pending or PairStatus.Skipped;
    public bool CanSwap => IsIdle && SelectedPair is { Status: not PairStatus.Moved };
    public bool CanNavigate => HasResults && VisiblePairs.Count > 0;
    public bool CanUndo => IsIdle && MovedCount > 0 && _session?.Summary.JournalPath is not null;
    public int AutomaticCount => _options is null ? 0 : Pairs.Count(p => p.IsPending && ActionPolicy.IsAutomatic(_options, p.Member));
    public bool CanApplyAutomatic => IsIdle && AutomaticCount > 0;
    public string AutomaticText => Lang.T($"Sposta automatici ({AutomaticCount})", $"Move automatic ones ({AutomaticCount})");
    public string ApplyAllText => SelectedPair is { } p
        ? Lang.T($"Sposta tutti i «{p.KindLabel}» ({SameKindCount(p)})", $"Move all “{p.KindLabel}” ({SameKindCount(p)})")
        : Lang.T("Sposta tutti di questo tipo", "Move all of this kind");

    private int SameKindCount(PairItem p) => Pairs.Count(x => x.IsPending && x.Member.Kind == p.Member.Kind && x.Member.Confidence >= 60);

    public string DecisionHint
    {
        get
        {
            if (!HasResults || Pairs.Count == 0) return "";
            if (IsReadOnlyMode)
                return Lang.T("Sola lettura: nessun file viene toccato. Per spostare i doppioni scegli un'altra modalità.",
                              "Read-only: no file is touched. To move duplicates, choose another mode.");
            if (PendingCount == 0) return Lang.T("Hai deciso per tutte le coppie.", "You have decided on every pair.");
            if (SelectedPair is { IsPending: true } p)
            {
                int index = Pairs.Where(x => x.IsPending).ToList().IndexOf(p) + 1;
                return Lang.T($"Coppia da decidere {index} di {PendingCount}", $"Pair to decide {index} of {PendingCount}");
            }
            return Lang.T($"{PendingCount} coppie da decidere", $"{PendingCount} pairs to decide");
        }
    }

    // ------------------------------------------------------------------ conferme

    [ObservableProperty] private bool _confirmVisible;
    [ObservableProperty] private string _confirmTitle = "";
    [ObservableProperty] private string _confirmText = "";
    [ObservableProperty] private string _confirmYesText = "";

    /// <summary>Mostra una domanda sopra la finestra e aspetta la risposta.</summary>
    public Task<bool> ConfirmAsync(string title, string text, string yes)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        ConfirmTitle = title;
        ConfirmText = text;
        ConfirmYesText = yes;
        ConfirmVisible = true;
        return _confirm.Task;
    }

    [RelayCommand]
    private void ConfirmYes() => CloseConfirm(true);

    [RelayCommand]
    private void ConfirmNo() => CloseConfirm(false);

    private void CloseConfirm(bool answer)
    {
        ConfirmVisible = false;
        _confirm?.TrySetResult(answer);
    }

    // ------------------------------------------------------------------ cartelle

    public void AddFolders(IEnumerable<string> paths)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string path;
            try { path = Path.GetFullPath(raw); }
            catch (Exception) { continue; }
            if (File.Exists(path)) path = Path.GetDirectoryName(path)!; // trascinata una foto: si intende la sua cartella
            if (!Directory.Exists(path)) continue;
            if (Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new FolderItem(path, f => Folders.Remove(f));
            item.PropertyChanged += OnFolderChanged;
            Folders.Add(item);
        }
    }

    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderItem.IsKept) || sender is not FolderItem changed) return;
        // Una sola cartella da tenere: sceglierne una toglie la scelta alle altre.
        if (changed.IsKept)
            foreach (var f in Folders.Where(f => !ReferenceEquals(f, changed))) f.IsKept = false;
        OnPropertyChanged(nameof(KeepAutomatic));
        OnPropertyChanged(nameof(KeptFolder));
        OnPropertyChanged(nameof(KeepHint));
    }

    private void RefreshFolderNames()
    {
        foreach (var f in Folders)
            f.Name = Folders.Count(o => string.Equals(o.ShortName, f.ShortName, StringComparison.OrdinalIgnoreCase)) > 1 ? f.Path : f.ShortName;
    }

    /// <summary>Il nome della cartella aggiunta da cui viene la foto; nulla se la ricerca era su una cartella sola.</summary>
    private string? RootName(PhotoFile? f) => f is null || _options is not { Roots.Count: > 1 } ? null
        : Folders.FirstOrDefault(x => FileScanner.SameFolder(x.Path, f.Root))?.Name ?? Path.GetFileName(f.Root);

    // ------------------------------------------------------------------ ricerca

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!CanStart) return;
        SaveSettings();
        ClearResults();

        var o = BuildOptions();
        _options = o;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        var progress = new Progress<string>(m => StatusText = m); // messaggi del motore: restano nella lingua in cui arrivano
        IsScanning = true;
        Say(() => Lang.T("Preparo la ricerca...", "Getting ready to search..."));

        IEmbeddingProvider? embeddings = null;
        try
        {
            embeddings = await _neuralFactory(ModelPath, SelectedAccelerator.Value, progress);
            NeuralEngine = embeddings?.Engine;
            _neuralPhotos = null;
            RefreshEngines();
            var engine = new DedupEngine(_decoderFactory(), _metadataFactory(), embeddings);
            _result = await Task.Run(() => engine.Run(o, progress, ct), ct);
            _neuralPhotos = _result.NeuralPhotos;
        }
        catch (OperationCanceledException)
        {
            Say(() => Lang.T("Ricerca interrotta.", "Search stopped."));
            return;
        }
        catch (Exception ex)
        {
            Say(() => Lang.T($"Errore durante la ricerca: {ex.Message}", $"Error during the search: {ex.Message}"));
            return;
        }
        finally
        {
            embeddings?.Dispose();
            IsScanning = false;
            _neuralPhotos ??= 0;
            RefreshEngines();
            // Windows può aver appena scaricato il componente per la NPU o la GPU: si riguarda cosa c'è.
            if (embeddings is not null) _ = DetectEnginesAsync();
        }

        ShowResults(_result);
        if (AutomaticCount > 0) await ApplyAutomaticAsync();
    }

    [RelayCommand]
    private void Cancel() => _scanCts?.Cancel();

    internal ScanOptions BuildOptions()
    {
        var o = new ScanOptions
        {
            CachePath = CachePath,
            Recursive = IncludeSubfolders,
            DetectBursts = DetectBursts,
            BurstWindowSeconds = (double)BurstSeconds,
            Mode = SelectedMode.Value,
            AutoThreshold = Math.Max((double)Threshold, ScanOptions.AutoThresholdFloor),
            Disposal = SelectedDisposal.Value,
            VerifyBeforeMove = VerifyBeforeMove,
            CrossFolderOnly = CrossFolderOnly && HasSeveralFolders,
        };
        if (!string.IsNullOrWhiteSpace(QuarantineRoot)) o.QuarantineRoot = Path.GetFullPath(QuarantineRoot);
        foreach (var f in Folders)
        {
            o.Roots.Add(f.Path);
            if (f.IsKept) o.PreferredFolders.Add(f.Path);
        }
        return o;
    }

    private void ClearResults()
    {
        _session?.Dispose();
        _session = null;
        _result = null;
        SelectedPair = null;
        Pairs.Clear();
        VisiblePairs.Clear();
        HasResults = false;
        FilesCount = GroupsCount = 0;
        RefreshCounters();
    }

    private void ShowResults(ScanResult result)
    {
        _session = new ActionSession(_options!);
        var status = DefaultStatus;
        foreach (var g in result.Groups)
            foreach (var m in g.Duplicates)
                Pairs.Add(new PairItem(g, m) { Status = status });

        FilesCount = result.Files.Count;
        GroupsCount = result.Groups.Count;
        HasResults = true;
        ApplyFilter();
        SelectedPair = VisiblePairs.FirstOrDefault(p => p.IsPending) ?? VisiblePairs.FirstOrDefault();
        Say(() =>
        {
            string unreadable = result.UnreadableFiles > 0
                ? Lang.T($" {result.UnreadableFiles:N0} file illeggibili.", $" {result.UnreadableFiles:N0} unreadable files.")
                : "";
            return Pairs.Count == 0
                ? Lang.T($"Ricerca completata in {result.Elapsed:mm\\:ss}: nessun doppione tra {result.Files.Count:N0} foto.{unreadable}",
                         $"Search completed in {result.Elapsed:mm\\:ss}: no duplicates among {result.Files.Count:N0} photos.{unreadable}")
                : Lang.T($"Ricerca completata in {result.Elapsed:mm\\:ss}: {Pairs.Count:N0} doppioni in {result.Groups.Count:N0} gruppi.{unreadable}",
                         $"Search completed in {result.Elapsed:mm\\:ss}: {Pairs.Count:N0} duplicates in {result.Groups.Count:N0} groups.{unreadable}");
        });
        RefreshCounters();
    }

    private PairStatus DefaultStatus => IsReadOnlyMode ? PairStatus.ReportOnly : PairStatus.Pending;

    // ------------------------------------------------------------------ decisioni

    [RelayCommand]
    private async Task MoveDuplicateAsync()
    {
        if (!CanDecide || SelectedPair is not { } p) return;
        await MoveAsync([p], automatic: false);
        if (p.IsMoved) SelectNextPending(p);
    }

    [RelayCommand]
    private void KeepBoth()
    {
        if (!CanDecide || SelectedPair is not { } p) return;
        p.Status = PairStatus.Skipped;
        p.Note = "";
        RefreshCounters();
        SelectNextPending(p);
    }

    /// <summary>Scambia i ruoli: la foto a destra diventa quella da tenere. Non sposta nulla.</summary>
    [RelayCommand]
    private void Swap()
    {
        if (!CanSwap || SelectedPair is not { } p || _options is null) return;
        var oldKeeper = p.Keeper;
        GroupEditor.ChangeKeeper(p.Group, p.Duplicate, _options);
        RebuildGroup(p.Group, select: oldKeeper);
    }

    [RelayCommand]
    private async Task ApplyToAllOfKindAsync()
    {
        if (!CanDecide || SelectedPair is not { } p) return;
        var kind = p.Member.Kind;
        // Come nella riga di comando: anche con "sì a tutti", sotto il 60% si decide una coppia alla volta.
        var targets = Pairs.Where(x => x.IsPending && x.Member.Kind == kind && x.Member.Confidence >= 60).ToList();
        if (targets.Count == 0) return;
        long bytes = targets.Sum(x => x.Duplicate.Size);
        bool ok = await ConfirmAsync(
            Lang.T($"Spostare tutti i «{p.KindLabel}»?", $"Move all “{p.KindLabel}”?"),
            Lang.T($"Sposto {Destination} i {targets.Count:N0} doppioni ancora da decidere di questo tipo ({ReportWriter.FormatBytes(bytes)}). ",
                   $"I will move {Destination} the {targets.Count:N0} duplicates of this kind still to decide ({ReportWriter.FormatBytes(bytes)}). ") +
            KeepSentence,
            Lang.T($"Sposta {targets.Count:N0}", $"Move {targets.Count:N0}"));
        if (!ok) return;
        await MoveAsync(targets, automatic: false);
        SelectNextPending(p);
    }

    [RelayCommand]
    private async Task ApplyAutomaticAsync()
    {
        if (_options is null || !IsIdle) return;
        var targets = Pairs.Where(p => p.IsPending && ActionPolicy.IsAutomatic(_options, p.Member)).ToList();
        if (targets.Count == 0) return;
        string rule = _options.Mode == RunMode.SemiAutomatic
            ? (_options.VerifyBeforeMove
                ? Lang.T("i file identici al byte, riconfrontati per intero uno per uno subito prima",
                         "byte-identical files, each compared again in full just before")
                : Lang.T("i file identici al byte, ricontrollati uno per uno subito prima (peso, data, inizio e fine del file)",
                         "byte-identical files, each checked again just before (size, date, start and end of the file)"))
            : Lang.T($"quelli con affidabilità di almeno {_options.EffectiveAutoThreshold:0}% (mai gli scatti multipli)",
                     $"those with a confidence of at least {_options.EffectiveAutoThreshold:0}% (never burst shots)");
        string size = ReportWriter.FormatBytes(targets.Sum(p => p.Duplicate.Size));
        bool ok = await ConfirmAsync(
            Lang.T("Spostamento automatico", "Automatic move"),
            Lang.T($"La modalità «{SelectedMode.Label}» può spostare da sola {targets.Count:N0} doppioni ({size}): {rule}. " +
                   $"Li sposto {Destination}? {KeepSentence} Le altre coppie te le mostro una per una.",
                   $"The “{SelectedMode.Label}” mode can move {targets.Count:N0} duplicates on its own ({size}): {rule}. " +
                   $"Shall I move them {Destination}? {KeepSentence} I will show you the other pairs one by one."),
            Lang.T($"Sposta {targets.Count:N0}", $"Move {targets.Count:N0}"));
        if (!ok) return;
        await MoveAsync(targets, automatic: true);
        SelectedPair = VisiblePairs.FirstOrDefault(p => p.IsPending) ?? SelectedPair;
    }

    private string KeepSentence => _options?.PreferredFolders.FirstOrDefault() is { } keep
        ? Lang.T($"Restano sempre le copie nella cartella «{KeptName(keep)}».", $"The copies in the “{KeptName(keep)}” folder always stay.")
        : Lang.T("In ogni coppia resta la copia a sinistra, «da tenere».", "In every pair the left-hand copy, “to keep”, stays.");

    private string KeptName(string keep) => Folders.FirstOrDefault(f => FileScanner.SameFolder(f.Path, keep))?.Name ?? keep;

    private string Destination => SelectedDisposal.Value == DisposalMethod.Quarantine
        ? Lang.T($"in quarantena ({(_options ?? BuildOptions()).QuarantineRoot})", $"to quarantine ({(_options ?? BuildOptions()).QuarantineRoot})")
        : Lang.T("nel Cestino", "to the Recycle Bin");

    private async Task MoveAsync(IReadOnlyList<PairItem> items, bool automatic)
    {
        if (_session is null) return;
        var session = _session;
        IsWorking = true;
        try
        {
            await ReleasePreviewFilesAsync();
            int done = 0;
            foreach (var p in items)
            {
                if (p.IsMoved) continue;
                var outcome = await Task.Run(() => session.Move(p.Keeper, p.Member, automatic));
                p.Status = outcome.Result is MoveResult.Moved or MoveResult.AlreadyHandled ? PairStatus.Moved : PairStatus.Blocked;
                p.Note = outcome.Message;
                if (items.Count > 1 && ++done % 25 == 0) Say(() => Lang.T($"Spostati {done:N0} di {items.Count:N0}...", $"Moved {done:N0} of {items.Count:N0}..."));
            }
            int moved = items.Count(p => p.IsMoved);
            int blocked = items.Count(p => p.IsBlocked);
            Say(() => blocked == 0
                ? Lang.T($"Spostati {moved:N0} file {Destination}.", $"Moved {moved:N0} files {Destination}.")
                : Lang.T($"Spostati {moved:N0} file; {blocked:N0} non toccati per sicurezza (vedi la colonna Stato).",
                         $"Moved {moved:N0} files; {blocked:N0} left alone for safety (see the Status column)."));
        }
        finally
        {
            IsWorking = false;
            RefreshCounters();
            _imagesTask = LoadImagesAsync(SelectedPair); // le anteprime interrotte vanno ricaricate
        }
    }

    /// <summary>
    /// Su Windows un file aperto, anche solo per disegnarne l'anteprima, non si può spostare:
    /// prima di ogni spostamento si ferma il caricamento e si aspetta che i file vengano rilasciati.
    /// </summary>
    private async Task ReleasePreviewFilesAsync()
    {
        _imageCts?.Cancel();
        try { await _imagesTask; }
        catch (Exception) { /* un'anteprima fallita non conta: serve solo che il file sia chiuso */ }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (!CanUndo || _session?.Summary.JournalPath is not { } journal || _options is null) return;
        bool ok = await ConfirmAsync(
            Lang.T("Annullare gli spostamenti?", "Undo the moves?"),
            Lang.T($"Riporto al loro posto i {MovedCount:N0} file spostati in questa sessione.",
                   $"I will put back the {MovedCount:N0} files moved in this session."),
            Lang.T("Ripristina", "Restore"));
        if (!ok) return;

        IsWorking = true;
        try
        {
            _session.Dispose(); // chiude il registro prima di rileggerlo
            var r = await Task.Run(() => ActionJournal.Undo(journal));
            foreach (var p in Pairs.Where(p => p.IsMoved && File.Exists(p.Duplicate.Path)))
            {
                p.Status = DefaultStatus;
                p.Note = Lang.T("ripristinato", "restored");
            }
            _session = new ActionSession(_options);
            Say(() => r.Skipped == 0
                ? Lang.T($"Ripristinati {r.Restored:N0} file.", $"Restored {r.Restored:N0} files.")
                : Lang.T($"Ripristinati {r.Restored:N0} file, {r.Skipped:N0} no: {r.Messages.FirstOrDefault()}",
                         $"Restored {r.Restored:N0} files, {r.Skipped:N0} not: {r.Messages.FirstOrDefault()}"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "annulla");
            _session = new ActionSession(_options);
            Say(() => Lang.T($"Ripristino non completato: {ex.Message}. Il registro è in {journal}.",
                                $"Restore not completed: {ex.Message}. The journal is in {journal}."));
        }
        finally
        {
            IsWorking = false;
            RefreshCounters();
        }
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (_result is null) return;
        string dir = AppFiles.ReportFolder;
        string path = Path.Combine(dir, $"DupliFoto-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        try
        {
            Directory.CreateDirectory(dir);
            ReportWriter.WriteHtml(_result, path);
            ReportWriter.WriteCsv(_result, Path.ChangeExtension(path, ".csv"));
            Say(() => Lang.T($"Report salvato: {path}", $"Report saved: {path}"));
            Shell.Open(path);
        }
        catch (Exception ex)
        {
            Say(() => Lang.T($"Report non salvato: {ex.Message}", $"Report not saved: {ex.Message}"));
        }
    }

    // ------------------------------------------------------------------ navigazione

    [RelayCommand]
    private void Next() => Step(+1);

    [RelayCommand]
    private void Previous() => Step(-1);

    /// <summary>Passa alla coppia successiva (o precedente) da decidere; senza coppie da decidere, alla riga accanto.</summary>
    private void Step(int direction)
    {
        if (VisiblePairs.Count == 0) return;
        int start = SelectedPair is null ? -1 : VisiblePairs.IndexOf(SelectedPair);
        bool onlyPending = !IsReadOnlyMode && VisiblePairs.Any(p => p.IsPending);
        for (int i = 1; i <= VisiblePairs.Count; i++)
        {
            int k = ((start + direction * i) % VisiblePairs.Count + VisiblePairs.Count) % VisiblePairs.Count;
            if (!onlyPending || VisiblePairs[k].IsPending)
            {
                SelectedPair = VisiblePairs[k];
                return;
            }
        }
    }

    private void SelectNextPending(PairItem from)
    {
        int start = VisiblePairs.IndexOf(from);
        var next = VisiblePairs.Skip(start + 1).FirstOrDefault(p => p.IsPending)
                   ?? VisiblePairs.Take(Math.Max(start, 0)).FirstOrDefault(p => p.IsPending);
        if (next is not null) SelectedPair = next;
        else RefreshState();
    }

    /// <summary>Dopo un cambio di copia da tenere, le coppie del gruppo vengono ricreate.</summary>
    private void RebuildGroup(DuplicateGroup g, PhotoFile select)
    {
        var old = Pairs.Where(p => ReferenceEquals(p.Group, g)).ToList();
        int at = old.Count > 0 ? Pairs.IndexOf(old[0]) : Pairs.Count;
        // Resta valido solo lo stato "spostato": il file non c'è più. Tutto il resto va rivalutato.
        var moved = old.Where(p => p.IsMoved).ToDictionary(p => p.Duplicate.Path, p => p.Note, StringComparer.OrdinalIgnoreCase);
        foreach (var p in old) Pairs.Remove(p);

        var status = DefaultStatus;
        // Un file già spostato che con la nuova copia da tenere non è più un doppione (solo tra cartelle diverse)
        // resta comunque nell'elenco: è stato spostato davvero.
        var fresh = old.Where(p => p.IsMoved && !g.Duplicates.Any(m => ReferenceEquals(m.File, p.Duplicate)))
            .Concat(g.Duplicates.Select(m => moved.TryGetValue(m.File.Path, out var note)
                ? new PairItem(g, m) { Status = PairStatus.Moved, Note = note }
                : new PairItem(g, m) { Status = status })).ToList();
        for (int i = 0; i < fresh.Count; i++) Pairs.Insert(at + i, fresh[i]);

        ApplyFilter();
        SelectedPair = VisiblePairs.FirstOrDefault(p => ReferenceEquals(p.Duplicate, select)) ?? VisiblePairs.FirstOrDefault(p => p.Group == g);
        RefreshCounters();
    }

    private void ApplyFilter()
    {
        var keep = SelectedPair;
        VisiblePairs.Clear();
        foreach (var p in Pairs.Where(Matches)) VisiblePairs.Add(p);
        SelectedPair = keep is not null && VisiblePairs.Contains(keep) ? keep : VisiblePairs.FirstOrDefault();
        RefreshState();
    }

    private bool Matches(PairItem p) => SelectedFilter.Value switch
    {
        PairFilter.Pending => p.IsPending,
        PairFilter.Exact => p.Member.Kind == MatchKind.ExactBytes,
        PairFilter.Pixels => p.Member.Kind == MatchKind.IdenticalPixels,
        PairFilter.Perceptual => p.Member.Kind == MatchKind.Perceptual,
        PairFilter.Burst => p.Member.Kind == MatchKind.Burst,
        PairFilter.Moved => p.IsMoved,
        PairFilter.Skipped => p.Status == PairStatus.Skipped,
        _ => true,
    };

    // ------------------------------------------------------------------ reazioni ai cambiamenti

    partial void OnSelectedPairChanged(PairItem? value)
    {
        Left.Show(value?.Keeper, RootName(value?.Keeper));
        Right.Show(value?.Duplicate, RootName(value?.Duplicate));
        _imagesTask = LoadImagesAsync(value);
        RefreshState();
    }

    partial void OnSelectedFilterChanged(Choice<PairFilter> value) => ApplyFilter();

    partial void OnSelectedModeChanged(Choice<RunMode> value)
    {
        if (_options is not null) _options.Mode = value.Value;
        // Cambiare modalità dopo la ricerca non richiede di rifarla: cambia solo cosa si può fare.
        foreach (var p in Pairs)
        {
            if (value.Value == RunMode.ReadOnly && p.IsPending) p.Status = PairStatus.ReportOnly;
            else if (value.Value != RunMode.ReadOnly && p.Status == PairStatus.ReportOnly) p.Status = PairStatus.Pending;
        }
        RefreshCounters();
    }

    partial void OnThresholdChanged(decimal value)
    {
        if (_options is not null) _options.AutoThreshold = Math.Max((double)value, ScanOptions.AutoThresholdFloor);
        RefreshState();
    }

    partial void OnSelectedDisposalChanged(Choice<DisposalMethod> value)
    {
        if (_options is not null) _options.Disposal = value.Value;
    }

    partial void OnVerifyBeforeMoveChanged(bool value)
    {
        if (_options is not null) _options.VerifyBeforeMove = value;
    }

    partial void OnQuarantineRootChanged(string value)
    {
        if (_options is not null && MovedCount == 0 && !string.IsNullOrWhiteSpace(value))
        {
            try { _options.QuarantineRoot = Path.GetFullPath(value); }
            catch (Exception) { /* percorso non valido: resta il precedente */ }
        }
    }

    partial void OnIsScanningChanged(bool value) => RefreshState();
    partial void OnIsWorkingChanged(bool value) => RefreshState();
    partial void OnHasResultsChanged(bool value) => RefreshState();

    private async Task LoadImagesAsync(PairItem? pair)
    {
        _imageCts?.Cancel();
        var cts = _imageCts = new CancellationTokenSource();
        await Task.WhenAll(LoadAsync(Left, pair?.Keeper, cts.Token), LoadAsync(Right, pair?.Duplicate, cts.Token));

        // Prepara la coppia successiva: lo scorrimento diventa immediato.
        if (!cts.IsCancellationRequested && pair is not null)
        {
            int i = VisiblePairs.IndexOf(pair);
            if (i >= 0 && i + 1 < VisiblePairs.Count)
            {
                var next = VisiblePairs[i + 1];
                foreach (var f in new[] { next.Keeper, next.Duplicate })
                    if (File.Exists(f.Path))
                        try { await _images.LoadAsync(f.Path, cts.Token); } catch (Exception) { /* solo un'anticipazione */ }
            }
        }
    }

    private async Task LoadAsync(PhotoPanel panel, PhotoFile? f, CancellationToken ct)
    {
        if (f is null) return;
        panel.IsLoading = true;
        try
        {
            var bitmap = await _images.LoadAsync(f.Path, ct);
            if (!ct.IsCancellationRequested) panel.Image = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                panel.Placeholder = File.Exists(f.Path)
                    ? Lang.T($"Anteprima non disponibile\n{ex.Message}", $"Preview not available\n{ex.Message}")
                    : Lang.T("Il file non è più qui\n(spostato o rinominato)", "The file is no longer here\n(moved or renamed)");
        }
        finally
        {
            if (!ct.IsCancellationRequested) panel.IsLoading = false;
        }
    }

    private void RefreshCounters()
    {
        PendingCount = Pairs.Count(p => p.IsPending);
        MovedCount = Pairs.Count(p => p.IsMoved);
        SkippedCount = Pairs.Count(p => p.Status == PairStatus.Skipped);
        MovedBytesText = ReportWriter.FormatBytes(Pairs.Where(p => p.IsMoved).Sum(p => p.Duplicate.Size));
        ReclaimableText = ReportWriter.FormatBytes(Pairs.Where(p => p.Status is PairStatus.Pending or PairStatus.ReportOnly).Sum(p => p.Duplicate.Size));
        RefreshState();
    }

    /// <summary>Aggiorna tutte le proprietà calcolate (sono poche decine: più semplice che tracciarle una per una).</summary>
    private void RefreshState()
    {
        foreach (var name in DerivedProperties) OnPropertyChanged(name);
    }

    private static readonly string[] DerivedProperties =
    [
        nameof(HasFolders), nameof(IsIdle), nameof(CanStart), nameof(CanChangeDisposal), nameof(ShowWelcome),
        nameof(ShowNoDuplicates), nameof(ShowComparison), nameof(ShowPickHint), nameof(ShowListEmpty), nameof(ListEmptyText), nameof(DuplicatesCount),
        nameof(IsReadOnlyMode), nameof(IsAutomaticMode), nameof(ConfidenceText), nameof(KindText), nameof(ReasonText),
        nameof(KeeperReasonText), nameof(PairNoteText), nameof(IsHigh), nameof(IsMid), nameof(IsLow), nameof(CanDecide),
        nameof(CanSwap), nameof(CanNavigate), nameof(CanUndo), nameof(AutomaticCount), nameof(CanApplyAutomatic),
        nameof(AutomaticText), nameof(ApplyAllText), nameof(DecisionHint), nameof(KeepAutomatic), nameof(KeptFolder),
        nameof(HasSeveralFolders), nameof(MoveTip), nameof(KeepHint),
    ];

    // ------------------------------------------------------------------ impostazioni salvate

    private void LoadSettings()
    {
        var s = _store.Load();
        AddFolders(s.Folders.Select(f => f.Path));
        // Una sola cartella da tenere (le versioni 0.1 permettevano più cartelle "preferite": vale la prima).
        if (s.Folders.FirstOrDefault(x => x.Preferred) is { } kept)
            Folders.FirstOrDefault(f => string.Equals(f.Path, kept.Path, StringComparison.OrdinalIgnoreCase))?.IsKept = true;
        CrossFolderOnly = s.CrossFolderOnly;
        SelectedMode = Modes.FirstOrDefault(m => m.Value == s.Mode) ?? Modes[0];
        Threshold = (decimal)Math.Clamp(s.Threshold, ScanOptions.AutoThresholdFloor, 100);
        SelectedDisposal = Disposals.FirstOrDefault(d => d.Value == s.Disposal) ?? Disposals[0];
        if (!string.IsNullOrWhiteSpace(s.QuarantineRoot)) QuarantineRoot = s.QuarantineRoot;
        VerifyBeforeMove = s.VerifyBeforeMove;
        IncludeSubfolders = s.IncludeSubfolders;
        DetectBursts = s.DetectBursts;
        BurstSeconds = (decimal)Math.Clamp(s.BurstSeconds, 1, 120);
        ModelPath = s.ModelPath;
        SelectedAccelerator = Accelerators.FirstOrDefault(a => a.Value == s.Accelerator) ?? Accelerators[0];
        RemoveTempOnExit = s.RemoveTempOnExit;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == s.Language) ?? Languages[0];
    }

    public void SaveSettings() => _store.Save(new GuiSettings
    {
        Folders = Folders.Select(f => new GuiSettings.FolderSetting(f.Path, f.IsKept)).ToList(),
        CrossFolderOnly = CrossFolderOnly,
        Mode = SelectedMode.Value,
        Threshold = (double)Threshold,
        Disposal = SelectedDisposal.Value,
        QuarantineRoot = QuarantineRoot,
        VerifyBeforeMove = VerifyBeforeMove,
        IncludeSubfolders = IncludeSubfolders,
        DetectBursts = DetectBursts,
        BurstSeconds = (double)BurstSeconds,
        ModelPath = ModelPath,
        Accelerator = SelectedAccelerator.Value,
        RemoveTempOnExit = RemoveTempOnExit,
        Language = SelectedLanguage.Value,
    });

    // ------------------------------------------------------------------ motori: i pallini CPU, GPU e NPU

    public IReadOnlyList<EngineDot> Engines { get; } = [new(ComputeEngine.Cpu), new(ComputeEngine.Gpu), new(ComputeEngine.Npu)];

    private IReadOnlyList<EngineAvailability> _availability = [];

    /// <summary>Dove lavora (o ha lavorato nell'ultima ricerca) la rete neurale; null se non è stata usata.</summary>
    public ComputeEngine? NeuralEngine { get; private set; }

    /// <summary>Quante foto ha esaminato la rete neurale nell'ultima ricerca (null durante la ricerca).</summary>
    private int? _neuralPhotos;

    /// <summary>Guarda cosa offre il PC (senza scaricare niente) e aggiorna i pallini.</summary>
    public async Task DetectEnginesAsync() => SetEngines(await _detectEngines());

    internal void SetEngines(IReadOnlyList<EngineAvailability> availability)
    {
        _availability = availability;
        RefreshEngines();
    }

    /// <summary>
    /// Verde dove si lavora: la CPU sempre (legge e confronta le foto), la GPU o la NPU quando ci lavora la rete neurale.
    /// Giallo dove si potrebbe lavorare, rosso dove no.
    /// </summary>
    private void RefreshEngines()
    {
        foreach (var dot in Engines)
        {
            var found = _availability.FirstOrDefault(a => a.Engine == dot.Engine);
            bool neural = NeuralEngine == dot.Engine;
            dot.Detail = found?.Detail ?? Text.Empty;
            dot.State = dot.Engine == ComputeEngine.Cpu || neural ? DotState.Active
                : found is { Usable: true } ? DotState.Ready
                : DotState.Off;
            dot.Work = (dot.Engine, neural) switch
            {
                (ComputeEngine.Cpu, true) => new("legge e confronta le foto, e fa lavorare la rete neurale",
                                                 "reads and compares the photos, and runs the neural network"),
                (ComputeEngine.Cpu, false) => new("legge e confronta le foto", "reads and compares the photos"),
                (_, true) => _neuralPhotos switch
                {
                    null => new("la rete neurale lavora qui", "the neural network is working here"),
                    0 => new("la rete neurale era pronta qui, ma nell'ultima ricerca nessuna foto ne aveva bisogno",
                             "the neural network was ready here, but no photo in the last search needed it"),
                    int n => new($"la rete neurale ha lavorato qui: {n:N0} foto nell'ultima ricerca",
                                 $"the neural network worked here: {n:N0} photos in the last search"),
                },
                _ when dot.State == DotState.Ready && string.IsNullOrWhiteSpace(ModelPath) =>
                    new("la usa la rete neurale: scegli un modello in «Altre opzioni»",
                        "the neural network uses it: choose a model in “More options”"),
                _ => Text.Empty,
            };
        }
    }

    partial void OnModelPathChanged(string? value) => RefreshEngines();

    // ------------------------------------------------------------------ lingua

    partial void OnSelectedLanguageChanged(Choice<string> value) => Lang.Set(value.Value);

    /// <summary>
    /// Cambio di lingua: i testi fissi della finestra si aggiornano da soli (vedi TExtension); qui quelli calcolati.
    /// I motivi dei doppioni sono nelle due lingue e cambiano anch'essi; restano nella lingua di prima solo i
    /// messaggi già scritti (la barra di stato, le note degli spostamenti).
    /// </summary>
    private void OnLanguageChanged()
    {
        foreach (var c in Modes.Concat<IChoice>(Disposals).Concat(Accelerators).Concat(Filters).Concat(Languages)) c.Refresh();
        foreach (var dot in Engines) dot.Refresh();
        foreach (var f in Folders) f.Refresh();
        foreach (var p in Pairs) p.Refresh();
        Left.Refresh();
        Right.Refresh();
        if (_statusMaker is { } status) Say(status);
        OnPropertyChanged(string.Empty);
    }
}
