namespace DupliFoto.Core.Matching;

/// <summary>
/// Sceglie quale copia tenere in un gruppo.
/// - Doppioni della stessa immagine: vince la qualità del FILE (risoluzione, metadati, nome pulito, cartella preferita).
/// - Scatti multipli: vince la qualità della FOTO (nitidezza), poi la risoluzione.
/// </summary>
public static class KeeperPolicy
{
    public static (PhotoFile Keeper, string Reason) Choose(IReadOnlyList<PhotoFile> files, bool isBurst, ScanOptions options)
    {
        if (files.Count == 0) throw new ArgumentException("Gruppo vuoto", nameof(files));

        var ordered = isBurst
            ? files.OrderByDescending(f => f.Sharpness)
                   .ThenByDescending(f => f.PixelCount)
                   .ThenByDescending(f => f.MetadataRichness)
                   .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            : files.OrderByDescending(f => IsInPreferredFolder(f, options))
                   .ThenByDescending(f => f.PixelCount)
                   .ThenByDescending(f => f.MetadataRichness)
                   .ThenBy(f => f.HasCopyMarker)
                   .ThenBy(f => f.HasEditMarker)
                   .ThenBy(f => f.LastWriteUtc)
                   .ThenBy(f => f.Path.Length)
                   .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase);

        var keeper = ordered.First();
        return (keeper, Explain(keeper, files, isBurst, options));
    }

    private static bool IsInPreferredFolder(PhotoFile f, ScanOptions o) =>
        o.PreferredFolders.Any(p => f.Path.StartsWith(Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));

    private static string Explain(PhotoFile k, IReadOnlyList<PhotoFile> all, bool isBurst, ScanOptions o)
    {
        var others = all.Where(f => !ReferenceEquals(f, k)).ToList();
        if (others.Count == 0) return "";
        if (isBurst && others.All(f => k.Sharpness > f.Sharpness)) return "lo scatto più nitido";
        if (IsInPreferredFolder(k, o) && others.Any(f => !IsInPreferredFolder(f, o))) return "si trova nella cartella preferita";
        if (others.All(f => k.PixelCount > f.PixelCount)) return "risoluzione più alta";
        if (others.All(f => k.MetadataRichness > f.MetadataRichness)) return "metadati più completi (data, GPS...)";
        if (!k.HasCopyMarker && others.Any(f => f.HasCopyMarker)) return "nome originale, senza \"(1)\" o \"Copia\"";
        if (others.All(f => k.LastWriteUtc <= f.LastWriteUtc)) return "la copia più vecchia";
        return "percorso più breve";
    }
}
