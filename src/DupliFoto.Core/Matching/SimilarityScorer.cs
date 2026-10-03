using DupliFoto.Core.Imaging;

namespace DupliFoto.Core.Matching;

/// <summary>
/// Decide SE due foto sono doppioni e CON QUALE affidabilità (0-100).
/// Tutte le regole di punteggio stanno qui, in un solo posto, così sono facili da tarare e testare.
///
///   100      identici al byte
///    99      stessi pixel, metadati diversi
///   90-98    stessa immagine ricompressa / ridimensionata / ruotata
///   60-89    scatti multipli della stessa scena
///   ≤ 80     qualunque coppia in cui una delle due è una "versione modificata"
/// </summary>
public static class SimilarityScorer
{
    public readonly record struct Match(MatchKind Kind, double Confidence, Text Reason);

    public const double ExactConfidence = 100;
    public const double PixelConfidence = 99;
    public const double PerceptualMax = 98;
    public const double BurstMax = 89;
    public const double EditedCap = 80;

    public static Match? Compare(PhotoFile a, PhotoFile b, ScanOptions o)
    {
        if (a.FullHash is not null && a.FullHash == b.FullHash && a.Size == b.Size)
            return new Match(MatchKind.ExactBytes, ExactConfidence, new("identici al byte", "byte-identical"));

        if (IsExcludedPair(a, b, out _)) return null;

        if (a.PixelHash is not null && a.PixelHash == b.PixelHash)
            return new Match(MatchKind.IdenticalPixels, PixelConfidence, new("stessi pixel, cambiano solo i metadati", "same pixels, only the metadata differ"));

        if (!a.IsAnalyzed || !b.IsAnalyzed) return null;

        var (distance, variant) = BestDistance(a, b);
        double? cosine = Cosine(a.Embedding, b.Embedding);
        Match? best = null;

        // Se entrambe hanno una data di scatto e le date differiscono, sono DUE scatti distinti:
        // possono essere quasi identici all'occhio, ma non sono copie l'uno dell'altro.
        // Le copie vere (ricompresse, inoltrate, ridimensionate) conservano la data originale o la perdono del tutto.
        bool distinctCaptures = a.TakenAt is { } ca && b.TakenAt is { } cb && Math.Abs((ca - cb).TotalSeconds) > 0.05;

        // --- Stessa immagine (ricompressa, ridimensionata, ruotata) ---
        if (distance <= o.PerceptualMaxDistance && !distinctCaptures)
        {
            double c = PerceptualMax - distance;
            var why = new List<Text> { new($"hash percettivo a distanza {distance}/64", $"perceptual hash at distance {distance}/64") };

            if (variant != 0) { c -= 1; why.Add(new("ruotata o specchiata", "rotated or mirrored")); }

            double arA = a.AspectRatio;
            double arB = variant >= 4 && b.AspectRatio > 0 ? 1 / b.AspectRatio : b.AspectRatio; // trasposta = lati scambiati
            if (arA > 0 && arB > 0 && Math.Abs(arA - arB) / Math.Max(arA, arB) > 0.02)
            {
                c -= 10;
                why.Add(new("proporzioni diverse (forse ritagliata)", "different proportions (perhaps cropped)"));
            }

            if (variant == 0 && a.DHash is { } da && b.DHash is { } db && PerceptualHash.Distance(da, db) > 16)
            {
                c -= 6;
                why.Add(new("dHash discordante", "dHash disagrees"));
            }

            if (cosine is { } cs && cs < 0.90)
            {
                c -= 10;
                why.Add(new($"la rete neurale vede differenze (somiglianza {cs:P0})", $"the neural network sees differences ({cs:P0} similar)"));
            }

            if (a.NormalizedName == b.NormalizedName)
            {
                c += 1;
                why.Add(new("nome compatibile", "matching name"));
            }

            best = new Match(MatchKind.Perceptual, Math.Clamp(c, 50, PerceptualMax), Text.Join(", ", why));
        }

        // --- Scatti multipli della stessa scena ---
        if (o.DetectBursts && a.TakenAt is { } ta && b.TakenAt is { } tb && a.CameraKey == b.CameraKey)
        {
            double dt = Math.Abs((ta - tb).TotalSeconds);
            bool farApart = DistanceMeters(a, b) is > 200;
            bool visuallyClose = distance <= o.BurstMaxDistance || cosine >= o.BurstMinCosine;
            bool neuralVeto = cosine is { } cv && cv < o.BurstMinCosine - 0.08;

            if (dt <= o.BurstWindowSeconds && !farApart && visuallyClose && !neuralVeto)
            {
                double visual = cosine is { } cs2
                    ? Math.Clamp((cs2 - o.BurstMinCosine) / (1 - o.BurstMinCosine), 0, 1)
                    : Math.Clamp(1 - (double)distance / o.BurstMaxDistance, 0, 1);
                double time = 1 - dt / o.BurstWindowSeconds;
                double c = 60 + 22 * visual + 7 * time;
                var reason = cosine is { } cs3
                    ? new Text($"scattate a {dt:0.#} s di distanza con la stessa fotocamera, somiglianza neurale {cs3:P0}",
                               $"taken {dt:0.#} s apart with the same camera, {cs3:P0} neural similarity")
                    : new Text($"scattate a {dt:0.#} s di distanza con la stessa fotocamera, hash a distanza {distance}/64",
                               $"taken {dt:0.#} s apart with the same camera, hash at distance {distance}/64");

                if (best is null || c > best.Value.Confidence)
                    best = new Match(MatchKind.Burst, Math.Min(c, BurstMax), reason);
            }
        }

        if (best is { } m && (a.HasEditMarker || b.HasEditMarker) && m.Confidence > EditedCap)
            best = m with { Confidence = EditedCap, Reason = m.Reason + new Text(", una delle due è una versione modificata", ", one of the two is an edited version") };

        return best;
    }

