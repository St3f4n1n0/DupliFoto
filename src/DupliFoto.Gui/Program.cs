using Avalonia;
using DupliFoto.Gui.Services;

namespace DupliFoto.Gui;

internal static class Program
{
    /// <summary>Quando parte il nostro codice: prima ci sono solo l'avvio di .NET e, al primo avvio, lo scompattamento dell'exe.</summary>
    internal static DateTime MainStarted { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        MainStarted = DateTime.Now;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ErrorLog.Write(ex, "fatale");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write(e.Exception, "attività in background");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "avvio");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
#if WINDOWS
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz()
#else
        .UsePlatformDetect()
#endif
        .WithInterFont()
        .LogToTrace();
}
