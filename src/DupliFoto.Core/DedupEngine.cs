using System.Collections.Concurrent;
using System.Diagnostics;
using DupliFoto.Core.Imaging;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Scanning;

namespace DupliFoto.Core;

/// <summary>
/// Esegue l'intera analisi a imbuto. Non modifica MAI i file: le azioni sono in <see cref="Actions.ActionExecutor"/>.
/// </summary>
public sealed class DedupEngine(IImageDecoder decoder, IMetadataReader metadata, IEmbeddingProvider? embeddings = null)
{
    private readonly ImageAnalyzer _analyzer = new(decoder, metadata);

    public ScanResult Run(ScanOptions o, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = o.MaxDegreeOfParallelism, CancellationToken = ct };

        var cache = new AnalysisCache(o.CachePath);
        cache.Load();

        // ---------- Livello 0: inventario ----------
        var files = FileScanner.Scan(o, progress, ct);
        foreach (var f in files) cache.ApplyTo(f);

        // ---------- Livello 1: identici al byte ----------
        var exactSets = ExactMatcher.FindExactGroups(files, o, progress, ct);
        // Lo stesso file raggiunto da due percorsi (Z:\Foto e \\NAS\Foto, un'unità SUBST, una giunzione) non è una
        // copia: si tiene un percorso solo, altrimenti "spostare il doppione" toglierebbe anche la copia da tenere.
        var aliases = ExactMatcher.RemoveAliases(exactSets, progress);
        if (aliases.Count > 0) files = files.Where(f => !aliases.Contains(f)).ToList();
        var groups = new List<DuplicateGroup>();
        var hiddenDuplicates = new HashSet<PhotoFile>(ReferenceEqualityComparer.Instance);
        var exactKeeperOf = new Dictionary<PhotoFile, DuplicateGroup>(ReferenceEqualityComparer.Instance);

        foreach (var set in exactSets)
        {
            var (keeper, why) = KeeperPolicy.Choose(set, isBurst: false, o);
            var group = new DuplicateGroup
            {
                Keeper = keeper,
                KeeperReason = why,
                Duplicates = set.Where(f => !ReferenceEquals(f, keeper)).Select(f => new GroupMember
                {
                    File = f,
                    Kind = MatchKind.ExactBytes,
                    Confidence = SimilarityScorer.ExactConfidence,
                    Reason = DescribeExact(keeper, f),
                }).ToList(),
            };
            groups.Add(group);
            exactKeeperOf[keeper] = group;
            foreach (var d in group.Duplicates) hiddenDuplicates.Add(d.File);
        }
        progress?.Report($"Livello 1: {groups.Count:N0} gruppi di file identici.");

        // ---------- Analisi visiva (una volta per contenuto distinto) ----------
        var reps = files.Where(f => !hiddenDuplicates.Contains(f) && f.AnalysisError is null).ToList();
        var toAnalyze = reps.Where(f => !f.IsAnalyzed).ToList();
        progress?.Report($"Analisi visiva di {toAnalyze.Count:N0} foto ({reps.Count - toAnalyze.Count:N0} già in cache)...");
        int done = 0;
        Parallel.ForEach(toAnalyze, parallel, f =>
        {
            try { _analyzer.Analyze(f, o.ThumbnailSide); }
            catch (Exception ex) { f.AnalysisError = $"Decodifica: {ex.Message}"; }
            int n = Interlocked.Increment(ref done);
            if (n % 500 == 0) progress?.Report($"  analizzate {n:N0}/{toAnalyze.Count:N0}");
        });

        // I doppioni esatti hanno lo stesso contenuto della copia tenuta: ne ereditano l'analisi (utile per il report).
        foreach (var g in groups)
            foreach (var d in g.Duplicates)
                CopyVisualAnalysis(g.Keeper, d.File);

        var analyzed = reps.Where(f => f.IsAnalyzed).ToList();

        // ---------- Candidati: vicini nell'hash percettivo o nel tempo ----------
        var pairs = FindCandidatePairs(analyzed, o, ct);
        progress?.Report($"Coppie candidate da valutare: {pairs.Count:N0}.");

