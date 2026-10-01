using System.IO.Enumeration;
using DupliFoto.Core.Matching;

namespace DupliFoto.Core.Scanning;

/// <summary>Livello 0: inventario. Solo nome, peso e data: costo quasi nullo.</summary>
public static class FileScanner
{
    // Attributi dei file "solo online" (OneDrive e simili): leggerli ne avvierebbe il download.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static List<PhotoFile> Scan(ScanOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Niente ReparsePoint qui: con OneDrive "File su richiesta" TUTTI i file e le cartelle lo sono, anche quelli
        // già scaricati. I veri collegamenti (simbolici e giunzioni) vengono esclusi più sotto.
        var skip = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;
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
        // Con cartelle annidate ("Foto" e "Foto\Catalogate") una foto appartiene alla più specifica.
        var rootsBySpecificity = options.Roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))
                                              .OrderByDescending(r => r.Length).ToList();

        foreach (var root in options.Roots)
        {
            if (!System.IO.Directory.Exists(root))
            {
                progress?.Report($"Cartella non trovata, ignorata: {root}");
                continue;
            }

            var files = new FileSystemEnumerable<string>(root, (ref FileSystemEntry e) => e.ToFullPath(), enumeration)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    !e.IsDirectory && options.Extensions.Contains(Path.GetExtension(e.FileName).ToString()) && !IsLink(ref e),
                // Mai seguire collegamenti simbolici e giunzioni: eviterebbe cicli infiniti e foto contate due volte.
                ShouldRecursePredicate = (ref FileSystemEntry e) => !IsLink(ref e),
            };

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                string full = Path.GetFullPath(path);
                // Mai analizzare la quarantena o il cestino: vi sono già i doppioni rimossi.
                if (full.StartsWith(quarantine, StringComparison.OrdinalIgnoreCase)) continue;
                if (full.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(full)) continue; // cartelle radice sovrapposte

                FileInfo fi;
                try { fi = new FileInfo(full); }
                catch (Exception) { continue; }
                if (fi.Length == 0) continue;

                var file = new PhotoFile
                {
                    Path = full,
                    Size = fi.Length,
                    LastWriteUtc = fi.LastWriteTimeUtc,
                    Root = rootsBySpecificity.First(r => IsUnder(full, r)),
                };
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

    /// <summary>Collegamento simbolico o giunzione (non un segnaposto di OneDrive, che non ha una destinazione).</summary>
    private static bool IsLink(ref FileSystemEntry e)
    {
        if ((e.Attributes & FileAttributes.ReparsePoint) == 0) return false;
        try { return e.ToFileSystemInfo().LinkTarget is not null; }
        catch (Exception) { return true; } // nel dubbio non si segue
    }

    /// <summary>Vero se <paramref name="path"/> sta dentro <paramref name="folder"/> (non basta il prefisso: "D:\Foto" non contiene "D:\Foto2").</summary>
    public static bool IsUnder(string path, string folder)
    {
        string f = Normalize(folder);
        return path.StartsWith(f, StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(f), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stessa cartella, anche se scritta in modo diverso ("D:\Foto" e "d:\foto\").</summary>
    public static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string p)
    {
        var full = Path.GetFullPath(p);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
