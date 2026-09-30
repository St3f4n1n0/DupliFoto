using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // Un errore imprevisto non deve chiudere il programma a metà lavoro: lo si registra e lo si segnala.
            // Ogni spostamento è indipendente e annotato nel registro, quindi si può continuare in sicurezza.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "interfaccia");
                vm.StatusText = $"Errore imprevisto: {e.Exception.Message} (dettagli in {ErrorLog.FilePath})";
                e.Handled = true;
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
