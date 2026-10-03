using DupliFoto.Core;
using DupliFoto.Core.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Windows.AI.MachineLearning;

namespace DupliFoto.Accel;

public enum AcceleratorPreference { Auto, Npu, Gpu, Cpu }

/// <summary>
/// Calcola embedding di immagini con un modello ONNX (es. DINOv2-small) tramite Windows ML.
/// Windows ML scarica e registra gli execution provider certificati per l'hardware presente
/// (Qualcomm QNN, Intel OpenVINO, AMD VitisAI/MIGraphX, NVIDIA TensorRT-RTX) e include DirectML
/// per qualunque GPU DirectX 12.
/// Il dispositivo lo sceglie DupliFoto, non una politica di ONNX Runtime: così sa sempre dove lavora la rete
/// neurale (i pallini nella finestra, il report). Per la NPU il modello viene reso a dimensioni fisse, una foto alla
/// volta: le NPU (Intel OpenVINO in testa) non accettano dimensioni variabili, e con il lotto variabile il modello
/// finiva tutto sulla CPU senza dirlo. Se un motore non c'è o rifiuta il modello si passa al successivo, dicendo perché.
/// </summary>
public sealed class WindowsMlEmbeddingProvider : IEmbeddingProvider
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f]; // normalizzazione ImageNet
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    /// <summary>Solo per le prove su un PC senza NPU: "gpu" o "cpu" fa passare quel dispositivo per la NPU.</summary>
    private const string TestNpuVariable = "DUPLIFOTO_PROVA_NPU";

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _side;
    private bool _batchUnsupported;

    public string DeviceDescription { get; }
    public ComputeEngine Engine { get; }
    public int PreferredBatchSize { get; }

    private WindowsMlEmbeddingProvider(InferenceSession session, ComputeEngine engine, string description, int batch)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
        var dims = session.InputMetadata[_inputName].Dimensions; // es. [-1, 3, 224, 224]
        _side = dims.Length == 4 && dims[3] > 0 ? dims[3] : 224;
        _batchUnsupported = batch == 1 || dims.Length == 4 && dims[0] == 1;
        Engine = engine;
        DeviceDescription = description;
        PreferredBatchSize = _batchUnsupported ? 1 : batch;
    }

    public static async Task<WindowsMlEmbeddingProvider> CreateAsync(
        string modelPath, AcceleratorPreference preference, IProgress<string>? log = null)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException(Lang.T("Modello ONNX non trovato", "ONNX model not found"), modelPath);

        var env = OrtEnv.Instance(); // l'ambiente ONNX Runtime deve esistere prima di registrare i provider
        await RegisterCertifiedProvidersAsync(log);
        var blocked = CrashGuard.Blocked(afterCrash: true);
        var devices = new List<OrtEpDevice>();
        foreach (var d in DeviceInventory.Devices())
        {
            if (!blocked.Contains(CrashGuard.Key(d))) devices.Add(d);
            else if (DeviceInventory.EngineOf(d) is ComputeEngine.Gpu or ComputeEngine.Npu)
                log?.Report(Lang.T($"Rete neurale: {DeviceInventory.Describe(d)} ha fatto chiudere DupliFoto all'ultimo tentativo, lo salto (per riprovarlo cancella {CrashGuard.BlockedFile}).",
                                   $"Neural network: {DeviceInventory.Describe(d)} made DupliFoto close at the last attempt, skipping it (to try it again, delete {CrashGuard.BlockedFile})."));
        }

        // Se il motore scelto non c'è o rifiuta il modello, si passa al successivo. "Automatico" parte dalla NPU:
        // è fatta per la rete neurale e consuma poco.
        ComputeEngine[] order = preference switch
        {
            AcceleratorPreference.Gpu => [ComputeEngine.Gpu, ComputeEngine.Cpu],
            AcceleratorPreference.Cpu => [ComputeEngine.Cpu],
            _ => [ComputeEngine.Npu, ComputeEngine.Gpu, ComputeEngine.Cpu],
        };
        foreach (var engine in order.Where(e => e != ComputeEngine.Cpu))
        {
            var candidates = Candidates(devices, engine);
            if (candidates.Count == 0)
            {
                log?.Report(Lang.T($"Rete neurale: nessuna {engine.ToString().ToUpperInvariant()} utilizzabile da Windows ML.",
                                   $"Neural network: no {engine.ToString().ToUpperInvariant()} that Windows ML can use."));
                continue;
            }
            foreach (var device in candidates)
                if (TryCreate(env, modelPath, engine, device, log) is { } provider) return provider;
        }

        // La CPU, con la sessione normale di ONNX Runtime: c'è sempre.
        var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return new WindowsMlEmbeddingProvider(new InferenceSession(modelPath, so), ComputeEngine.Cpu,
            Lang.T("Windows ML sulla CPU", "Windows ML on the CPU"), batch: 8);
    }

    /// <summary>I dispositivi di un motore, nell'ordine in cui provarli: per la GPU prima DirectML, il più compatibile.</summary>
    private static List<OrtEpDevice> Candidates(IReadOnlyList<OrtEpDevice> devices, ComputeEngine engine)
    {
        var wanted = engine;
        if (engine == ComputeEngine.Npu && Environment.GetEnvironmentVariable(TestNpuVariable) is { Length: > 0 } test)
            wanted = test.Equals("cpu", StringComparison.OrdinalIgnoreCase) ? ComputeEngine.Cpu : ComputeEngine.Gpu;
        return devices.Where(d => DeviceInventory.EngineOf(d) == wanted)
            .OrderBy(d => d.EpName == "DmlExecutionProvider" ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Una sessione su quel dispositivo: prima tutto il modello lì (se una parte finisse sulla CPU la creazione fallisce),
    /// poi, se il dispositivo non sa eseguire tutte le operazioni, con quelle che mancano sulla CPU.
    /// </summary>
    private static WindowsMlEmbeddingProvider? TryCreate(OrtEnv env, string modelPath, ComputeEngine engine, OrtEpDevice device, IProgress<string>? log)
    {
        string what = DeviceInventory.Describe(device);
        string label = engine.ToString().ToUpperInvariant();
        IReadOnlyDictionary<string, long> shapes;
        try { shapes = engine == ComputeEngine.Npu ? StaticShapes(modelPath) : new Dictionary<string, long>(); }
        catch (Exception ex)
        {
            log?.Report(Lang.T($"Rete neurale: modello non leggibile ({ex.Message}).", $"Neural network: model not readable ({ex.Message})."));
            return null;
        }

        // Con la CPU stessa al posto della NPU (solo nelle prove) "tutto sul dispositivo" non ha senso: ONNX Runtime lo rifiuta.
        bool[] attempts = device.EpName == "CPUExecutionProvider" ? [false] : [true, false];
        foreach (bool whole in attempts)
        {
            WindowsMlEmbeddingProvider? provider = null;
            CrashGuard.Begin(device);
            try
            {
                using var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                foreach (var (name, value) in shapes) so.AddFreeDimensionOverrideByName(name, value);
                if (whole) so.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
                if (device.EpName == "DmlExecutionProvider")
                {
                    so.EnableMemoryPattern = false; // DirectML vuole così
                    so.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                }
                so.AppendExecutionProvider(env, [device], new Dictionary<string, string>());
                var session = new InferenceSession(modelPath, so);
                string description = whole
                    ? Lang.T($"Windows ML sulla {label}: {what}", $"Windows ML on the {label}: {what}")
                    : Lang.T($"Windows ML sulla {label}: {what}, con alcune operazioni sulla CPU",
                             $"Windows ML on the {label}: {what}, with some operations on the CPU");
                int batch = engine == ComputeEngine.Npu ? 1 : 16;
                provider = new WindowsMlEmbeddingProvider(session, engine, description, batch);
                provider.WarmUp(); // una foto finta: un dispositivo che sa creare la sessione ma poi non calcola, si scarta subito
                return provider;
            }
            catch (Exception ex)
            {
                provider?.Dispose();
                log?.Report(whole
                    ? Lang.T($"La {label} ({what}) non esegue tutto il modello ({ex.Message}): provo con una parte sulla CPU.",
                             $"The {label} ({what}) cannot run the whole model ({ex.Message}): trying with part of it on the CPU.")
                    : Lang.T($"La {label} ({what}) non accetta il modello: {ex.Message}", $"The {label} ({what}) does not accept the model: {ex.Message}"));
            }
            finally
            {
                CrashGuard.End();
            }
        }
        return null;
    }

    /// <summary>Un calcolo su un'immagine tutta nera: se il dispositivo non sa davvero eseguire il modello, lo si vede qui.</summary>
    private void WarmUp()
    {
        var input = new DenseTensor<float>(new float[3 * _side * _side], [1, 3, _side, _side]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
        _ = results.First().AsTensor<float>().Length;
    }

    /// <summary>
    /// Le dimensioni variabili degli ingressi, rese fisse: il lotto a 1 (una foto alla volta), i canali a 3, il lato a 224.
    /// Le NPU compilano il modello per forme fisse; con un lotto variabile lo rifiutano e tutto finisce sulla CPU.
    /// </summary>
    internal static IReadOnlyDictionary<string, long> StaticShapes(string modelPath)
    {
        using var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL };
        using var probe = new InferenceSession(modelPath, so);
        var shapes = new Dictionary<string, long>();
        foreach (var input in probe.InputMetadata.Values)
        {
            var dims = input.Dimensions;
            var names = input.SymbolicDimensions;
            for (int i = 0; i < dims.Length && i < names.Length; i++)
                if (dims[i] < 0 && !string.IsNullOrEmpty(names[i]))
                    shapes.TryAdd(names[i], i == 0 ? 1 : dims.Length == 4 && i == 1 ? 3 : 224);
        }
        return shapes;
    }

    /// <summary>Scarica (se serve) e registra gli execution provider certificati per questo PC.</summary>
    public static async Task RegisterCertifiedProvidersAsync(IProgress<string>? log = null)
    {
        try
        {
            var catalog = ExecutionProviderCatalog.GetDefault();
            await catalog.EnsureAndRegisterCertifiedAsync();
        }
        catch (Exception ex)
        {
            // Windows precedente a 24H2, rete assente, criteri aziendali: si prosegue con DirectML/CPU.
            log?.Report(Lang.T($"Provider certificati Windows ML non disponibili ({ex.Message}); uso DirectML/CPU.",
                               $"Certified Windows ML providers not available ({ex.Message}); using DirectML/CPU."));
        }
    }

    /// <summary>Elenco leggibile dei dispositivi di calcolo visti da ONNX Runtime (NPU, GPU, CPU).</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        try
        {
            return DeviceInventory.Devices().Select(d =>
                $"{d.HardwareDevice.Type} {DeviceInventory.Describe(d)} ({d.HardwareDevice.VendorId:x4}:{d.HardwareDevice.DeviceId:x4})" +
                (DeviceInventory.IsSoftwareAdapter(d) ? Lang.T(", adattatore software: non usato", ", software adapter: not used") : "")).Distinct().ToList();
        }
        catch (Exception ex)
        {
            return [Lang.T($"elenco non disponibile: {ex.Message}", $"list not available: {ex.Message}")];
        }
    }

    public float[][] Embed(IReadOnlyList<RgbImage> images)
    {
        if (images.Count == 0) return [];
        if (_batchUnsupported || images.Count == 1)
            return images.Select(i => RunBatch([i])[0]).ToArray();
        try
        {
            return RunBatch(images);
        }
        catch (OnnxRuntimeException)
        {
            _batchUnsupported = true; // alcuni provider accettano solo batch 1: si ripiega una volta per tutte
            return images.Select(i => RunBatch([i])[0]).ToArray();
        }
    }

    private float[][] RunBatch(IReadOnlyList<RgbImage> images)
    {
        int n = images.Count, plane = _side * _side;
        var data = new float[n * 3 * plane];
        Parallel.For(0, n, i => Preprocess(images[i], data.AsSpan(i * 3 * plane, 3 * plane), _side));

        var input = new DenseTensor<float>(data, [n, 3, _side, _side]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions.ToArray();

        // Accetta sia [n, d] (pooler/embedding) sia [n, token, d] (last_hidden_state: si usa il token CLS).
        int d = dims[^1];
        var vectors = new float[n][];
        for (int i = 0; i < n; i++)
        {
            var v = new float[d];
            for (int k = 0; k < d; k++)
                v[k] = dims.Length == 3 ? output[i, 0, k] : output[i, k];
            Normalize(v);
            vectors[i] = v;
        }
        return vectors;
    }

    /// <summary>Ridimensiona il lato corto a <paramref name="side"/>, ritaglia al centro, normalizza in CHW.</summary>
    internal static void Preprocess(RgbImage img, Span<float> dst, int side)
    {
        int plane = side * side;
        double scale = Math.Min(img.Width, img.Height) / (double)side;
        double ox = (img.Width - side * scale) / 2, oy = (img.Height - side * scale) / 2;
        var p = img.Pixels;
        int w = img.Width, h = img.Height;

        for (int y = 0; y < side; y++)
        {
            double sy = Math.Clamp(oy + (y + 0.5) * scale - 0.5, 0, h - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, h - 1);
            float fy = (float)(sy - y0);
            for (int x = 0; x < side; x++)
            {
                double sx = Math.Clamp(ox + (x + 0.5) * scale - 0.5, 0, w - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, w - 1);
                float fx = (float)(sx - x0);
                for (int c = 0; c < 3; c++)
                {
                    float a = p[(y0 * w + x0) * 3 + c], b = p[(y0 * w + x1) * 3 + c];
                    float cc = p[(y1 * w + x0) * 3 + c], dd = p[(y1 * w + x1) * 3 + c];
                    float v = (a + (b - a) * fx) * (1 - fy) + (cc + (dd - cc) * fx) * fy;
                    dst[c * plane + y * side + x] = (v / 255f - Mean[c]) / Std[c];
                }
            }
        }
    }

    internal static void Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += x * x;
        float inv = sum > 0 ? (float)(1 / Math.Sqrt(sum)) : 0;
        for (int i = 0; i < v.Length; i++) v[i] *= inv;
    }

    public void Dispose() => _session.Dispose();
}
