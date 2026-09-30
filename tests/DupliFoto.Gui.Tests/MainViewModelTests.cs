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
}

