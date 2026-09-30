using System.Buffers;
using System.IO.Hashing;

namespace DupliFoto.Core.Matching;

/// <summary>
/// Livello 1: file identici al byte.
/// Imbuto: stesso peso → stesso hash parziale (inizio+fine) → stesso hash completo.
/// Il nome NON è un requisito: una copia rinominata "vacanze.jpg" viene trovata lo stesso.
/// </summary>
public static class ExactMatcher
{
    private const int BufferSize = 1 << 20;

    /// <summary>Restituisce gruppi di file identici (ognuno con almeno 2 file).</summary>
    public static List<List<PhotoFile>> FindExactGroups(
        IReadOnlyList<PhotoFile> files, ScanOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct };

        // 1) Peso identico: gratis, e scarta la gran parte dei file.
        var sameSize = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        progress?.Report($"Stesso peso: {sameSize.Count:N0} file candidati su {files.Count:N0}.");

        // 2) Hash parziale: legge solo 2 × 64 KB per file.
        Parallel.ForEach(sameSize.Where(f => f.PartialHash is null), parallel, f =>
        {
            try { f.PartialHash = ComputePartialHash(f.Path, f.Size, options.PartialHashBytes); }
            catch (Exception ex) { f.AnalysisError = $"Lettura: {ex.Message}"; }
        });

        var samePartial = sameSize
            .Where(f => f.PartialHash is not null)
            .GroupBy(f => (f.Size, f.PartialHash))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        progress?.Report($"Stesso hash parziale: {samePartial.Count:N0} file. Calcolo hash completi...");

        // 3) Hash completo (xxHash128), solo per i sopravvissuti.
        Parallel.ForEach(samePartial.Where(f => f.FullHash is null), parallel, f =>
        {
            try { f.FullHash = ComputeFullHash(f.Path); }
            catch (Exception ex) { f.AnalysisError = $"Lettura: {ex.Message}"; }
        });

        return samePartial
            .Where(f => f.FullHash is not null)
            .GroupBy(f => (f.Size, f.FullHash))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();
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
    /// Confronto byte per byte: l'unica prova davvero al 100%.
    /// Viene eseguito subito prima di ogni azione automatica sui file "identici".
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
