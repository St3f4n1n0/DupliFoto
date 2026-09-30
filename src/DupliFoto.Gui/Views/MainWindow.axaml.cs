using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DupliFoto.Gui.ViewModels;

namespace DupliFoto.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += (_, _) => Vm?.SaveSettings();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Cartelle da analizzare",
            AllowMultiple = true,
        });
        Vm?.AddFolders(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async void OnBrowseQuarantine(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Cartella di quarantena" });
        if (Vm is { } vm && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.QuarantineRoot = path;
    }

    private async void OnBrowseModel(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Modello ONNX",
            FileTypeFilter = [new FilePickerFileType("Modello ONNX") { Patterns = ["*.onnx"] }],
        });
        if (Vm is { } vm && files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.ModelPath = path;
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
