using System.Buffers;
using System.IO.Hashing;
using DupliFoto.Core.Scanning;

namespace DupliFoto.Core.Matching;

/// <summary>
/// Livello 1: file identici al byte.
/// Imbuto: stesso peso → stesso hash parziale (inizio+fine) → stesso hash completo.
/// Il nome NON è un requisito: una copia rinominata "vacanze.jpg" viene trovata lo stesso.
/// I video passano dallo stesso imbuto, mai insieme alle foto; con la verifica rapida l'ultimo passo è l'hash di
/// 16 blocchi sparsi sul file invece che di tutto il file (vedi <see cref="VideoCheck"/>).
/// </summary>
public static class ExactMatcher
{
    private const int BufferSize = 1 << 20;

    /// <summary>Restituisce gruppi di file identici (ognuno con almeno 2 file).</summary>
    public static List<List<PhotoFile>> FindExactGroups(
        IReadOnlyList<PhotoFile> files, ScanOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct };

        // 1) Peso identico: gratis, e scarta la gran parte dei file. Una foto e un video non sono mai copie.
        var sameSize = files.GroupBy(f => (f.Size, f.IsVideo)).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        progress?.Report(Lang.T($"Stesso peso: {sameSize.Count:N0} file candidati su {files.Count:N0}.", $"Same size: {sameSize.Count:N0} candidate files out of {files.Count:N0}."));

        // 2) Hash parziale: legge solo 2 × 64 KB per file.
        var toPartial = sameSize.Where(f => f.PartialHash is null).ToList();
        var partial = new ProgressCounter(progress, toPartial.Count, (done, total, _) =>
            Lang.T($"Hash parziali: {done:N0} di {total:N0} file...", $"Partial hashes: {done:N0} of {total:N0} files..."));
        Parallel.ForEach(toPartial, parallel, f =>
        {
            try { f.PartialHash = ComputePartialHash(f.Path, f.Size, options.PartialHashBytes); }
            catch (Exception ex) { f.AnalysisError = Lang.T($"Lettura: {ex.Message}", $"Reading: {ex.Message}"); }
            partial.Add();
        });
        partial.Finish();

        var survivors = sameSize
            .Where(f => f.PartialHash is not null)
            .GroupBy(f => (f.Size, f.PartialHash, f.IsVideo))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        // Video con la verifica rapida: 16 blocchi sparsi invece del file intero.
        bool quickVideos = options.VideoCheck == VideoCheck.Quick;
        var sampled = quickVideos ? survivors.Where(f => f.IsVideo).ToList() : [];
        var samePartial = quickVideos ? survivors.Where(f => !f.IsVideo).ToList() : survivors;
        progress?.Report(Lang.T($"Stesso hash parziale: {survivors.Count:N0} file. Calcolo hash completi...", $"Same partial hash: {survivors.Count:N0} files. Computing full hashes..."));

        var exact = new List<List<PhotoFile>>();
        if (sampled.Count > 0)
        {
            var sampling = new ProgressCounter(progress, sampled.Count, (done, total, _) =>
                Lang.T($"Video, verifica rapida: {done:N0} di {total:N0}...", $"Videos, quick check: {done:N0} of {total:N0}..."));
            Parallel.ForEach(sampled, parallel, f =>
            {
                try { f.SampleHash = ComputeSampleHash(f.Path, f.Size, options.PartialHashBytes, ScanOptions.VideoSampleBlocks); }
                catch (Exception ex) { f.AnalysisError = Lang.T($"Lettura: {ex.Message}", $"Reading: {ex.Message}"); }
                sampling.Add();
            });
            sampling.Finish();
            exact.AddRange(sampled.Where(f => f.SampleHash is not null)
                .GroupBy(f => (f.Size, f.SampleHash))
                .Where(g => g.Count() > 1)
                .Select(g => g.ToList()));
        }

        // 3) Hash completo (xxHash128), solo per i sopravvissuti: legge i file interi, è la parte lunga.
        var toFull = samePartial.Where(f => f.FullHash is null).ToList();
        string totalSize = Reporting.ReportWriter.FormatBytes(toFull.Sum(f => f.Size));
        var full = new ProgressCounter(progress, toFull.Count, (done, total, read) =>
            Lang.T($"Hash completi: {done:N0} di {total:N0} file ({Reporting.ReportWriter.FormatBytes(read)} di {totalSize})...",
                   $"Full hashes: {done:N0} of {total:N0} files ({Reporting.ReportWriter.FormatBytes(read)} of {totalSize})..."));
        Parallel.ForEach(toFull, parallel, f =>
        {
            try { f.FullHash = ComputeFullHash(f.Path); }
            catch (Exception ex) { f.AnalysisError = Lang.T($"Lettura: {ex.Message}", $"Reading: {ex.Message}"); }
            full.Add(f.Size);
        });
        full.Finish();