        // ---------- Livello 2: hash dei pixel, solo dove può fare la differenza ----------
        var pixelCandidates = pairs
            .Where(p => SimilarityScorer.BestDistance(p.A, p.B) is (0, 0) && p.A.Width == p.B.Width && p.A.Height == p.B.Height)
            .SelectMany(p => new[] { p.A, p.B })
            .Where(f => f.PixelHash is null)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<PhotoFile>()
            .ToList();
        if (pixelCandidates.Count > 0)
        {
            progress?.Report($"Livello 2: confronto pixel a piena risoluzione su {pixelCandidates.Count:N0} foto...");
            Parallel.ForEach(pixelCandidates, parallel, f =>
            {
                try { _analyzer.ComputePixelHash(f); }
                catch (Exception) { /* resta senza hash dei pixel: verrà valutata come percettiva */ }
            });
        }

        // ---------- Embedding neurali (NPU/GPU/CPU), solo sulle foto candidate ----------
        if (embeddings is not null)
            ComputeEmbeddings(pairs, progress, ct);

        // ---------- Valutazione delle coppie e formazione dei gruppi ----------
        var matches = new ConcurrentBag<(PhotoFile A, PhotoFile B, SimilarityScorer.Match M)>();
        Parallel.ForEach(pairs, parallel, p =>
        {
            if (SimilarityScorer.Compare(p.A, p.B, o) is { } m) matches.Add((p.A, p.B, m));
        });
        groups.AddRange(BuildSimilarityGroups(analyzed, matches, o));

        // ---------- Ordinamento e numerazione ----------
        var ordered = groups
            .OrderBy(g => g.Kind)
            .ThenByDescending(g => g.Confidence)
            .ThenByDescending(g => g.ReclaimableBytes)
            .ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Id = i + 1;

        // ---------- Cache ----------
        foreach (var f in files.Where(f => f.PartialHash is not null || f.IsAnalyzed)) cache.Store(f);
        cache.Prune(o.Roots, files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
        try { cache.Save(); } catch (Exception ex) { progress?.Report($"Cache non salvata: {ex.Message}"); }

        progress?.Report($"Analisi completata in {sw.Elapsed:mm\\:ss}.");
        return new ScanResult
        {
            Files = files,
            Groups = ordered,
            Elapsed = sw.Elapsed,
            AcceleratorDescription = embeddings?.DeviceDescription ?? "Nessun modello neurale (solo algoritmi classici)",
        };
    }

    // ------------------------------------------------------------------

    internal readonly record struct Pair(PhotoFile A, PhotoFile B);

    internal static List<Pair> FindCandidatePairs(IReadOnlyList<PhotoFile> analyzed, ScanOptions o, CancellationToken ct)
    {
        var index = new Dictionary<PhotoFile, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < analyzed.Count; i++) index[analyzed[i]] = i;
        var seen = new HashSet<(int, int)>();
        var pairs = new List<Pair>();

        void Add(PhotoFile a, PhotoFile b)
        {
            int ia = index[a], ib = index[b];
            if (ia == ib) return;
            var key = ia < ib ? (ia, ib) : (ib, ia);
            if (seen.Add(key)) pairs.Add(ia < ib ? new Pair(a, b) : new Pair(b, a));
        }

