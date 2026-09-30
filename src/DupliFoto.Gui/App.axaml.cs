using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;

namespace DupliFoto.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel(SettingsStore.Default);
            // Cartelle passate all'avvio, per esempio trascinandole sull'icona del programma.
            vm.AddFolders(desktop.Args ?? []);
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
