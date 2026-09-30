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
            log.Report($"Rete neurale: {provider.DeviceDescription}");
            return provider;
        }
        catch (Exception ex)
        {
            log.Report($"Modello non caricato ({ex.Message}): proseguo con i soli algoritmi classici.");
            return null;
        }
#else
        await Task.CompletedTask;
        log.Report("La rete neurale è disponibile solo nella versione per Windows: proseguo con gli algoritmi classici.");
        return null;
#endif
    }
}
