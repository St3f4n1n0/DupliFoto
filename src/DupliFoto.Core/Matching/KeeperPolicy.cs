using DupliFoto.Core.Scanning;

namespace DupliFoto.Core.Matching;

/// <summary>
/// Sceglie quale copia tenere in un gruppo.
/// - Doppioni della stessa immagine: prima la cartella da tenere scelta dall'utente, poi la qualità del FILE
///   (risoluzione, metadati, nome pulito).
/// - Scatti multipli: vince la qualità della FOTO (nitidezza), poi la risoluzione.
/// </summary>
public static class KeeperPolicy
{
    public static (PhotoFile Keeper, Text Reason) Choose(IReadOnlyList<PhotoFile> files, bool isBurst, ScanOptions options)
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

    /// <summary>
    /// La copia sta in una cartella da tenere? Con cartelle annidate conta la cartella aggiunta più specifica:
    /// se "Foto" è da tenere ma "Foto\Catalogate" è stata aggiunta a parte, le foto di Catalogate sono di Catalogate.
    /// </summary>
    private static bool IsInPreferredFolder(PhotoFile f, ScanOptions o) =>
        o.PreferredFolders.Any(p => FileScanner.IsUnder(f.Path, p)
                                    && !(f.Root.Length > 0 && FileScanner.IsUnder(f.Root, p) && !FileScanner.SameFolder(f.Root, p)));

    private static Text Explain(PhotoFile k, IReadOnlyList<PhotoFile> all, bool isBurst, ScanOptions o)
    {
        var others = all.Where(f => !ReferenceEquals(f, k)).ToList();
        if (others.Count == 0) return Text.Empty;
        if (isBurst && others.All(f => k.Sharpness > f.Sharpness)) return new("lo scatto più nitido", "the sharpest shot");
        if (IsInPreferredFolder(k, o) && others.Any(f => !IsInPreferredFolder(f, o)))
            return new("si trova nella cartella da tenere", "it is in the folder to keep");
        if (others.All(f => k.PixelCount > f.PixelCount)) return new("risoluzione più alta", "higher resolution");
        if (others.All(f => k.MetadataRichness > f.MetadataRichness))
            return new("metadati più completi (data, GPS...)", "richer metadata (date, GPS...)");
        if (!k.HasCopyMarker && others.Any(f => f.HasCopyMarker))
            return new("nome originale, senza \"(1)\" o \"Copia\"", "original name, without \"(1)\" or \"Copy\"");
        if (others.All(f => k.LastWriteUtc <= f.LastWriteUtc)) return new("la copia più vecchia", "the oldest copy");
        return new("percorso più breve", "shorter path");
    }
}
