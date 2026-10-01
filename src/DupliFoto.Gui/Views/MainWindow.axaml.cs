using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;

namespace DupliFoto.Gui.Views;

public partial class MainWindow : Window
{
    private IDisposable? _solidBackground;

    public MainWindow()
    {
        InitializeComponent();
        UpdateBackground();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += (_, _) => Vm?.SaveSettings();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty) UpdateBackground();
    }

    /// <summary>
    /// Con Mica lo sfondo della finestra lo disegna Windows. In ogni altro caso serve lo sfondo pieno del tema:
    /// su Windows 10, dove Mica non c'è, Avalonia ripiega su una finestra del tutto trasparente e attraverso
    /// l'app si vedeva il desktop.
    /// </summary>
    private void UpdateBackground()
    {
        _solidBackground?.Dispose();
        _solidBackground = null;
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
            Background = Brushes.Transparent;
        else
            _solidBackground = Bind(BackgroundProperty, this.GetResourceObservable("WindowFallbackBrush"));
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnAddFolder(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Cartelle da analizzare",
            AllowMultiple = true,
        });
        Vm?.AddFolders(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    });

    private async void OnBrowseQuarantine(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Cartella di quarantena" });
        if (Vm is { } vm && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.QuarantineRoot = path;
    });

    private async void OnBrowseModel(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Modello ONNX",
            FileTypeFilter = [new FilePickerFileType("Modello ONNX") { Patterns = ["*.onnx"] }],
        });
        if (Vm is { } vm && files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.ModelPath = path;
    });

    /// <summary>I gestori "async void" non devono mai far cadere il programma: l'errore va nella barra di stato.</summary>
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "finestra");
            if (Vm is { } vm) vm.StatusText = $"Operazione non riuscita: {ex.Message}";
        }
    }

    // Cartelle trascinate da Esplora risorse (una foto trascinata vale per la sua cartella).
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = Vm is { IsIdle: true } && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is not { IsIdle: true } vm) return;
        var items = e.DataTransfer.TryGetFiles() ?? [];
        vm.AddFolders(items.Select(i => i.TryGetLocalPath()).OfType<string>());
    }
}
