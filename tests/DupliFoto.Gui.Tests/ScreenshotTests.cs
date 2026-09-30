using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using DupliFoto.Core;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>
/// Disegna la finestra vera (Skia) nei suoi stati principali e salva le immagini:
/// servono a controllare l'aspetto senza un PC Windows. Cartella: variabile DUPLIFOTO_SCREENSHOTS,
/// altrimenti "screenshots" accanto ai test.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly SamplePhotos _photos = new();
    private static readonly string OutDir = Environment.GetEnvironmentVariable("DUPLIFOTO_SCREENSHOTS")
                                            ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

    public void Dispose()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        _photos.Dispose();
    }

    private static MainWindow Show(MainViewModel vm)
    {
        var window = new MainWindow { DataContext = vm, WindowState = Avalonia.Controls.WindowState.Normal, Width = 1360, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Save(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nessun fotogramma");
        Directory.CreateDirectory(OutDir);
        frame.Save(Path.Combine(OutDir, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Assert.Equal(1360, frame.PixelSize.Width);
    }

    [AvaloniaTheory]
    [InlineData("chiaro")]
    [InlineData("scuro")]
    public async Task Main_window_states(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "scuro" ? ThemeVariant.Dark : ThemeVariant.Light;

        var vm = MainViewModelTests.NewViewModel(_photos, RunMode.Assisted);
        vm.AddFolders([Path.Combine(_photos.Photos, "WhatsApp")]);
        vm.Folders[0].IsPreferred = true;
        var window = Show(vm);
        Save(window, $"1-benvenuto-{theme}");

        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "IMG-20260810-WA0001.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);
        Save(window, $"2-confronto-{theme}");

        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "raffica_2.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);
        Save(window, $"3-raffica-{theme}");

        var confirm = vm.ConfirmAsync(
            "Spostamento automatico",
            "La modalità «Semi-automatica» può spostare da sola 1 doppione (56 KB): i file identici al byte, riverificati uno per uno subito prima. Li sposto in quarantena? Le altre coppie te le mostro una per una.",
            "Sposta 1");
        Save(window, $"4-conferma-{theme}");
        vm.ConfirmNoCommand.Execute(null);
        Assert.False(await confirm);
        window.Close();
    }
}
