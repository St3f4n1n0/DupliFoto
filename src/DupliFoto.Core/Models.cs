namespace DupliFoto.Core;

/// <summary>Tipo di corrispondenza trovata, dal più certo al meno certo.</summary>
public enum MatchKind
{
    /// <summary>File identici byte per byte (stesso peso, stesso hash completo).</summary>
    ExactBytes = 0,
    /// <summary>Stessi pixel decodificati, file diversi (es. metadati EXIF modificati).</summary>
    IdenticalPixels = 1,
    /// <summary>Stessa immagine ricompressa, ridimensionata o ruotata (hash percettivo).</summary>
    Perceptual = 2,
    /// <summary>Scatti multipli della stessa scena (raffica, doppio scatto).</summary>
    Burst = 3,
}

/// <summary>Modalità operative del programma.</summary>
public enum RunMode
{
    /// <summary>Analisi pura: nessun file viene toccato, solo report.</summary>
    ReadOnly,
    /// <summary>Ogni azione viene chiesta all'utente.</summary>
    Assisted,
    /// <summary>Agisce da solo solo sui file identici al 100% (verificati byte per byte); il resto lo chiede.</summary>
    SemiAutomatic,
    /// <summary>Agisce da solo fino alla soglia di affidabilità impostata (minimo 90%).</summary>
    Automatic,
}

/// <summary>Cosa fare dei doppioni. La cancellazione definitiva volutamente non esiste.</summary>
public enum DisposalMethod
{
    /// <summary>Sposta in una cartella di quarantena, con registro per annullare.</summary>
    Quarantine,
    /// <summary>Sposta nel Cestino di Windows.</summary>
    RecycleBin,
}

/// <summary>Una foto trovata sul disco, con tutto ciò che il motore ne ha ricavato.</summary>
public sealed class PhotoFile
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }

    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    public string BaseName => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    // --- Nome ---
    public string NormalizedName { get; set; } = "";
    public bool HasCopyMarker { get; set; }
    public bool HasEditMarker { get; set; }

    // --- Livello 1: identità dei byte ---
    public ulong? PartialHash { get; set; }
    public UInt128? FullHash { get; set; }

    // --- Livello 2-4: contenuto visivo ---
    public int Width { get; set; }
    public int Height { get; set; }
    public long PixelCount => (long)Width * Height;
    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;
    /// <summary>pHash nelle 8 orientazioni (indice 0 = orientamento originale).</summary>
    public ulong[]? PHashVariants { get; set; }
    public ulong? PHash => PHashVariants is { Length: > 0 } v ? v[0] : null;
    public ulong? DHash { get; set; }
    /// <summary>Hash xxHash128 dei soli pixel decodificati (calcolato solo sui candidati).</summary>
    public UInt128? PixelHash { get; set; }
    /// <summary>Varianza del Laplaciano sull'anteprima: più alta = più nitida.</summary>
    public double Sharpness { get; set; }
    /// <summary>Embedding neurale normalizzato L2 (solo se è configurato un modello).</summary>
    public float[]? Embedding { get; set; }

    // --- Metadati ---
    public DateTime? TakenAt { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Quanti metadati utili contiene (data, fotocamera, GPS...). Serve a scegliere la copia da tenere.</summary>
    public int MetadataRichness { get; set; }

    public string? AnalysisError { get; set; }
    public bool IsAnalyzed => PHashVariants is not null;

    public string CameraKey => $"{CameraMake}|{CameraModel}".Trim('|');

    public override string ToString() => Path;
}

/// <summary>Un membro di un gruppo, con l'affidabilità misurata rispetto alla copia da tenere.</summary>
public sealed class GroupMember
{
    public required PhotoFile File { get; init; }
    public required MatchKind Kind { get; init; }
    /// <summary>Affidabilità 0-100 che questo file sia un doppione della copia da tenere.</summary>
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Un gruppo di doppioni: una copia da tenere e uno o più candidati da rimuovere.</summary>
public sealed class DuplicateGroup
{
    public int Id { get; set; }
    public required PhotoFile Keeper { get; set; }
    public required List<GroupMember> Duplicates { get; init; }
    public string KeeperReason { get; set; } = "";

    /// <summary>Il tipo "peggiore" del gruppo (il meno certo).</summary>
    public MatchKind Kind => Duplicates.Count == 0 ? MatchKind.ExactBytes : Duplicates.Max(d => d.Kind);
    /// <summary>L'affidabilità minima del gruppo.</summary>
    public double Confidence => Duplicates.Count == 0 ? 0 : Duplicates.Min(d => d.Confidence);
    public long ReclaimableBytes => Duplicates.Sum(d => d.File.Size);
    public IEnumerable<PhotoFile> AllFiles => Duplicates.Select(d => d.File).Prepend(Keeper);
}

/// <summary>Risultato completo di un'analisi.</summary>
public sealed class ScanResult
{
    public required IReadOnlyList<PhotoFile> Files { get; init; }
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public string AcceleratorDescription { get; init; } = "Nessun modello neurale (solo algoritmi classici)";
    public int UnreadableFiles => Files.Count(f => f.AnalysisError is not null);
}
