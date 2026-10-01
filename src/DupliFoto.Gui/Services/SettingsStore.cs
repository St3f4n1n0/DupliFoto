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

/// <summary>
/// Le impostazioni in un file JSON. <paramref name="legacyPath"/> è dove le salvava una versione precedente: si leggono
/// da lì finché non c'è il file nuovo, e al primo salvataggio il vecchio file viene tolto.
/// </summary>
public sealed class SettingsStore(string? path, string? legacyPath = null)
{
    /// <summary>
    /// Nella cartella dei file di lavoro. Le versioni 0.2 e precedenti le tenevano in %APPDATA%\DupliFoto: la versione
    /// portatile parte invece dai valori predefiniti, perché le sue impostazioni viaggiano con l'exe.
    /// </summary>
    public static SettingsStore Default => new(
        System.IO.Path.Combine(AppFiles.Folder, "gui.json"), AppFiles.IsPortable ? null : AppFiles.LegacySettingsPath);

    public GuiSettings Load()
    {
        try
        {
            string? source = File.Exists(path) ? path : File.Exists(legacyPath) ? legacyPath : null;
            if (source is not null)
                return JsonSerializer.Deserialize(File.ReadAllText(source), GuiJson.Default.GuiSettings) ?? new GuiSettings();
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
        catch (Exception) { return; /* non poter salvare le preferenze non deve fermare il programma */ }

        if (legacyPath is null || !File.Exists(legacyPath)) return;
        try
        {
            File.Delete(legacyPath);
            string folder = System.IO.Path.GetDirectoryName(legacyPath)!;
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception) { /* resta il vecchio file: non dà fastidio, e «Pulisci DupliFoto.bat» lo toglie */ }
    }
}
