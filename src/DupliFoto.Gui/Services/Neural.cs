using DupliFoto.Core;
using DupliFoto.Core.Imaging;

namespace DupliFoto.Gui.Services;

/// <summary>La rete neurale (facoltativa) esiste solo nella versione Windows, con Windows ML.</summary>
public static class Neural
{
    public static bool IsAvailable => OperatingSystem.IsWindows();

    public static async Task<IEmbeddingProvider?> TryCreateAsync(string? modelPath, string accelerator, IProgress<string> log)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return null;
#if WINDOWS
        try
        {
            var preference = accelerator switch
            {
                "npu" => DupliFoto.Accel.AcceleratorPreference.Npu,
                "gpu" => DupliFoto.Accel.AcceleratorPreference.Gpu,
                "cpu" => DupliFoto.Accel.AcceleratorPreference.Cpu,
                _ => DupliFoto.Accel.AcceleratorPreference.Auto,
            };
            var provider = await DupliFoto.Accel.WindowsMlEmbeddingProvider.CreateAsync(modelPath, preference, log);
            log.Report(Lang.T($"Rete neurale: {provider.DeviceDescription}", $"Neural network: {provider.DeviceDescription}"));
            return provider;
        }
        catch (Exception ex)
        {
            log.Report(Lang.T($"Modello non caricato ({ex.Message}): proseguo con i soli algoritmi classici.",
                              $"Model not loaded ({ex.Message}): carrying on with the classic algorithms only."));
            return null;
        }
#else
        await Task.CompletedTask;
        log.Report(Lang.T("La rete neurale è disponibile solo nella versione per Windows: proseguo con gli algoritmi classici.",
                          "The neural network is only available in the Windows version: carrying on with the classic algorithms."));
        return null;
#endif
    }

    /// <summary>
    /// Cosa offre il PC a CPU, GPU e NPU. Non scarica niente: i componenti di Windows ML che mancano li scarica
    /// Windows alla prima ricerca con il modello.
    /// </summary>
    public static async Task<IReadOnlyList<EngineAvailability>> DetectEnginesAsync()
    {
#if WINDOWS
        try { return await Task.Run(() => DupliFoto.Accel.DeviceInventory.DetectAsync(download: false)); }
        catch (Exception) { /* sotto, come senza Windows ML */ }
#else
        await Task.CompletedTask;
#endif
        var onlyWindows = new Text("Solo nella versione per Windows", "Only in the Windows version");
        return
        [
            new(ComputeEngine.Cpu, true, new($"{Environment.ProcessorCount} thread", $"{Environment.ProcessorCount} threads")),
            new(ComputeEngine.Gpu, false, onlyWindows),
            new(ComputeEngine.Npu, false, onlyWindows),
        ];
    }
}
