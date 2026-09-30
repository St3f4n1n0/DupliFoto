using System.Collections.ObjectModel;
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
    private ScanOptions? _options;
    private ScanResult? _result;
    private ActionSession? _session;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _imageCts;
    private Task _imagesTask = Task.CompletedTask;
    private TaskCompletionSource<bool>? _confirm;

    public MainViewModel(SettingsStore store, Func<IImageDecoder>? decoder = null, Func<IMetadataReader>? metadata = null)
    {
        _store = store;
        _decoderFactory = decoder ?? (() => new MagickImageDecoder());
        _metadataFactory = metadata ?? (() => new ExifMetadataReader());
        _images = new ImageLoader(_decoderFactory());

        _selectedMode = Modes[0];
        _selectedDisposal = Disposals[0];
        _selectedAccelerator = Accelerators[0];
        _selectedFilter = Filters[0];
        _quarantineRoot = new ScanOptions().QuarantineRoot;
        Folders.CollectionChanged += (_, _) => RefreshState();
        LoadSettings();
    }

    // ------------------------------------------------------------------ impostazioni

    public ObservableCollection<FolderItem> Folders { get; } = new();

    /// <summary>Cache delle analisi (in %LOCALAPPDATA%); <c>null</c> per non usarla.</summary>
    public string? CachePath { get; init; } = AnalysisCache.DefaultPath;

    public IReadOnlyList<Choice<RunMode>> Modes { get; } =
    [
        new(RunMode.ReadOnly, "Sola lettura", "Trova i doppioni e mostra il confronto, senza toccare nessun file."),
        new(RunMode.Assisted, "Assistita", "Ti mostra ogni coppia e decidi tu cosa spostare."),
        new(RunMode.SemiAutomatic, "Semi-automatica", "Sposta da sola solo i file identici al byte (riverificati); il resto lo decidi tu."),
        new(RunMode.Automatic, "Automatica", "Sposta da sola i doppioni sopra la soglia; gli scatti multipli li decidi sempre tu."),
    ];

    public IReadOnlyList<Choice<DisposalMethod>> Disposals { get; } =
    [
        new(DisposalMethod.Quarantine, "Quarantena", "Una cartella da cui si può annullare tutto con un clic."),
        new(DisposalMethod.RecycleBin, "Cestino", "Il Cestino di Windows."),
    ];

    public IReadOnlyList<Choice<string>> Accelerators { get; } =
    [
        new("auto", "Automatico (il più veloce)"),
        new("npu", "NPU"),
        new("gpu", "GPU"),
        new("cpu", "Solo CPU"),
    ];

    public IReadOnlyList<Choice<PairFilter>> Filters { get; } =
    [
        new(PairFilter.All, "Tutte le coppie"),
        new(PairFilter.Pending, "Da decidere"),
        new(PairFilter.Exact, "Identici al byte"),
        new(PairFilter.Pixels, "Stessi pixel"),
        new(PairFilter.Perceptual, "Stessa immagine"),
        new(PairFilter.Burst, "Scatti multipli"),
        new(PairFilter.Moved, "Spostate"),
        new(PairFilter.Skipped, "Tenute entrambe"),
    ];

    [ObservableProperty] private bool _includeSubfolders = true;
    [ObservableProperty] private Choice<RunMode> _selectedMode;
    [ObservableProperty] private decimal _threshold = 99;
    [ObservableProperty] private Choice<DisposalMethod> _selectedDisposal;
    [ObservableProperty] private string _quarantineRoot;
    [ObservableProperty] private bool _detectBursts = true;
    [ObservableProperty] private decimal _burstSeconds = 10;
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private Choice<string> _selectedAccelerator;

    public bool IsReadOnlyMode => SelectedMode.Value == RunMode.ReadOnly;
    public bool IsAutomaticMode => SelectedMode.Value == RunMode.Automatic;
    public bool IsNeuralAvailable => Neural.IsAvailable;
    public string VersionText => $"Versione {AppInfo.Version}";

    // ------------------------------------------------------------------ stato

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusText = "Aggiungi una o più cartelle e premi «Avvia ricerca».";

    public bool HasFolders => Folders.Count > 0;
    public bool IsIdle => !IsScanning && !IsWorking;
    public bool CanStart => HasFolders && IsIdle;
    public bool CanChangeDisposal => IsIdle && MovedCount == 0;
    public bool ShowWelcome => !HasResults && !IsScanning;
    public bool ShowNoDuplicates => HasResults && Pairs.Count == 0;
    public bool ShowComparison => HasResults && SelectedPair is not null;
    public bool ShowPickHint => HasResults && Pairs.Count > 0 && SelectedPair is null;
    public bool ShowListEmpty => VisiblePairs.Count == 0;
    public string ListEmptyText => IsScanning ? "Ricerca in corso..."
        : !HasResults ? "Le coppie trovate compariranno qui."
        : Pairs.Count == 0 ? "Nessun doppione."
        : "Nessuna coppia con questo filtro.";

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

    public PhotoPanel Left { get; } = new("Da tenere", isKeeper: true);
    public PhotoPanel Right { get; } = new("Doppione", isKeeper: false);

    public string ConfidenceText => SelectedPair?.ConfidenceText ?? "—";
    public string KindText => SelectedPair?.KindLabel ?? "";
    public string ReasonText => SelectedPair?.Member.Reason ?? "";
    public string KeeperReasonText => SelectedPair?.Group.KeeperReason is { Length: > 0 } r ? $"Da tenere perché: {r}" : "";
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
    public string AutomaticText => $"Sposta automatici ({AutomaticCount})";
    public string ApplyAllText => SelectedPair is { } p
        ? $"Sposta tutti i «{p.KindLabel}» ({Pairs.Count(x => x.IsPending && x.Member.Kind == p.Member.Kind && x.Member.Confidence >= 60)})"
        : "Sposta tutti di questo tipo";

    public string DecisionHint
    {
        get
        {
            if (!HasResults || Pairs.Count == 0) return "";
            if (IsReadOnlyMode) return "Sola lettura: nessun file viene toccato. Per spostare i doppioni scegli un'altra modalità.";
            if (PendingCount == 0) return "Hai deciso per tutte le coppie.";
            if (SelectedPair is { IsPending: true } p)
            {
                int index = Pairs.Where(x => x.IsPending).ToList().IndexOf(p) + 1;
                return $"Coppia da decidere {index} di {PendingCount}";
            }
            return $"{PendingCount} coppie da decidere";
        }
    }

    // ------------------------------------------------------------------ conferme

    [ObservableProperty] private bool _confirmVisible;
    [ObservableProperty] private string _confirmTitle = "";
    [ObservableProperty] private string _confirmText = "";
    [ObservableProperty] private string _confirmYesText = "Sì";

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
            Folders.Add(new FolderItem(path, f => Folders.Remove(f)));
        }
    }

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
        var progress = new Progress<string>(m => StatusText = m);
        IsScanning = true;
        StatusText = "Preparo la ricerca...";

        IEmbeddingProvider? embeddings = null;
        try
        {
            embeddings = await Neural.TryCreateAsync(ModelPath, SelectedAccelerator.Value, progress);
            var engine = new DedupEngine(_decoderFactory(), _metadataFactory(), embeddings);
            _result = await Task.Run(() => engine.Run(o, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Ricerca interrotta.";
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"Errore durante la ricerca: {ex.Message}";
            return;
        }
        finally
        {
            embeddings?.Dispose();
            IsScanning = false;
        }

        ShowResults(_result);
        if (AutomaticCount > 0) await ApplyAutomaticAsync();
    }

    [RelayCommand]
    private void Cancel() => _scanCts?.Cancel();

    private ScanOptions BuildOptions()
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
        };
        if (!string.IsNullOrWhiteSpace(QuarantineRoot)) o.QuarantineRoot = Path.GetFullPath(QuarantineRoot);
        foreach (var f in Folders)
        {
            o.Roots.Add(f.Path);
            if (f.IsPreferred) o.PreferredFolders.Add(f.Path);
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

        string unreadable = result.UnreadableFiles > 0 ? $" {result.UnreadableFiles:N0} file illeggibili." : "";
        StatusText = Pairs.Count == 0
            ? $"Ricerca completata in {result.Elapsed:mm\\:ss}: nessun doppione tra {result.Files.Count:N0} foto.{unreadable}"
            : $"Ricerca completata in {result.Elapsed:mm\\:ss}: {Pairs.Count:N0} doppioni in {result.Groups.Count:N0} gruppi.{unreadable}";
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
            $"Spostare tutti i «{p.KindLabel}»?",
            $"Sposto {Destination} i {targets.Count:N0} doppioni ancora da decidere di questo tipo ({ReportWriter.FormatBytes(bytes)}). " +
            "In ogni gruppo resta la copia indicata come «da tenere».",
            $"Sposta {targets.Count:N0}");
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
            ? "i file identici al byte, riverificati uno per uno subito prima"
            : $"quelli con affidabilità di almeno {_options.EffectiveAutoThreshold:0}% (mai gli scatti multipli)";
        bool ok = await ConfirmAsync(
            "Spostamento automatico",
            $"La modalità «{SelectedMode.Label}» può spostare da sola {targets.Count:N0} doppioni " +
            $"({ReportWriter.FormatBytes(targets.Sum(p => p.Duplicate.Size))}): {rule}. " +
            $"Li sposto {Destination}? Le altre coppie te le mostro una per una.",
            $"Sposta {targets.Count:N0}");
        if (!ok) return;
        await MoveAsync(targets, automatic: true);
        SelectedPair = VisiblePairs.FirstOrDefault(p => p.IsPending) ?? SelectedPair;
    }

    private string Destination => SelectedDisposal.Value == DisposalMethod.Quarantine
        ? $"in quarantena ({(_options ?? BuildOptions()).QuarantineRoot})"
        : "nel Cestino";

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
                if (items.Count > 1 && ++done % 25 == 0) StatusText = $"Spostati {done:N0} di {items.Count:N0}...";
            }
            int moved = items.Count(p => p.IsMoved);
            int blocked = items.Count(p => p.IsBlocked);
            StatusText = blocked == 0
                ? $"Spostati {moved:N0} file {Destination}."
                : $"Spostati {moved:N0} file; {blocked:N0} non toccati per sicurezza (vedi la colonna Stato).";
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
            "Annullare gli spostamenti?",
            $"Riporto al loro posto i {MovedCount:N0} file spostati in questa sessione.",
            "Ripristina");
        if (!ok) return;

        IsWorking = true;
        try
        {
            _session.Dispose(); // chiude il registro prima di rileggerlo
            var r = await Task.Run(() => ActionJournal.Undo(journal));
            foreach (var p in Pairs.Where(p => p.IsMoved && File.Exists(p.Duplicate.Path)))
            {
                p.Status = DefaultStatus;
                p.Note = "ripristinato";
            }
            _session = new ActionSession(_options);
            StatusText = r.Skipped == 0
                ? $"Ripristinati {r.Restored:N0} file."
                : $"Ripristinati {r.Restored:N0} file, {r.Skipped:N0} no: {r.Messages.FirstOrDefault()}";
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "annulla");
            _session = new ActionSession(_options);
            StatusText = $"Ripristino non completato: {ex.Message}. Il registro è in {journal}.";
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
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DupliFoto");
        string path = Path.Combine(dir, $"DupliFoto-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        try
        {
            Directory.CreateDirectory(dir);
            ReportWriter.WriteHtml(_result, path);
            ReportWriter.WriteCsv(_result, Path.ChangeExtension(path, ".csv"));
            StatusText = $"Report salvato: {path}";
            Shell.Open(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Report non salvato: {ex.Message}";
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
        var fresh = g.Duplicates.Select(m => moved.TryGetValue(m.File.Path, out var note)
            ? new PairItem(g, m) { Status = PairStatus.Moved, Note = note }
            : new PairItem(g, m) { Status = status }).ToList();
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
        Left.Show(value?.Keeper);
        Right.Show(value?.Duplicate);
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
                panel.Placeholder = File.Exists(f.Path) ? $"Anteprima non disponibile\n{ex.Message}" : "Il file non è più qui\n(spostato o rinominato)";
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
        nameof(AutomaticText), nameof(ApplyAllText), nameof(DecisionHint),
    ];

    // ------------------------------------------------------------------ impostazioni salvate

    private void LoadSettings()
    {
        var s = _store.Load();
        AddFolders(s.Folders.Select(f => f.Path));
        foreach (var f in Folders)
            f.IsPreferred = s.Folders.Any(x => x.Preferred && string.Equals(x.Path, f.Path, StringComparison.OrdinalIgnoreCase));
        SelectedMode = Modes.FirstOrDefault(m => m.Value == s.Mode) ?? Modes[0];
        Threshold = (decimal)Math.Clamp(s.Threshold, ScanOptions.AutoThresholdFloor, 100);
        SelectedDisposal = Disposals.FirstOrDefault(d => d.Value == s.Disposal) ?? Disposals[0];
        if (!string.IsNullOrWhiteSpace(s.QuarantineRoot)) QuarantineRoot = s.QuarantineRoot;
        IncludeSubfolders = s.IncludeSubfolders;
        DetectBursts = s.DetectBursts;
        BurstSeconds = (decimal)Math.Clamp(s.BurstSeconds, 1, 120);
        ModelPath = s.ModelPath;
        SelectedAccelerator = Accelerators.FirstOrDefault(a => a.Value == s.Accelerator) ?? Accelerators[0];
    }

    public void SaveSettings() => _store.Save(new GuiSettings
    {
        Folders = Folders.Select(f => new GuiSettings.FolderSetting(f.Path, f.IsPreferred)).ToList(),
        Mode = SelectedMode.Value,
        Threshold = (double)Threshold,
        Disposal = SelectedDisposal.Value,
        QuarantineRoot = QuarantineRoot,
        IncludeSubfolders = IncludeSubfolders,
        DetectBursts = DetectBursts,
        BurstSeconds = (double)BurstSeconds,
        ModelPath = ModelPath,
        Accelerator = SelectedAccelerator.Value,
    });
}