        // Vicini nell'hash percettivo, in tutte le 8 orientazioni (BK-tree).
        var tree = new BkTree<PhotoFile>();
        foreach (var f in analyzed) tree.Add(f.PHash!.Value, f);
        foreach (var f in analyzed)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var variant in f.PHashVariants!.Distinct())
                foreach (var (other, _) in tree.Query(variant, o.PerceptualMaxDistance))
                    Add(f, other);
        }

        // Vicini nel tempo con la stessa fotocamera (scatti multipli). Ordinare per data rende il costo O(n log n).
        if (o.DetectBursts)
        {
            const int maxNeighbours = 60; // protezione contro migliaia di foto con la stessa data fittizia
            var timed = analyzed.Where(f => f.TakenAt is not null).OrderBy(f => f.TakenAt).ToList();
            for (int i = 0; i < timed.Count; i++)
            {
                for (int j = i + 1; j < timed.Count && j <= i + maxNeighbours; j++)
                {
                    if ((timed[j].TakenAt!.Value - timed[i].TakenAt!.Value).TotalSeconds > o.BurstWindowSeconds) break;
                    if (timed[i].CameraKey == timed[j].CameraKey) Add(timed[i], timed[j]);
                }
            }
        }
        return pairs;
    }

    private void ComputeEmbeddings(List<Pair> pairs, IProgress<string>? progress, CancellationToken ct)
    {
        if (embeddings is not { } provider) return;
        var targets = pairs.SelectMany(p => new[] { p.A, p.B })
            .Where(f => f.Embedding is null)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<PhotoFile>()
            .ToList();
        if (targets.Count == 0) return;

        progress?.Report($"Embedding neurali su {targets.Count:N0} foto con {provider.DeviceDescription}...");
        int batch = Math.Max(1, provider.PreferredBatchSize);
        for (int start = 0; start < targets.Count; start += batch)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = targets.Skip(start).Take(batch).ToList();
            var images = new RgbImage?[chunk.Count];
            Parallel.For(0, chunk.Count, i =>
            {
                try { images[i] = _analyzer.DecodeForEmbedding(chunk[i], 256); }
                catch (Exception) { images[i] = null; }
            });

            var ok = Enumerable.Range(0, chunk.Count).Where(i => images[i] is not null).ToList();
            if (ok.Count == 0) continue;
            try
            {
                var vectors = provider.Embed(ok.Select(i => images[i]!).ToList());
                for (int k = 0; k < ok.Count; k++) chunk[ok[k]].Embedding = vectors[k];
            }
            catch (Exception ex)
            {
                progress?.Report($"Embedding non disponibili ({ex.Message}): proseguo con i soli algoritmi classici.");
                return;
            }
        }
    }

    internal static List<DuplicateGroup> BuildSimilarityGroups(
        IReadOnlyList<PhotoFile> analyzed,
        IEnumerable<(PhotoFile A, PhotoFile B, SimilarityScorer.Match M)> matches,
        ScanOptions o)
    {
        var index = new Dictionary<PhotoFile, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < analyzed.Count; i++) index[analyzed[i]] = i;
        var parent = Enumerable.Range(0, analyzed.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        var matchList = matches.ToList();
        foreach (var (a, b, _) in matchList) parent[Find(index[a])] = Find(index[b]);

        var burstInCluster = new HashSet<int>();
        foreach (var (a, _, m) in matchList)
            if (m.Kind == MatchKind.Burst) burstInCluster.Add(Find(index[a]));

        var result = new List<DuplicateGroup>();
        foreach (var cluster in matchList.SelectMany(t => new[] { t.A, t.B })
                     .Distinct(ReferenceEqualityComparer.Instance).Cast<PhotoFile>()
                     .GroupBy(f => Find(index[f])))
        {
            var members = cluster.ToList();
            if (members.Count < 2) continue;
            bool isBurst = burstInCluster.Contains(cluster.Key);
            var (keeper, why) = KeeperPolicy.Choose(members, isBurst, o);

            var dups = new List<GroupMember>();
            foreach (var f in members.Where(f => !ReferenceEquals(f, keeper)))
            {
                // L'affidabilità si misura SEMPRE rispetto alla copia tenuta, non lungo una catena A~B~C.
                if (SimilarityScorer.Compare(keeper, f, o) is { } m)
                    dups.Add(new GroupMember { File = f, Kind = m.Kind, Confidence = m.Confidence, Reason = m.Reason });
                else
                    dups.Add(new GroupMember
                    {
                        File = f,
                        Kind = MatchKind.Burst,
                        Confidence = 50,
                        Reason = "simile ad altre foto del gruppo, ma non direttamente a quella da tenere",
                    });
            }
            result.Add(new DuplicateGroup { Keeper = keeper, KeeperReason = why, Duplicates = dups });
        }
        return result;
    }

    private static string DescribeExact(PhotoFile keeper, PhotoFile dup)
    {
        if (dup.NormalizedName == keeper.NormalizedName)
            return dup.HasCopyMarker ? "copia identica (nome con \"(1)\"/\"Copia\")" : "copia identica, stesso nome";
        return "copia identica con nome diverso";
    }

    private static void CopyVisualAnalysis(PhotoFile from, PhotoFile to)
    {
        to.Width = from.Width;
        to.Height = from.Height;
        to.PHashVariants = from.PHashVariants;
        to.DHash = from.DHash;
        to.Sharpness = from.Sharpness;
        to.TakenAt ??= from.TakenAt;
        to.CameraMake ??= from.CameraMake;
        to.CameraModel ??= from.CameraModel;
        to.Latitude ??= from.Latitude;
        to.Longitude ??= from.Longitude;
        to.MetadataRichness = from.MetadataRichness;
    }
}
