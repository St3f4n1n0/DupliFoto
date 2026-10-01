namespace DupliFoto.Core;

/// <summary>Tutte le impostazioni del motore, con valori predefiniti prudenti.</summary>
public sealed class ScanOptions
{
    public List<string> Roots { get; init; } = new();
    public bool Recursive { get; set; } = true;
    public bool IncludeHidden { get; set; }

    /// <summary>Estensioni considerate foto.</summary>
    public HashSet<string> Extensions { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".heic", ".heif", ".avif", ".webp", ".gif", ".bmp",
        ".tif", ".tiff", ".jxl",
        // RAW
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw",
    };

    /// <summary>Estensioni RAW: una coppia RAW+JPEG con lo stesso nome non è mai un doppione.</summary>
    public static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw",
    };

    // --- Rilevamento ---
    /// <summary>Byte letti all'inizio e alla fine del file per l'hash parziale.</summary>
    public int PartialHashBytes { get; set; } = 64 * 1024;
    /// <summary>Lato massimo dell'anteprima usata per hash percettivi e nitidezza.</summary>
    public int ThumbnailSide { get; set; } = 256;
    /// <summary>Distanza di Hamming massima tra pHash per considerare due foto la stessa immagine.</summary>
    public int PerceptualMaxDistance { get; set; } = 8;
    /// <summary>Distanza di Hamming massima tra pHash per candidare due foto a "scatto multiplo".</summary>
    public int BurstMaxDistance { get; set; } = 22;
    /// <summary>Secondi massimi tra due scatti per considerarli della stessa raffica.</summary>
    public double BurstWindowSeconds { get; set; } = 10;
    /// <summary>Somiglianza coseno minima tra embedding neurali per confermare uno scatto multiplo.</summary>
    public double BurstMinCosine { get; set; } = 0.88;
    public bool DetectBursts { get; set; } = true;

    // --- Azioni ---
    public RunMode Mode { get; set; } = RunMode.ReadOnly;
    /// <summary>In modalità automatica, soglia minima di affidabilità per agire senza chiedere.</summary>
    public double AutoThreshold { get; set; } = 99;
    /// <summary>Soglia sotto la quale la modalità automatica non può mai scendere.</summary>
    public const double AutoThresholdFloor = 90;
    public DisposalMethod Disposal { get; set; } = DisposalMethod.Quarantine;
    public string QuarantineRoot { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DupliFoto-Quarantena");
    /// <summary>Cartelle le cui copie vanno sempre tenute: tra due copie vince quella che sta qui.</summary>
    public List<string> PreferredFolders { get; init; } = new();
    /// <summary>
    /// Confronta le foto solo con quelle delle altre cartelle aggiunte ("cosa di B c'è già in A"):
    /// i doppioni dentro la stessa cartella vengono ignorati.
    /// </summary>
    public bool CrossFolderOnly { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public string? CachePath { get; set; }

    public double EffectiveAutoThreshold => Math.Max(AutoThreshold, AutoThresholdFloor);
}
