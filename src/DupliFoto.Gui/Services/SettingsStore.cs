using System.Text.Json;
using System.Text.Json.Serialization;
using DupliFoto.Core;

namespace DupliFoto.Gui.Services;

/// <summary>Le scelte dell'utente, ricordate tra un avvio e l'altro.</summary>
public sealed class GuiSettings
{
    public List<FolderSetting> Folders { get; set; } = new();
    public RunMode Mode { get; set; } = RunMode.ReadOnly;
    public double Threshold { get; set; } = 99;
    public DisposalMethod Disposal { get; set; } = DisposalMethod.Quarantine;
    public string? QuarantineRoot { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public bool CrossFolderOnly { get; set; }
    public bool DetectBursts { get; set; } = true;
    public double BurstSeconds { get; set; } = 10;
    public string? ModelPath { get; set; }
    public string Accelerator { get; set; } = "auto";

    /// <summary>Una cartella; <c>Preferred</c> = le copie che stanno qui si tengono sempre.</summary>
    public sealed record FolderSetting(string Path, bool Preferred);
}

/// <summary>JSON generato in compilazione, senza reflection: funziona anche nell'exe pubblicato con il trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
internal sealed partial class GuiJson : JsonSerializerContext
{
}

public sealed class SettingsStore(string? path)
{
    public static SettingsStore Default => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DupliFoto", "gui.json"));

    public GuiSettings Load()
    {
        try
        {
            if (path is not null && File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), GuiJson.Default.GuiSettings) ?? new GuiSettings();
        }
        catch (Exception) { /* impostazioni illeggibili: si riparte dai valori predefiniti */ }
        return new GuiSettings();
    }

    public void Save(GuiSettings settings)
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, GuiJson.Default.GuiSettings));
        }
        catch (Exception) { /* non poter salvare le preferenze non deve fermare il programma */ }
    }
}
