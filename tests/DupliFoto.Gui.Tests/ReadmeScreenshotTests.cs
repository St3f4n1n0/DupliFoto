using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using DupliFoto.Core;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>Lo screenshot del README, in inglese come il README: gira da solo, perché cambia la lingua.</summary>
[Collection(typeof(InEnglish))]
public sealed class ReadmeScreenshotTests : IDisposable
{
    public void Dispose() => Lang.Set(Lang.Italian);

    /// <summary>
    /// Lo screenshot del README, con un piccolo archivio di esempio. Solo su richiesta:
    /// DUPLIFOTO_README_SCREENSHOT=docs/images/screenshot.png (e facoltativamente DUPLIFOTO_README_ROOT per la cartella).
    /// </summary>
    [Fact]
    public Task Readme_screenshot() => Ui.Run(async () =>
    {
        if (Environment.GetEnvironmentVariable("DUPLIFOTO_README_SCREENSHOT") is not { Length: > 0 } output) return;
        string root = Environment.GetEnvironmentVariable("DUPLIFOTO_README_ROOT") ?? Path.Combine(Path.GetTempPath(), "Photos");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string holidays = Path.Combine(root, "Holidays 2024"), whatsapp = Path.Combine(root, "WhatsApp"), exported = Path.Combine(root, "Exported");
        foreach (var d in new[] { holidays, whatsapp, exported }) Directory.CreateDirectory(d);

        var shot = new DateTime(2024, 8, 10, 19, 42, 5);
        var exif = new DupliFoto.Tests.RealImages.Exif(shot, Latitude: 46.0321, Longitude: 11.2402, Make: "Google", Model: "Pixel 9");
        var lake = DupliFoto.Tests.TestImages.Landscape(11, 1600, 1067);
        DupliFoto.Tests.RealImages.SaveJpeg(lake, Path.Combine(holidays, "lake.jpg"), exif: exif);
        File.Copy(Path.Combine(holidays, "lake.jpg"), Path.Combine(holidays, "lake (1).jpg"));
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Resize(lake, 800, 533), Path.Combine(whatsapp, "IMG-20240810-WA0007.jpg"), quality: 55);
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Landscape(10, 1600, 1067), Path.Combine(holidays, "sunset_1.jpg"),
            exif: exif with { TakenAt = shot.AddMinutes(20) });
        DupliFoto.Tests.RealImages.SaveJpeg(
            DupliFoto.Tests.TestImages.Resize(DupliFoto.Tests.TestImages.Resize(DupliFoto.Tests.TestImages.Landscape(10, 1600, 1067, shiftX: 0.01), 500, 333), 1600, 1067),
            Path.Combine(holidays, "sunset_2.jpg"), exif: exif with { TakenAt = shot.AddMinutes(20).AddSeconds(1.2) });
        var hills = DupliFoto.Tests.TestImages.Landscape(12, 1200, 800);
        DupliFoto.Tests.RealImages.SavePng(hills, Path.Combine(holidays, "hills.png"));
        DupliFoto.Tests.RealImages.SavePng(hills, Path.Combine(exported, "hills.png"), comment: "exported");
        DupliFoto.Tests.RealImages.SaveJpeg(DupliFoto.Tests.TestImages.Landscape(13, 1600, 1067), Path.Combine(holidays, "morning.jpg"),
            exif: exif with { TakenAt = shot.AddDays(1) });

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Lang.Set(Lang.English); // il README è in inglese
        var vm = new MainViewModel(new SettingsStore(null))
        {
            CachePath = null,
            QuarantineRoot = Path.Combine(Path.GetTempPath(), "DupliFoto-Quarantena"),
        };
        vm.AddFolders([holidays, whatsapp, exported]);
        vm.Folders[0].IsKept = true;
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        var window = new MainWindow { DataContext = vm, WindowState = Avalonia.Controls.WindowState.Normal, Width = 1360, Height = 930 };
        window.Show();
        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "IMG-20240810-WA0007.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);
        // I pallini di un portatile con GPU e NPU, senza modello neurale: lavora la CPU.
        vm.SetEngines([new(ComputeEngine.Cpu, true, Text.Empty), new(ComputeEngine.Gpu, true, Text.Empty), new(ComputeEngine.Npu, true, Text.Empty)]);

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nessun fotogramma");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        frame.Save(output, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
        Directory.Delete(root, recursive: true);
    });
}