        exact.AddRange(samePartial
            .Where(f => f.FullHash is not null)
            .GroupBy(f => (f.Size, f.FullHash, f.IsVideo))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList()));
        return exact;
    }

    /// <summary>
    /// Toglie dai gruppi, e restituisce, i percorsi che portano a un file già presente nello stesso gruppo:
    /// lo stesso file raggiunto per due strade (vedi <see cref="FileIdentity"/>) non è una copia.
    /// </summary>
    public static HashSet<PhotoFile> RemoveAliases(List<List<PhotoFile>> groups, IProgress<string>? progress = null)
    {
        var aliases = new HashSet<PhotoFile>(ReferenceEqualityComparer.Instance);
        foreach (var group in groups)
        {
            var ids = group.Select(f => FileIdentity.TryGetId(f.Path)).ToList();
            for (int i = 1; i < group.Count; i++)
            {
                if (ids[i] is not { } id) continue;
                int first = ids.FindIndex(0, i, other => other == id);
                if (first < 0) continue;
                aliases.Add(group[i]);
                progress?.Report(Lang.T($"Stesso file raggiunto da due percorsi, contato una volta sola: {group[i].Path} = {group[first].Path}",
                    $"Same file reached through two paths, counted once: {group[i].Path} = {group[first].Path}"));
            }
            group.RemoveAll(aliases.Contains);
        }
        groups.RemoveAll(g => g.Count < 2);
        return aliases;
    }

    public static ulong ComputePartialHash(string path, long size, int chunk)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var hasher = new XxHash3();
        var buffer = ArrayPool<byte>.Shared.Rent(chunk);
        try
        {
            int read = fs.ReadAtLeast(buffer.AsSpan(0, (int)Math.Min(chunk, size)), (int)Math.Min(chunk, size), throwOnEndOfStream: false);
            hasher.Append(buffer.AsSpan(0, read));
            if (size > 2L * chunk)
            {
                fs.Seek(-chunk, SeekOrigin.End);
                read = fs.ReadAtLeast(buffer.AsSpan(0, chunk), chunk, throwOnEndOfStream: false);
                hasher.Append(buffer.AsSpan(0, read));
            }
            else if (size > chunk)
            {
                read = fs.ReadAtLeast(buffer.AsSpan(0, (int)(size - chunk)), (int)(size - chunk), throwOnEndOfStream: false);
                hasher.Append(buffer.AsSpan(0, read));
            }
            return hasher.GetCurrentHashAsUInt64();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static UInt128 ComputeFullHash(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        var hasher = new XxHash128();
        hasher.Append(fs);
        return hasher.GetCurrentHashAsUInt128();
    }

    /// <summary>
    /// Confronto rapido, byte per byte, dei soli primi e ultimi <paramref name="chunk"/> byte dei due file (gli stessi
    /// dell'hash parziale): è lì che stanno i metadati (EXIF), che un programma può riscrivere lasciando uguali peso e
    /// data. È il controllo che si fa subito prima di spostare, se non è attiva la verifica completa (<see cref="FilesAreIdentical"/>).
    /// </summary>
    public static bool HeadAndTailAreIdentical(string a, string b, int chunk) => BlocksAreIdentical(a, b, chunk, 2);

    /// <summary>
    /// I blocchi da leggere: <paramref name="blocks"/> blocchi da <paramref name="chunk"/> byte, il primo all'inizio,
    /// l'ultimo alla fine e gli altri sparsi a distanze uguali. Un file che non è più grande di tutti i blocchi insieme
    /// si legge per intero, in un blocco solo.
    /// </summary>
    internal static IEnumerable<(long Offset, int Count)> SampleBlocks(long size, int chunk, int blocks)
    {
        if (size <= (long)chunk * blocks)
        {
            yield return (0, (int)size);
            yield break;
        }
        for (int i = 0; i < blocks; i++)
            yield return ((long)((double)(size - chunk) * i / (blocks - 1)), chunk);
    }

    /// <summary>Video, verifica rapida: hash del peso e dei <paramref name="blocks"/> blocchi sparsi sul file.</summary>
    public static UInt128 ComputeSampleHash(string path, long size, int chunk, int blocks)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        var hasher = new XxHash128();
        hasher.Append(BitConverter.GetBytes(size));
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(size, (long)chunk * blocks) + 1);
        try
        {
            foreach (var (offset, count) in SampleBlocks(size, chunk, blocks))
            {
                fs.Position = offset;
                int read = fs.ReadAtLeast(buffer.AsSpan(0, count), count, throwOnEndOfStream: false);
                hasher.Append(buffer.AsSpan(0, read));
            }
            return hasher.GetCurrentHashAsUInt128();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Confronto byte per byte dei soli blocchi di <see cref="SampleBlocks"/>: 2 blocchi sono inizio e fine (il controllo
    /// rapido delle foto), 16 la verifica rapida dei video.
    /// </summary>
    public static bool BlocksAreIdentical(string a, string b, int chunk, int blocks)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        long size = fa.Length;

        using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        int max = (int)Math.Min(size, (long)chunk * blocks) + 1;
        var ba = ArrayPool<byte>.Shared.Rent(max);
        var bb = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            foreach (var (offset, count) in SampleBlocks(size, chunk, blocks))
            {
                sa.Position = offset;
                sb.Position = offset;
                int ra = sa.ReadAtLeast(ba.AsSpan(0, count), count, throwOnEndOfStream: false);
                int rb = sb.ReadAtLeast(bb.AsSpan(0, count), count, throwOnEndOfStream: false);
                if (ra != count || rb != count || !ba.AsSpan(0, count).SequenceEqual(bb.AsSpan(0, count))) return false;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ba);
            ArrayPool<byte>.Shared.Return(bb);
        }
    }

    /// <summary>
    /// Confronto byte per byte: l'unica prova davvero al 100%.
    /// Viene eseguito subito prima di ogni spostamento dei file "identici", se è attiva la verifica completa.
    /// </summary>
    public static bool FilesAreIdentical(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;

        using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        var ba = ArrayPool<byte>.Shared.Rent(BufferSize);
        var bb = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                int ra = sa.ReadAtLeast(ba.AsSpan(0, BufferSize), BufferSize, throwOnEndOfStream: false);
                int rb = sb.ReadAtLeast(bb.AsSpan(0, BufferSize), BufferSize, throwOnEndOfStream: false);
                if (ra != rb) return false;
                if (ra == 0) return true;
                if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ba);
            ArrayPool<byte>.Shared.Return(bb);
        }
    }
}
