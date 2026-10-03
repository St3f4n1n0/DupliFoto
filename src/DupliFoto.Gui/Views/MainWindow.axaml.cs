using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DupliFoto.Core;
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
        Scroller.SizeChanged += (_, _) => UpdateLayoutMode();
        TopCard.SizeChanged += (_, _) => UpdateLayoutMode();
        // Scegliere una coppia porta la sua riga in vista dentro l'elenco, e lì basta: se la finestra scorresse
        // anche lei, i pulsanti si sposterebbero sotto il mouse.
        PairList.AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);
    }

    /// <summary>Sotto questa altezza utile (1366×768, o schermi più alti con il ridimensionamento al 125-150%): disposizione compatta.</summary>
    internal const double CompactBelow = 860;

    /// <summary>
    /// Schermi bassi: margini e caratteri più piccoli; e se nemmeno così ci sta tutto, la finestra scorre invece di
    /// mettere il confronto sopra l'elenco. Dentro lo ScrollViewer la griglia avrebbe un'altezza infinita: le si dà
    /// quella della finestra, ma mai meno di quanto serve al riquadro in alto più il minimo per confronto ed elenco.
    /// </summary>
    private void UpdateLayoutMode()
    {
        double available = Scroller.Bounds.Height;
        if (available <= 0) return;
        bool compact = available < CompactBelow;
        var rows = RootGrid.RowDefinitions;
        if (compact != RootGrid.Classes.Contains("compact"))
        {
            RootGrid.Classes.Set("compact", compact);
            rows[3].Height = new GridLength(compact ? 185 : 230); // contatori ed elenco; poi l'altezza la decide il divisore
        }
        double minimum = TopCard.Bounds.Height + rows[1].MinHeight + rows[2].Height.Value + rows[3].MinHeight;
        RootGrid.Height = Math.Max(available - RootGrid.Margin.Top - RootGrid.Margin.Bottom, minimum);
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
            Title = Strings.FoldersToScan,
            AllowMultiple = true,
        });
        Vm?.AddFolders(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    });

    private async void OnBrowseQuarantine(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Strings.QuarantineFolder });
        if (Vm is { } vm && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.QuarantineRoot = path;
    });

    private async void OnBrowseModel(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Lang.T("Modello ONNX", "ONNX model"),
            FileTypeFilter = [new FilePickerFileType(Lang.T("Modello ONNX", "ONNX model")) { Patterns = ["*.onnx"] }],
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
            if (Vm is { } vm) vm.StatusText = Lang.T($"Operazione non riuscita: {ex.Message}", $"Operation failed: {ex.Message}");
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
