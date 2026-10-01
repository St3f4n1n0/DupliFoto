using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DupliFoto.Core;
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
            // Prima di leggere le impostazioni: «Pulisci DupliFoto.bat» e i file delle versioni precedenti, portati
            // nella cartella accanto all'exe.
            AppFiles.Prepare();
            var vm = new MainViewModel(SettingsStore.Default);
            // Cartelle passate all'avvio, per esempio trascinandole sull'icona del programma.
            vm.AddFolders(desktop.Args ?? []);
            var window = new MainWindow { DataContext = vm };
            window.Opened += (_, _) =>
            {
                NoteSlowStartup();
                // A finestra aperta, senza rallentare l'avvio: via le copie scompattate delle versioni precedenti.
                Task.Run(() => AppFiles.RemoveOldExtractions());
            };
            desktop.MainWindow = window;
            // Alla chiusura anche la copia scompattata di questa versione, se l'utente non ha scelto di tenerla.
            desktop.Exit += (_, _) =>
            {
                if (vm.RemoveTempOnExit) AppFiles.RemoveExtractionAtExit();
            };

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

    /// <summary>
    /// Un avvio lento finisce nel registro degli errori, diviso tra ciò che succede prima del nostro codice
    /// (avvio di .NET e, solo la prima volta, lo scompattamento dell'exe portabile) e l'apertura della finestra.
    /// </summary>
    private static void NoteSlowStartup()
    {
        try
        {
            var started = Process.GetCurrentProcess().StartTime;
            var now = DateTime.Now;
            if (now - started < TimeSpan.FromSeconds(8)) return;
            ErrorLog.Write($"Avvio lento: {(now - started).TotalSeconds:0.0} s in tutto, " +
                           $"{(Program.MainStarted - started).TotalSeconds:0.0} s prima del programma (avvio di .NET, scompattamento al primo avvio), " +
                           $"{(now - Program.MainStarted).TotalSeconds:0.0} s per aprire la finestra", "avvio");
        }
        catch (Exception) { /* è solo una misura */ }
    }
}
