using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DupliFoto.Core;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>
/// Le unità senza Cestino (chiavette, schede di memoria, dischi di rete): l'avviso arriva prima di cercare, quando la
/// destinazione si può ancora cambiare, e i doppioni vanno in quarantena. Qui la cartella "WhatsApp" fa la chiavetta.
/// </summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly SamplePhotos _photos = new();
    private bool _stickPlugged = true;

    public void Dispose() => _photos.Dispose();

    private string Stick => _photos.P("WhatsApp");

    private MainViewModel NewViewModel(SettingsStore? store = null) =>
        new(store ?? new SettingsStore(null),
            hasRecycleBin: path => !(_stickPlugged && path.StartsWith(Stick, StringComparison.OrdinalIgnoreCase)))
        {
            CachePath = null,
            QuarantineRoot = _photos.Quarantine,
        };

    private static Choice<DisposalMethod> Choice(MainViewModel vm, DisposalMethod d) => vm.Disposals.Single(c => c.Value == d);

    private void AssertWarned(MainViewModel vm)
    {
        Assert.Equal(DisposalMethod.Quarantine, vm.SelectedDisposal.Value);
        Assert.Equal(DisposalMethod.Quarantine, vm.BuildOptions().Disposal);
        Assert.True(vm.ConfirmVisible);
        Assert.False(vm.ConfirmCanCancel);                    // un avviso: c'è solo «Ho capito»
        Assert.Contains(Stick, vm.ConfirmText);
        Assert.Contains(_photos.Quarantine, vm.ConfirmText);  // dove andranno, per cancellarli poi a mano
        Assert.Contains("cancella tu a mano", vm.ConfirmText);
    }

    [Fact]
    public Task Choosing_the_recycle_bin_with_a_folder_on_a_stick_warns_and_keeps_the_quarantine() => Ui.Run(() =>
    {
        var vm = NewViewModel();
        vm.AddFolders([_photos.Photos, Stick]);
        Assert.False(vm.ConfirmVisible);

        vm.SelectedDisposal = Choice(vm, DisposalMethod.RecycleBin);
        Dispatcher.UIThread.RunJobs(); // il controllo si fa appena il menu ha finito di scrivere la scelta

        AssertWarned(vm);
        vm.ConfirmYesCommand.Execute(null);
        Assert.False(vm.ConfirmVisible);
    });

    [Fact]
    public Task Adding_a_folder_on_a_stick_switches_to_the_quarantine() => Ui.Run(() =>
    {
        var vm = NewViewModel();
        vm.AddFolders([_photos.Photos]);
        vm.SelectedDisposal = Choice(vm, DisposalMethod.RecycleBin);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DisposalMethod.RecycleBin, vm.SelectedDisposal.Value); // disco fisso: il Cestino c'è
        Assert.False(vm.ConfirmVisible);

        vm.AddFolders([Stick]);
        AssertWarned(vm);
    });

    [Fact]
    public Task Saved_settings_with_the_recycle_bin_and_a_stick_warn_at_start() => Ui.Run(() =>
    {
        var store = new SettingsStore(Path.Combine(_photos.Root, "gui.json"));
        _stickPlugged = false;
        var before = NewViewModel(store);
        before.AddFolders([_photos.Photos, Stick]);
        before.SelectedDisposal = Choice(before, DisposalMethod.RecycleBin);
        before.SaveSettings();

        _stickPlugged = true; // ora quella cartella è su una chiavetta
        var vm = NewViewModel(store);
        Dispatcher.UIThread.RunJobs(); // il controllo si fa appena parte la finestra
        AssertWarned(vm);
    });

    [Fact]
    public Task The_search_waits_for_the_warning_and_the_duplicates_go_to_quarantine() => Ui.Run(async () =>
    {
        _stickPlugged = false;
        var vm = NewViewModel();
        vm.AddFolders([_photos.Photos, Stick]);
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        vm.SelectedDisposal = Choice(vm, DisposalMethod.RecycleBin);
        Dispatcher.UIThread.RunJobs();

        _stickPlugged = true; // la chiavetta è stata cambiata dopo aver scelto
        var search = vm.StartCommand.ExecuteAsync(null);
        await Task.Delay(100);
        Assert.False(search.IsCompleted);
        Assert.False(vm.IsScanning);                          // prima si legge l'avviso
        AssertWarned(vm);

        vm.ConfirmYesCommand.Execute(null);
        await search;
        Assert.True(vm.HasResults);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "mare (1).jpg");
        await vm.MoveDuplicateCommand.ExecuteAsync(null);
        Assert.False(File.Exists(_photos.P("mare (1).jpg")));
        Assert.Single(Directory.GetFiles(_photos.Quarantine, "mare (1).jpg", SearchOption.AllDirectories));
    });

    [Fact]
    public Task The_window_shows_the_quarantine_and_only_the_ok_button() => Ui.Run(() =>
    {
        var vm = NewViewModel();
        vm.AddFolders([_photos.Photos, Stick]);
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var destination = window.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.Disposals));

        destination.SelectedItem = Choice(vm, DisposalMethod.RecycleBin);   // come se l'utente scegliesse «Cestino»
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Choice(vm, DisposalMethod.Quarantine), destination.SelectedItem);
        AssertWarned(vm);
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.False(buttons.Single(b => ReferenceEquals(b.Command, vm.ConfirmNoCommand)).IsVisible);
        Assert.True(buttons.Single(b => ReferenceEquals(b.Command, vm.ConfirmYesCommand)).IsEffectivelyVisible);
        ScreenshotTests.SaveFrame(window, "7-senza-cestino");
    });
}
