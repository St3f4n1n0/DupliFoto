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
/// per qualunque GPU DirectX 12. ONNX Runtime poi sceglie il dispositivo secondo la preferenza;
/// se nessun acceleratore è disponibile si ricade sulla CPU, senza errori.
/// </summary>
public sealed class WindowsMlEmbeddingProvider : IEmbeddingProvider
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f]; // normalizzazione ImageNet
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _side;
    private bool _batchUnsupported;

    public string DeviceDescription { get; }
    public int PreferredBatchSize { get; }

    private WindowsMlEmbeddingProvider(InferenceSession session, string description, int batch)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
        var dims = session.InputMetadata[_inputName].Dimensions; // es. [-1, 3, 224, 224]
        _side = dims.Length == 4 && dims[3] > 0 ? dims[3] : 224;
        _batchUnsupported = dims.Length == 4 && dims[0] == 1;
        DeviceDescription = description;
        PreferredBatchSize = _batchUnsupported ? 1 : batch;
    }

    public static async Task<WindowsMlEmbeddingProvider> CreateAsync(
        string modelPath, AcceleratorPreference preference, IProgress<string>? log = null)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Modello ONNX non trovato", modelPath);

        _ = OrtEnv.Instance(); // l'ambiente ONNX Runtime deve esistere prima di registrare i provider
        await RegisterCertifiedProvidersAsync(log);

        var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        so.SetEpSelectionPolicy(preference switch
        {
            AcceleratorPreference.Npu => ExecutionProviderDevicePolicy.PREFER_NPU,
            AcceleratorPreference.Gpu => ExecutionProviderDevicePolicy.PREFER_GPU,
            AcceleratorPreference.Cpu => ExecutionProviderDevicePolicy.PREFER_CPU,
            _ => ExecutionProviderDevicePolicy.MAX_PERFORMANCE,
        });

        var session = new InferenceSession(modelPath, so);
        string devices = string.Join(", ", ListDevices());
        string description = $"Windows ML ({PreferenceLabel(preference)}; disponibili: {devices})";
        // Le NPU lavorano quasi sempre con forme statiche: una immagine alla volta è la scelta più sicura.
        int batch = preference == AcceleratorPreference.Npu ? 1 : preference == AcceleratorPreference.Cpu ? 8 : 16;
        return new WindowsMlEmbeddingProvider(session, description, batch);
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
            log?.Report($"Provider certificati Windows ML non disponibili ({ex.Message}); uso DirectML/CPU.");
        }
    }

    /// <summary>Elenco leggibile dei dispositivi di calcolo visti da ONNX Runtime (NPU, GPU, CPU).</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        try
        {
            return OrtEnv.Instance().GetEpDevices()
                .Select(d => $"{d.HardwareDevice.Type} {d.HardwareDevice.Vendor} via {d.EpName}")
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            return [$"elenco non disponibile: {ex.Message}"];
        }
    }

    private static string PreferenceLabel(AcceleratorPreference p) => p switch
    {
        AcceleratorPreference.Npu => "preferenza NPU",
        AcceleratorPreference.Gpu => "preferenza GPU",
        AcceleratorPreference.Cpu => "solo CPU",
        _ => "massime prestazioni",
    };

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
