using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(DupliFoto.Gui.Tests.TestAppBuilder))]

namespace DupliFoto.Gui.Tests;

public static class TestAppBuilder
{
    // Skia vero (non il disegno finto di Headless): serve per gli screenshot.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Esegue il corpo di un test sul thread dell'interfaccia di una sessione Avalonia senza schermo, con un'applicazione
/// nuova per ogni test. È ciò che faceva [AvaloniaFact], ma con l'API pubblica di Avalonia.Headless: non dipende
/// dagli interni di xunit, quindi xunit si può aggiornare senza aspettare Avalonia.
/// </summary>
public static class Ui
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Ui).Assembly);

    public static Task Run(Action body) => Session.Dispatch(body, TestContext.Current.CancellationToken);

    public static Task Run(Func<Task> body) => Session.Dispatch(async () =>
    {
        await body();
        return true;
    }, TestContext.Current.CancellationToken);
}
