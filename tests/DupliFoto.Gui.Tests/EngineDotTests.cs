using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DupliFoto.Core;
using DupliFoto.Core.Imaging;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>I pallini CPU, GPU e NPU: verde dove si lavora, giallo dove si potrebbe, rosso dove no.</summary>
public sealed class EngineDotTests : IDisposable
{
    private readonly SamplePhotos _photos = new();

    public void Dispose() => _photos.Dispose();

    /// <summary>Un PC con GPU (DirectML) e NPU (OpenVINO), come quelli con Intel AI Boost.</summary>
    private static readonly EngineAvailability[] IntelLaptop =
    [
        new(ComputeEngine.Cpu, true, new("Intel Core Ultra 7, 16 thread", "Intel Core Ultra 7, 16 threads")),
        new(ComputeEngine.Gpu, true, new("Intel Arc (GPU via DmlExecutionProvider)", "Intel Arc (GPU via DmlExecutionProvider)")),
        new(ComputeEngine.Npu, true, new("Intel AI Boost (Intel via OpenVINOExecutionProvider)", "Intel AI Boost (Intel via OpenVINOExecutionProvider)")),
    ];

    /// <summary>Una rete neurale finta su un motore a scelta: il colore medio della foto come embedding.</summary>
    private sealed class FakeNeural(ComputeEngine engine) : IEmbeddingProvider
    {
        public string DeviceDescription => $"prova sulla {engine}";
        public ComputeEngine Engine => engine;
        public int PreferredBatchSize => 1;

        public float[][] Embed(IReadOnlyList<RgbImage> images) => images.Select(i =>
        {
            float r = 0, g = 0, b = 0;
            for (int p = 0; p < i.Pixels.Length; p += 3) { r += i.Pixels[p]; g += i.Pixels[p + 1]; b += i.Pixels[p + 2]; }
            float n = MathF.Sqrt(r * r + g * g + b * b);
            return new[] { r / n, g / n, b / n };
        }).ToArray();

        public void Dispose() { }
    }

    private MainViewModel NewViewModel(ComputeEngine? neural, IReadOnlyList<EngineAvailability>? pc = null)
    {
        var vm = new MainViewModel(new SettingsStore(null),
            neural: (model, _, _) => Task.FromResult<IEmbeddingProvider?>(model is null || neural is null ? null : new FakeNeural(neural.Value)),
            detectEngines: pc is null ? null : () => Task.FromResult(pc))
        {
            CachePath = null,
            QuarantineRoot = _photos.Quarantine,
        };
        vm.AddFolders([_photos.Photos]);
        vm.SelectedMode = vm.Modes.Single(m => m.Value == RunMode.Assisted);
        return vm;
    }

    private static DotState[] States(MainViewModel vm) => vm.Engines.Select(e => e.State).ToArray();

    [Fact]
    public Task Without_a_model_only_the_CPU_works() => Ui.Run(async () =>
    {
        var vm = NewViewModel(neural: null, IntelLaptop);
        await vm.DetectEnginesAsync();
        Assert.Equal([DotState.Active, DotState.Ready, DotState.Ready], States(vm));
        Assert.Contains("scegli un modello", vm.Engines[2].Tip); // il suggerimento per usarla

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal([DotState.Active, DotState.Ready, DotState.Ready], States(vm));
        Assert.Null(vm.NeuralEngine);
    });

    [Fact]
    public Task The_dot_of_the_engine_the_network_runs_on_turns_green() => Ui.Run(async () =>
    {
        var vm = NewViewModel(ComputeEngine.Npu, IntelLaptop);
        vm.ModelPath = Path.Combine(_photos.Root, "modello.onnx");
        await vm.DetectEnginesAsync();
        await vm.StartCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs(); // dopo la ricerca si riguarda cosa offre il PC

        Assert.Equal(ComputeEngine.Npu, vm.NeuralEngine);
        Assert.Equal([DotState.Active, DotState.Ready, DotState.Active], States(vm));
        Assert.Contains("NPU: in uso", vm.Engines[2].Tip);
        Assert.Contains("la rete neurale ha lavorato qui", vm.Engines[2].Tip); // le foto degli scatti multipli
        Assert.Contains("Intel AI Boost", vm.Engines[2].Tip);
        Assert.Contains("GPU: disponibile, ma ora lavora un altro motore", vm.Engines[1].Tip);
    });

    [Fact]
    public Task Engines_that_are_missing_are_red() => Ui.Run(async () =>
    {
        var vm = NewViewModel(neural: null);
        await vm.DetectEnginesAsync(); // qui (Linux) solo la CPU, come sui PC senza Windows ML
        Assert.Equal([DotState.Active, DotState.Off, DotState.Off], States(vm));

        // Nella finestra: tre pallini, con i colori del tema.
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 900 };
        window.Show();
        vm.SetEngines([IntelLaptop[0], IntelLaptop[1], new(ComputeEngine.Npu, false, new("Nessuna NPU in questo PC", "No NPU in this PC"))]);
        Dispatcher.UIThread.RunJobs();
        var dots = window.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Classes.Contains("dot")).ToList();
        Assert.Equal(3, dots.Count);
        Color Fill(int i) => Assert.IsAssignableFrom<ISolidColorBrush>(dots[i].Fill).Color;
        Assert.Equal(Color.Parse("#13A10E"), Fill(0));
        Assert.Equal(Color.Parse("#F2B200"), Fill(1));
        Assert.Equal(Color.Parse("#D13438"), Fill(2));
        Assert.Contains("Nessuna NPU in questo PC", vm.Engines[2].Tip);
        window.Close();
    });
}
