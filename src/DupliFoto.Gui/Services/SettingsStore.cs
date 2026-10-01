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
    /// <summary>Alla chiusura togliere la copia del programma scompattata in %TEMP% (vedi AppFiles.RemoveExtractionAtExit).</summary>
    public bool RemoveTempOnExit { get; set; } = true;

    /// <summary>Una cartella; <c>Preferred</c> = le copie che stanno qui si tengono sempre.</summary>
    public sealed record FolderSetting(string Path, bool Preferred);
}

/// <summary>JSON generato in compilazione, senza reflection: funziona anche nell'exe pubblicato con il trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
internal sealed partial class GuiJson : JsonSerializerContext
{
}

/// <summary>Le impostazioni in un file JSON.</summary>
public sealed class SettingsStore(string? path)
{
    /// <summary>
    /// Nella cartella dei file di lavoro, accanto all'exe. Quelle delle versioni precedenti (in %LOCALAPPDATA% o in
    /// %APPDATA%) le porta qui AppFiles.Prepare, all'avvio.
    /// </summary>
    public static SettingsStore Default => new(System.IO.Path.Combine(AppFiles.Folder, "gui.json"));

    public GuiSettings Load()
    {
        try
        {
            if (File.Exists(path))
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