    /// <summary>Coppie che sembrano doppioni ma NON lo sono mai.</summary>
    public static bool IsExcludedPair(PhotoFile a, PhotoFile b, out string reason)
    {
        bool rawA = ScanOptions.RawExtensions.Contains(a.Extension);
        bool rawB = ScanOptions.RawExtensions.Contains(b.Extension);
        if (rawA != rawB &&
            string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.BaseName, b.BaseName, StringComparison.OrdinalIgnoreCase))
        {
            reason = "coppia RAW+JPEG dello stesso scatto";
            return true;
        }
        reason = "";
        return false;
    }

    /// <summary>Distanza minima tra il pHash di <paramref name="a"/> e le 8 orientazioni di <paramref name="b"/>.</summary>
    public static (int Distance, int Variant) BestDistance(PhotoFile a, PhotoFile b)
    {
        if (a.PHashVariants is not { } va || b.PHashVariants is not { } vb) return (64, 0);
        int best = 65, bestVariant = 0;
        for (int v = 0; v < vb.Length; v++)
        {
            int d = PerceptualHash.Distance(va[0], vb[v]);
            if (d < best) { best = d; bestVariant = v; }
        }
        return (best, bestVariant);
    }

    public static double? Cosine(float[]? x, float[]? y)
    {
        if (x is null || y is null || x.Length != y.Length || x.Length == 0) return null;
        double dot = 0;
        for (int i = 0; i < x.Length; i++) dot += x[i] * y[i];
        return dot; // gli embedding sono già normalizzati L2
    }

    private static double? DistanceMeters(PhotoFile a, PhotoFile b)
    {
        if (a.Latitude is not { } la1 || a.Longitude is not { } lo1 || b.Latitude is not { } la2 || b.Longitude is not { } lo2)
            return null;
        const double r = 6_371_000;
        double dLat = (la2 - la1) * Math.PI / 180, dLon = (lo2 - lo1) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(la1 * Math.PI / 180) * Math.Cos(la2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(h));
    }
}
