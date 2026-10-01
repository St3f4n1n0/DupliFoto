using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DupliFoto.Core;
using DupliFoto.Gui.Services;
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

    // Ogni test ha un'applicazione sua (vedi Ui.Run): il tema scelto da un test non passa al successivo.
    public void Dispose() => _photos.Dispose();

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

    [Theory]
    [InlineData("chiaro")]
    [InlineData("scuro")]
    public Task Main_window_states(string theme) => Ui.Run(async () =>
    {
        Application.Current!.RequestedThemeVariant = theme == "scuro" ? ThemeVariant.Dark : ThemeVariant.Light;

        var vm = MainViewModelTests.NewViewModel(_photos, RunMode.Assisted);
        vm.AddFolders([Path.Combine(_photos.Photos, "WhatsApp")]);
        vm.Folders[0].IsKept = true;
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
    });
    /// <summary>
    /// Schermi piccoli: 1366×768 al 100% (circa 1366×697 utili, tolti titolo e barra delle applicazioni) e lo stesso
    /// schermo al 125% (circa 1093×556). Le foto del confronto devono vedersi e nessuna parte deve finire sopra
    /// un'altra; dove non ci sta tutto, la finestra scorre.
    /// </summary>
    [Theory]
    [InlineData(1366, 697, "1366x768", true)]
    [InlineData(1093, 556, "1366x768-125", false)]
    public Task Small_screens_keep_every_part_apart(int width, int height, string name, bool fits) => Ui.Run(async () =>
    {
        var vm = MainViewModelTests.NewViewModel(_photos, RunMode.Assisted);
        vm.AddFolders([Path.Combine(_photos.Photos, "WhatsApp")]);
        vm.Folders[0].IsKept = true;
        var window = new MainWindow { DataContext = vm, WindowState = WindowState.Normal, Width = width, Height = height };
        window.Show();
        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "IMG-20260810-WA0001.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nessun fotogramma");
        Directory.CreateDirectory(OutDir);
        frame.Save(Path.Combine(OutDir, $"5-schermo-{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        // Con gli angoli trasformati: la colonna centrale sta in un Viewbox che la può rimpicciolire.
        Rect At(string control) => window.FindControl<Control>(control) is { } c
            ? new Rect(c.TranslatePoint(default, window)!.Value, c.TranslatePoint(new Point(c.Bounds.Width, c.Bounds.Height), window)!.Value)
            : throw new InvalidOperationException($"Manca {control}");
        Assert.True(At("MiddleColumn").Bottom <= At("DecisionBar").Top + 1, "affidabilità e motivi finiscono sopra i pulsanti");
        Assert.True(At("DecisionBar").Bottom <= At("Stage").Bottom + 1, "i pulsanti escono dal riquadro del confronto");
        Assert.True(At("Stage").Bottom <= At("Counters").Top + 1, "il confronto finisce sopra i contatori");
        Assert.True(At("Counters").Bottom <= At("PairList").Top + 1, "i contatori finiscono sopra l'elenco");
        Assert.False(At("PairActions").Intersects(At("BulkActions")), "i pulsanti della coppia e quelli per tutte si sovrappongono");
        Assert.All(window.GetVisualDescendants().OfType<Image>().Where(i => i.Source is not null),
            i => Assert.True(i.Bounds.Height >= 100, $"foto alta solo {i.Bounds.Height:0} punti"));
        Assert.True(At("SwapButton").Bottom <= At("DecisionBar").Top + 1, "«Scambia» finisce sopra i pulsanti");
        if (fits) Assert.True(At("PairList").Bottom <= height, "a 1366×768 deve starci tutto senza scorrere");
        Assert.Equal(0, window.FindControl<ScrollViewer>("Scroller")!.Offset.Y); // scegliere una coppia non fa scorrere la finestra
        window.Close();
    });

    /// <summary>
    /// Senza Mica (Windows 10, e qui) la finestra deve avere lo sfondo pieno del tema: su Windows 10 Avalonia
    /// ripiega su una finestra trasparente, e con lo sfondo trasparente si vedeva il desktop attraverso l'app.
    /// </summary>
    [Fact]
    public Task Window_background_is_opaque_without_Mica() => Ui.Run(() =>
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = Show(new MainViewModel(new SettingsStore(null)) { CachePath = null });
        Assert.NotEqual(WindowTransparencyLevel.Mica, window.ActualTransparencyLevel);
        var light = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color;

        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color;

        Assert.Equal(255, light.A);
        Assert.Equal(255, dark.A);
        Assert.NotEqual(light, dark); // segue il tema
        window.Close();
    });

    /// <summary>
    /// Lo screenshot del README, con un piccolo archivio di esempio. Solo su richiesta:
    /// DUPLIFOTO_README_SCREENSHOT=docs/images/screenshot.png (e facoltativamente DUPLIFOTO_README_ROOT per la cartella).
    /// </summary>
    [Fact]
    public Task Readme_screenshot() => Ui.Run(async () =>
    {
        if (Environment.GetEnvironmentVariable("DUPLIFOTO_README_SCREENSHOT") is not { Length: > 0 } output) return;
        string root = Environment.GetEnvironmentVariable("DUPLIFOTO_README_ROOT") ?? Path.Combine(Path.GetTempPath(), "Foto");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string holidays = Path.Combine(root, "Vacanze 2024"), whatsapp = Path.Combine(root, "WhatsApp"), exported = Path.Combine(root, "Esportate");
        foreach (var d in new[] { holidays, whatsapp, exported }) Directory.CreateDirectory(d);

        var shot = new DateTime(2024, 8, 10, 19, 42, 5);
        var exif = new DupliFoto.Tests.RealImages.Exif(shot, Latitude: 46.0321, Longitude: 11.2402, Make: "Google", Model: "Pixel 9");
        var lake = DupliFoto.Tests.TestImages.Landscape(11, 1600, 1067);
        DupliFoto.Tests.RealImages.SaveJpeg(lake, Path.Combine(holidays, "lago.jpg"), exif: exif);
        File.Copy(Path.Combine(holidays, "lago.jpg"), Path.Combine(holidays, "lago (1).jpg"));
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Resize(lake, 800, 533), Path.Combine(whatsapp, "IMG-20240810-WA0007.jpg"), quality: 55);
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Landscape(10, 1600, 1067), Path.Combine(holidays, "tramonto_1.jpg"),
            exif: exif with { TakenAt = shot.AddMinutes(20) });
        DupliFoto.Tests.RealImages.SaveJpeg(
            DupliFoto.Tests.TestImages.Resize(DupliFoto.Tests.TestImages.Resize(DupliFoto.Tests.TestImages.Landscape(10, 1600, 1067, shiftX: 0.01), 500, 333), 1600, 1067),
            Path.Combine(holidays, "tramonto_2.jpg"), exif: exif with { TakenAt = shot.AddMinutes(20).AddSeconds(1.2) });
        var hills = DupliFoto.Tests.TestImages.Landscape(12, 1200, 800);
        DupliFoto.Tests.RealImages.SavePng(hills, Path.Combine(holidays, "colline.png"));
        DupliFoto.Tests.RealImages.SavePng(hills, Path.Combine(exported, "colline.png"), comment: "esportata");
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Landscape(13, 1600, 1067), Path.Combine(holidays, "mattino.jpg"),
            exif: exif with { TakenAt = shot.AddDays(1) });

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var vm = new MainViewModel(new SettingsStore(null)) { CachePath = null };
        vm.AddFolders([holidays, whatsapp, exported]);
        vm.Folders[0].IsKept = true;
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        vm.QuarantineRoot = Path.Combine(Path.GetTempPath(), "DupliFoto-Quarantena");
        var window = new MainWindow { DataContext = vm, WindowState = Avalonia.Controls.WindowState.Normal, Width = 1360, Height = 930 };
        window.Show();
        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "IMG-20240810-WA0007.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nessun fotogramma");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        frame.Save(output, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
        Directory.Delete(root, recursive: true);
    });
}
