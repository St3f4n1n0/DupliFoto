using DupliFoto.Core.Matching;

namespace DupliFoto.Core.Scanning;

/// <summary>Livello 0: inventario. Solo nome, peso e data: costo quasi nullo.</summary>
public static class FileScanner
{
    public static List<PhotoFile> Scan(ScanOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var skip = FileAttributes.ReparsePoint | FileAttributes.Offline;
        if (!options.IncludeHidden) skip |= FileAttributes.Hidden | FileAttributes.System;

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = skip,
            ReturnSpecialDirectories = false,
        };

        string quarantine = Normalize(options.QuarantineRoot);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PhotoFile>();

        foreach (var root in options.Roots)
        {
            if (!System.IO.Directory.Exists(root))
            {
                progress?.Report($"Cartella non trovata, ignorata: {root}");
                continue;
            }

            foreach (var path in System.IO.Directory.EnumerateFiles(root, "*", enumeration))
            {
                ct.ThrowIfCancellationRequested();
                if (!options.Extensions.Contains(Path.GetExtension(path))) continue;

                string full = Path.GetFullPath(path);
                // Mai analizzare la quarantena o il cestino: vi sono già i doppioni rimossi.
                if (full.StartsWith(quarantine, StringComparison.OrdinalIgnoreCase)) continue;
                if (full.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(full)) continue; // cartelle radice sovrapposte

                FileInfo fi;
                try { fi = new FileInfo(full); }
                catch (Exception) { continue; }
                if (fi.Length == 0) continue;

                var file = new PhotoFile { Path = full, Size = fi.Length, LastWriteUtc = fi.LastWriteTimeUtc };
                var n = NameNormalizer.Normalize(file.BaseName);
                file.NormalizedName = n.Normalized;
                file.HasCopyMarker = n.HasCopyMarker;
                file.HasEditMarker = n.HasEditMarker;
                result.Add(file);

                if (result.Count % 5000 == 0) progress?.Report($"Trovate {result.Count:N0} foto...");
            }
        }

        progress?.Report($"Inventario completato: {result.Count:N0} foto.");
        return result;
    }

    private static string Normalize(string p)
    {
        var full = Path.GetFullPath(p);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
