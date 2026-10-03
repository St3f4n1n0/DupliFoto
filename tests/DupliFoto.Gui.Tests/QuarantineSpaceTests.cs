using Avalonia.Threading;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using DupliFoto.Tests;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>
/// Le foto su una chiavetta (E:), la quarantena accanto all'exe sul disco C:. Spostare vuol dire copiare: lo si dice
/// una volta. Se il disco della quarantena si sta riempiendo ci si ferma, e si cancella per sempre solo con due conferme.
/// </summary>
public sealed class QuarantineSpaceTests : IDisposable
{
    private readonly SamplePhotos _photos = new();
    private readonly FakeDisks _disks;

    public QuarantineSpaceTests() => _disks = new FakeDisks { Stick = _photos.Photos };

    public void Dispose() => _photos.Dispose();

    private async Task<MainViewModel> SearchedAsync()
    {
        var vm = new MainViewModel(new SettingsStore(null), disks: _disks) { CachePath = null, QuarantineRoot = _photos.Quarantine };
        vm.AddFolders([_photos.Photos]);
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        await vm.StartCommand.ExecuteAsync(null);
        return vm;
    }

    /// <summary>Avvia lo spostamento della coppia e aspetta la prima domanda (o la fine, se non ce ne sono).</summary>
    private static async Task<Task> StartMoving(MainViewModel vm, string duplicate)
    {
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == duplicate);
        var move = vm.MoveDuplicateCommand.ExecuteAsync(null);
        await MainViewModelTests.WaitFor(() => vm.ConfirmVisible || move.IsCompleted);
        return move;
    }

    /// <summary>Aspetta che compaia la domanda con questo titolo (le domande arrivano una dopo l'altra, in sottofondo).</summary>
    private static async Task Question(MainViewModel vm, string title)
    {
        try { await MainViewModelTests.WaitFor(() => vm.ConfirmVisible && vm.ConfirmTitle == title, 5_000); }
        catch (TimeoutException) { Assert.Fail($"manca la domanda «{title}» (c'è: «{(vm.ConfirmVisible ? vm.ConfirmTitle : "nessuna")}»)"); }
    }

    private static async Task Answer(MainViewModel vm, bool yes, string title)
    {
        await Question(vm, title);
        if (yes) vm.ConfirmYesCommand.Execute(null);
        else vm.ConfirmNoCommand.Execute(null);
    }

    private int InQuarantine(string name) =>
        Directory.Exists(_photos.Quarantine) ? Directory.GetFiles(_photos.Quarantine, name, SearchOption.AllDirectories).Length : 0;

    [Fact]
    public Task Moving_to_another_drive_warns_once_that_it_is_slow() => Ui.Run(async () =>
    {
        var vm = await SearchedAsync();

        var move = await StartMoving(vm, "mare (1).jpg");
        Assert.False(vm.ConfirmCanCancel);                              // un avviso, non una domanda
        Assert.Contains(_photos.Quarantine, vm.ConfirmText);
        Assert.Equal("Spostamento su un altro disco", vm.ConfirmTitle);
        Assert.Contains("copiato per intero", vm.ConfirmText);
        Assert.True(File.Exists(_photos.P("mare (1).jpg")));            // si sposta solo dopo «Ho capito»
        await Answer(vm, yes: true, "Spostamento su un altro disco");
        await move;
        Assert.Equal(1, InQuarantine("mare (1).jpg"));

        move = await StartMoving(vm, "IMG-20260810-WA0001.jpg");        // la seconda volta niente avviso
        Assert.False(vm.ConfirmVisible);
        await move;
        Assert.Equal(1, InQuarantine("IMG-20260810-WA0001.jpg"));
    });

    [Fact]
    public Task A_nearly_full_quarantine_drive_suspends_the_moves_and_stop_leaves_everything() => Ui.Run(async () =>
    {
        _disks.Space = FakeDisks.AlmostFull;
        var vm = await SearchedAsync();

        var move = await StartMoving(vm, "mare (1).jpg");
        await Answer(vm, yes: true, "Spostamento su un altro disco");
        await Question(vm, "Disco della quarantena quasi pieno");
        Assert.Contains("meno del 10%", vm.ConfirmText);
        Assert.Equal("Fermati", vm.ConfirmNoText);
        Assert.True(vm.ConfirmIsDangerous);
        await Answer(vm, yes: false, "Disco della quarantena quasi pieno");
        await move;

        var pair = vm.Pairs.Single(p => p.DuplicateName == "mare (1).jpg");
        Assert.True(pair.IsPending);
        Assert.True(File.Exists(_photos.P("mare (1).jpg")));
        Assert.Equal(0, InQuarantine("*.jpg"));
        Assert.Contains("Libera spazio", vm.StatusText);
    });

    [Fact]
    public Task Deleting_for_good_needs_two_confirmations_and_keeps_the_copy_to_keep() => Ui.Run(async () =>
    {
        _disks.Space = FakeDisks.AlmostFull;
        var vm = await SearchedAsync();
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 900 };
        window.Show();

        // Prima volta: «Cancella per sempre…», poi alla seconda domanda «Annulla». Non si tocca niente.
        var move = await StartMoving(vm, "mare (1).jpg");
        await Answer(vm, yes: true, "Spostamento su un altro disco");
        await Answer(vm, yes: true, "Disco della quarantena quasi pieno");
        await Question(vm, "Cancellare per sempre?");
        Assert.True(vm.ConfirmIsDangerous);
        Assert.Contains("non potrà riportarli indietro", vm.ConfirmText);
        await Answer(vm, yes: false, "Cancellare per sempre?");
        await move;
        Assert.True(File.Exists(_photos.P("mare (1).jpg")));

        // Seconda volta: due sì. Il doppione sparisce per sempre, la copia da tenere resta.
        move = await StartMoving(vm, "mare (1).jpg");
        await Answer(vm, yes: true, "Disco della quarantena quasi pieno");
        await Question(vm, "Cancellare per sempre?");
        Dispatcher.UIThread.RunJobs();
        ScreenshotTests.SaveFrame(window, "8-cancellare-per-sempre");
        await Answer(vm, yes: true, "Cancellare per sempre?");
        await move;

        var pair = vm.Pairs.Single(p => p.DuplicateName == "mare (1).jpg");
        Assert.Equal(PairStatus.Deleted, pair.Status);
        Assert.Equal("Cancellato per sempre", pair.StatusText);
        Assert.False(File.Exists(_photos.P("mare (1).jpg")));
        Assert.True(File.Exists(_photos.P("mare.jpg")));
        Assert.Equal(0, InQuarantine("mare (1).jpg"));
        Assert.Contains("cancellati per sempre", vm.StatusText);

        // Vale fino alla prossima ricerca: il doppione dopo non chiede più niente.
        move = await StartMoving(vm, "IMG-20260810-WA0001.jpg");
        Assert.False(vm.ConfirmVisible);
        await move;
        Assert.Equal(PairStatus.Deleted, vm.Pairs.Single(p => p.DuplicateName == "IMG-20260810-WA0001.jpg").Status);
        Assert.True(File.Exists(_photos.P("mare.jpg")));
    });
}
