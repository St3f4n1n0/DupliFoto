using Avalonia;
using DupliFoto.Gui.Services;

namespace DupliFoto.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
