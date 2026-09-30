using Avalonia;
using Avalonia.Headless;

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
