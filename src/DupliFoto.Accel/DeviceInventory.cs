using DupliFoto.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace DupliFoto.Accel;

/// <summary>
/// Cosa offre questo PC alla rete neurale, motore per motore (CPU, GPU, NPU), per i pallini nella finestra e per
/// "duplifoto-cli hardware". Senza <c>download</c> non scarica niente: registra solo i componenti di Windows ML già
/// presenti e, per quelli che mancano, dice se Windows li può scaricare alla prima ricerca con il modello.
/// </summary>
public static class DeviceInventory
{
    /// <summary>I componenti di Windows ML che fanno lavorare una NPU (Intel, Qualcomm, AMD).</summary>
    private static readonly string[] NpuProviders = ["OpenVINO", "QNN", "VitisAI"];

    public static async Task<IReadOnlyList<EngineAvailability>> DetectAsync(bool download = false)
    {
        var missing = new List<string>(); // componenti compatibili non ancora scaricati
        try
        {
            _ = OrtEnv.Instance();
            var catalog = ExecutionProviderCatalog.GetDefault();
            foreach (var p in catalog.FindAllProviders())
                if (p.ReadyState != ExecutionProviderReadyState.Ready) missing.Add(p.Name);
            if (download) await catalog.EnsureAndRegisterCertifiedAsync();
            else await catalog.RegisterCertifiedAsync();
        }
        catch (Exception) { /* Windows precedente a 24H2 o criteri aziendali: restano DirectML e CPU */ }

        var devices = Devices();
        return
        [
            new(ComputeEngine.Cpu, true, Cpu()),
            Gpu(devices),
            Npu(devices, missing, PnpDevices.Present("ComputeAccelerator")),
        ];
    }

    /// <summary>I dispositivi che ONNX Runtime sa usare, con il loro componente (execution provider).</summary>
    internal static IReadOnlyList<OrtEpDevice> Devices()
    {
        try { return OrtEnv.Instance().GetEpDevices(); }
        catch (Exception) { return []; }
    }

    internal static ComputeEngine? EngineOf(OrtEpDevice d) => d.HardwareDevice.Type switch
    {
        OrtHardwareDeviceType.CPU => ComputeEngine.Cpu,
        OrtHardwareDeviceType.GPU => ComputeEngine.Gpu,
        OrtHardwareDeviceType.NPU => ComputeEngine.Npu,
        _ => null,
    };

    /// <summary>"Intel via OpenVINOExecutionProvider", "GPU via DmlExecutionProvider".</summary>
    internal static string Describe(OrtEpDevice d) =>
        $"{(string.IsNullOrWhiteSpace(d.HardwareDevice.Vendor) ? d.HardwareDevice.Type.ToString() : d.HardwareDevice.Vendor)} via {d.EpName}";

    private static Text Cpu()
    {
        string name = "CPU";
        try
        {
            if (OperatingSystem.IsWindows() &&
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
                    ?.GetValue("ProcessorNameString") is string n && n.Trim().Length > 0)
                name = n.Trim();
        }
        catch (Exception) { /* resta "CPU" */ }
        return new($"{name}, {Environment.ProcessorCount} thread", $"{name}, {Environment.ProcessorCount} threads");
    }

    private static EngineAvailability Gpu(IReadOnlyList<OrtEpDevice> devices)
    {
        var gpus = devices.Where(d => EngineOf(d) == ComputeEngine.Gpu).Select(Describe).Distinct().ToList();
        string adapters = string.Join(", ", PnpDevices.Present("Display"));
        string name = adapters.Length > 0 ? adapters : "GPU";
        return gpus.Count > 0
            ? new(ComputeEngine.Gpu, true, new($"{name} ({string.Join(", ", gpus)})", $"{name} ({string.Join(", ", gpus)})"))
            : new(ComputeEngine.Gpu, false, new(
                $"Nessuna GPU utilizzabile da Windows ML{(adapters.Length > 0 ? $" ({adapters}: serve DirectX 12)" : "")}",
                $"No GPU that Windows ML can use{(adapters.Length > 0 ? $" ({adapters}: DirectX 12 needed)" : "")}"));
    }

    internal static EngineAvailability Npu(IReadOnlyList<OrtEpDevice> devices, IReadOnlyList<string> missingProviders, IReadOnlyList<string> npuHardware)
    {
        var npus = devices.Where(d => EngineOf(d) == ComputeEngine.Npu).Select(Describe).Distinct().ToList();
        string hardware = string.Join(", ", npuHardware);
        string name = hardware.Length > 0 ? hardware : "NPU";
        if (npus.Count > 0)
            return new(ComputeEngine.Npu, true, new($"{name} ({string.Join(", ", npus)})", $"{name} ({string.Join(", ", npus)})"));

        var toDownload = missingProviders.Where(p => NpuProviders.Any(n => p.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (hardware.Length > 0 && toDownload.Count > 0)
            return new(ComputeEngine.Npu, true, new(
                $"{name}: compatibile. Windows scaricherà {string.Join(", ", toDownload)} alla prima ricerca con il modello",
                $"{name}: compatible. Windows will download {string.Join(", ", toDownload)} at the first search with the model"));
        if (hardware.Length > 0)
            return new(ComputeEngine.Npu, false, new(
                $"{name}: Windows ML non ha un componente per usarla (servono Windows 11 24H2 o successivo e driver aggiornati)",
                $"{name}: Windows ML has no component to use it (Windows 11 24H2 or later and up-to-date drivers are needed)"));
        return new(ComputeEngine.Npu, false, new("Nessuna NPU in questo PC", "No NPU in this PC"));
    }
}
