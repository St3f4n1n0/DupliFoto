using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DupliFoto.Core;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>Il flusso della finestra, sul motore vero e su foto vere, senza mostrare nulla a schermo.</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly SamplePhotos _photos = new();

    public void Dispose() => _photos.Dispose();

    internal static MainViewModel NewViewModel(SamplePhotos photos, RunMode mode)
    {
        var vm = new MainViewModel(new SettingsStore(null)) { CachePath = null };
        vm.AddFolders([photos.Photos]);
        vm.SelectedMode = vm.Modes.Single(m => m.Value == mode);
        vm.QuarantineRoot = photos.Quarantine;
        return vm;
    }

    /// <summary>Aspetta che la condizione diventi vera lasciando lavorare il dispatcher (ricerca e anteprime sono asincrone).</summary>
    internal static async Task WaitFor(Func<bool> condition, int timeoutMs = 20_000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condizione non raggiunta in tempo");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private PairItem PairOf(MainViewModel vm, string duplicateName) =>
        vm.Pairs.Single(p => p.DuplicateName == duplicateName);

    [AvaloniaFact]
    public async Task Search_finds_every_kind_of_pair()
    {
        var vm = NewViewModel(_photos, RunMode.Assisted);
        await vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.HasResults);
        Assert.Equal(8, vm.FilesCount);
        Assert.Equal(4, vm.DuplicatesCount);
        Assert.All(vm.Pairs, p => Assert.Equal(PairStatus.Pending, p.Status));
        Assert.Equal(MatchKind.ExactBytes, PairOf(vm, "mare (1).jpg").Member.Kind);
        Assert.Equal(MatchKind.Perceptual, PairOf(vm, "IMG-20260810-WA0001.jpg").Member.Kind);
        Assert.Equal(MatchKind.IdenticalPixels, vm.Pairs.Single(p => p.KeeperName.StartsWith("fiore")).Member.Kind);
        Assert.Equal(MatchKind.Burst, PairOf(vm, "raffica_2.jpg").Member.Kind);
        Assert.DoesNotContain(vm.Pairs, p => p.KeeperName == "montagna.jpg" || p.DuplicateName == "montagna.jpg");

        Assert.NotNull(vm.SelectedPair);
        Assert.True(vm.CanDecide);
        Assert.Equal(4, vm.PendingCount);
    }

    [AvaloniaFact]
    public async Task Moving_swapping_skipping_and_undo()
    {
        var vm = NewViewModel(_photos, RunMode.Assisted);
        await vm.StartCommand.ExecuteAsync(null);

        // Sposta la versione WhatsApp: finisce in quarantena, si passa alla coppia successiva.
        var whatsapp = PairOf(vm, "IMG-20260810-WA0001.jpg");
        vm.SelectedPair = whatsapp;
        await vm.MoveDuplicateCommand.ExecuteAsync(null);
        Assert.Equal(PairStatus.Moved, whatsapp.Status);
        Assert.False(File.Exists(_photos.P("WhatsApp/IMG-20260810-WA0001.jpg")));
        Assert.NotSame(whatsapp, vm.SelectedPair);
        Assert.Equal(1, vm.MovedCount);

        // Scambia la raffica: ora si tiene lo scatto mosso, e il doppione proposto è quello nitido.
        vm.SelectedPair = PairOf(vm, "raffica_2.jpg");
        vm.SwapCommand.Execute(null);
        Assert.Equal("raffica_2.jpg", vm.SelectedPair!.KeeperName);
        Assert.Equal("raffica_1.jpg", vm.SelectedPair.DuplicateName);
        Assert.Equal(PairStatus.Pending, vm.SelectedPair.Status);

        vm.KeepBothCommand.Execute(null);
        Assert.Equal(PairStatus.Skipped, PairOf(vm, "raffica_1.jpg").Status);
        Assert.True(File.Exists(_photos.P("raffica_1.jpg")));

        // Annulla: chiede conferma, poi rimette a posto il file.
        var undo = vm.UndoCommand.ExecuteAsync(null);
        Assert.True(vm.ConfirmVisible);
        vm.ConfirmYesCommand.Execute(null);
        await undo;
        Assert.True(File.Exists(_photos.P("WhatsApp/IMG-20260810-WA0001.jpg")));
        Assert.Equal(PairStatus.Pending, whatsapp.Status);
        Assert.Equal(0, vm.MovedCount);
    }

    [AvaloniaFact]
    public async Task Semi_automatic_offers_to_move_only_exact_copies()
    {
        var vm = NewViewModel(_photos, RunMode.SemiAutomatic);
        var start = vm.StartCommand.ExecuteAsync(null);
        await WaitFor(() => vm.ConfirmVisible || start.IsCompleted);
        Assert.True(vm.ConfirmVisible);
        Assert.Contains("1 doppion", vm.ConfirmText);
        vm.ConfirmYesCommand.Execute(null);
        await start;

        Assert.False(File.Exists(_photos.P("mare (1).jpg")));
        Assert.True(File.Exists(_photos.P("mare.jpg")));
        Assert.Equal(PairStatus.Moved, PairOf(vm, "mare (1).jpg").Status);
        Assert.Equal(3, vm.PendingCount);
        Assert.True(Directory.GetFiles(_photos.Quarantine, "registro-*.jsonl").Length == 1);
    }

    [AvaloniaFact]
    public async Task Read_only_never_touches_files_but_mode_can_change_afterwards()
    {
        var vm = NewViewModel(_photos, RunMode.ReadOnly);
        await vm.StartCommand.ExecuteAsync(null);

        Assert.All(vm.Pairs, p => Assert.Equal(PairStatus.ReportOnly, p.Status));
        Assert.False(vm.CanDecide);
        await vm.MoveDuplicateCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.MovedCount);
        Assert.False(Directory.Exists(_photos.Quarantine));

        // Dopo la ricerca si può passare ad "assistita" senza rifarla.
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        Assert.All(vm.Pairs, p => Assert.Equal(PairStatus.Pending, p.Status));
        Assert.True(vm.CanDecide);
    }

    [AvaloniaFact]
    public async Task Comparison_shows_both_photos()
    {
        var vm = NewViewModel(_photos, RunMode.Assisted);
        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = PairOf(vm, "IMG-20260810-WA0001.jpg");
        await WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);

        Assert.Equal("mare.jpg", vm.Left.FileName);
        Assert.Equal("IMG-20260810-WA0001.jpg", vm.Right.FileName);
        Assert.Contains("1200 × 900", vm.Left.Details);
        Assert.Contains("600 × 450", vm.Right.Details);
        Assert.Equal(1200, vm.Left.Image!.PixelSize.Width);
        Assert.EndsWith("%", vm.ConfidenceText);
    }

    [AvaloniaFact]
    public void Only_one_folder_can_be_the_one_to_keep()
    {
        var vm = new MainViewModel(new SettingsStore(null)) { CachePath = null };
        vm.AddFolders([_photos.Photos, _photos.P("WhatsApp")]);
        Assert.True(vm.KeepAutomatic);
        Assert.True(vm.HasSeveralFolders);
        Assert.Equal(["Foto", "WhatsApp"], vm.Folders.Select(f => f.Name));

        vm.Folders[1].IsKept = true;
        Assert.False(vm.KeepAutomatic);
        Assert.Same(vm.Folders[1], vm.KeptFolder);

        vm.Folders[0].IsKept = true;                                       // un'altra cartella toglie la scelta alla prima
        Assert.False(vm.Folders[1].IsKept);
        Assert.Equal([_photos.Photos], vm.BuildOptions().PreferredFolders);

        vm.KeepAutomatic = true;
        Assert.All(vm.Folders, f => Assert.False(f.IsKept));
        Assert.Empty(vm.BuildOptions().PreferredFolders);

        vm.Folders[1].IsKept = true;
        vm.Folders[1].RemoveCommand.Execute(null);                         // tolta la cartella da tenere: si torna alla scelta automatica
        Assert.True(vm.KeepAutomatic);
        Assert.False(vm.HasSeveralFolders);
        vm.CrossFolderOnly = true;
        Assert.False(vm.BuildOptions().CrossFolderOnly);                   // con una cartella sola non c'è nulla da confrontare
    }

    [AvaloniaFact]
    public void Keep_folder_and_comparison_are_remembered()
    {
        var store = new SettingsStore(Path.Combine(_photos.Root, "gui.json"));
        var vm = new MainViewModel(store) { CachePath = null };
        vm.AddFolders([_photos.Photos, _photos.P("WhatsApp")]);
        vm.Folders[1].IsKept = true;
        vm.CrossFolderOnly = true;
        vm.SaveSettings();

        var again = new MainViewModel(store) { CachePath = null };
        Assert.Equal([false, true], again.Folders.Select(f => f.IsKept));
        Assert.True(again.CrossFolderOnly);
        Assert.False(again.CompareEverywhere);
    }

    [AvaloniaFact]
    public void Settings_of_older_versions_are_carried_over()
    {
        // Le versioni 0.2 e precedenti salvavano in %APPDATA%\DupliFoto; ora tutto sta nella cartella dei file di lavoro.
        string legacy = Path.Combine(_photos.Root, "Roaming", "DupliFoto", "gui.json");
        string current = Path.Combine(_photos.Root, "Local", "DupliFoto", "gui.json");
        new SettingsStore(legacy).Save(new GuiSettings { Folders = [new(_photos.Photos, false)], CrossFolderOnly = true });

        var vm = new MainViewModel(new SettingsStore(current, legacy)) { CachePath = null };
        Assert.Equal([_photos.Photos], vm.Folders.Select(f => f.Path));
        Assert.True(vm.CrossFolderOnly);

        vm.SaveSettings();
        Assert.True(File.Exists(current));
        Assert.False(Directory.Exists(Path.GetDirectoryName(legacy)));   // il vecchio file, e la sua cartella vuota, se ne vanno
    }

    /// <summary>
    /// "Foto" e la sua sottocartella "WhatsApp" aggiunte entrambe, si tiene WhatsApp, solo tra cartelle diverse:
    /// le coppie sono tutte "copia in WhatsApp / copia in Foto", e i doppioni interni a Foto non compaiono.
    /// </summary>
    [AvaloniaFact]
    public async Task Keeping_one_folder_compares_it_with_the_other()
    {
        var vm = NewViewModel(_photos, RunMode.Assisted);
        vm.AddFolders([_photos.P("WhatsApp")]);
        vm.Folders[1].IsKept = true;
        vm.CrossFolderOnly = true;
        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(["mare (1).jpg", "mare.jpg"], vm.Pairs.Select(p => p.DuplicateName).Order());
        Assert.All(vm.Pairs, p => Assert.Equal("IMG-20260810-WA0001.jpg", p.KeeperName));
        Assert.Equal("WhatsApp", vm.Left.RootName);
        Assert.Equal("Foto", vm.Right.RootName);
        Assert.Equal("Da spostare", vm.Right.Role);
        Assert.Equal("Si tiene quella a sinistra: si trova nella cartella da tenere", vm.KeeperReasonText);

        await vm.MoveDuplicateCommand.ExecuteAsync(null);
        Assert.True(File.Exists(_photos.P("WhatsApp/IMG-20260810-WA0001.jpg")));  // la cartella da tenere non si tocca
        Assert.Equal(1, vm.MovedCount);
    }
}

