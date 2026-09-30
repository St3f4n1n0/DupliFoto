using System.Numerics;

namespace DupliFoto.Core.Imaging;

/// <summary>
/// Hash percettivi a 64 bit. Due foto "uguali all'occhio" hanno hash a distanza di Hamming piccola,
/// anche se ricompresse, ridimensionate o con colori leggermente diversi.
/// </summary>
public static class PerceptualHash
{
    private const int DctSize = 32;
    private const int HashSide = 8;
    private static readonly float[] CosTable = BuildCosTable();

    private static float[] BuildCosTable()
    {
        // CosTable[u * DctSize + x] = cos((2x+1) u π / 2N)
        var t = new float[DctSize * DctSize];
        for (int u = 0; u < DctSize; u++)
            for (int x = 0; x < DctSize; x++)
                t[u * DctSize + x] = (float)Math.Cos((2 * x + 1) * u * Math.PI / (2 * DctSize));
        return t;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>
    /// pHash (DCT) nelle 8 orientazioni possibili (4 rotazioni × specchiatura).
    /// L'indice 0 è l'immagine così com'è. Serve a riconoscere le foto ruotate o specchiate.
    /// </summary>
    public static ulong[] ComputePHashVariants(float[] luma, int width, int height)
    {
        var small = ResizeArea(luma, width, height, DctSize, DctSize);
        var variants = new ulong[8];
        var buffer = new float[DctSize * DctSize];
        for (int v = 0; v < 8; v++)
        {
            Transform(small, buffer, v);
            variants[v] = PHashFrom32(buffer);
        }
        return variants;
    }

    public static ulong ComputePHash(float[] luma, int width, int height)
        => PHashFrom32(ResizeArea(luma, width, height, DctSize, DctSize));

    /// <summary>dHash: confronta pixel adiacenti su una griglia 9×8. Veloce e robusto alla compressione.</summary>
    public static ulong ComputeDHash(float[] luma, int width, int height)
    {
        var s = ResizeArea(luma, width, height, 9, 8);
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++, bit++)
                if (s[y * 9 + x] < s[y * 9 + x + 1])
                    hash |= 1UL << bit;
        return hash;
    }

    /// <summary>Varianza del Laplaciano: misura classica di nitidezza (sfocato = valore basso).</summary>
    public static double Sharpness(float[] luma, int width, int height)
    {
        if (width < 3 || height < 3) return 0;
        double sum = 0, sumSq = 0;
        long n = 0;
        for (int y = 1; y < height - 1; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                int i = row + x;
                double lap = 4 * luma[i] - luma[i - 1] - luma[i + 1] - luma[i - width] - luma[i + width];
                sum += lap;
                sumSq += lap * lap;
                n++;
            }
        }
        double mean = sum / n;
        return sumSq / n - mean * mean;
    }

    // ------------------------------------------------------------------

    private static ulong PHashFrom32(float[] m)
    {
        // DCT-II separabile, calcolando solo gli 8×8 coefficienti a bassa frequenza che servono.
        Span<float> rows = stackalloc float[DctSize * HashSide]; // [y][u]
        for (int y = 0; y < DctSize; y++)
            for (int u = 0; u < HashSide; u++)
            {
                float acc = 0;
                int cu = u * DctSize, my = y * DctSize;
                for (int x = 0; x < DctSize; x++) acc += m[my + x] * CosTable[cu + x];
                rows[y * HashSide + u] = acc;
            }

        Span<float> coeffs = stackalloc float[HashSide * HashSide]; // [v][u]
        for (int v = 0; v < HashSide; v++)
            for (int u = 0; u < HashSide; u++)
            {
                float acc = 0;
                int cv = v * DctSize;
                for (int y = 0; y < DctSize; y++) acc += rows[y * HashSide + u] * CosTable[cv + y];
                coeffs[v * HashSide + u] = acc;
            }

        Span<float> sorted = stackalloc float[HashSide * HashSide];
        coeffs.CopyTo(sorted);
        sorted.Sort();
        float median = (sorted[31] + sorted[32]) / 2f;

        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (coeffs[i] > median) hash |= 1UL << i;
        return hash;
    }

    /// <summary>Applica una delle 8 trasformazioni del quadrato (rotazioni e specchiature) a una matrice 32×32.</summary>
    internal static void Transform(float[] src, float[] dst, int variant)
    {
        const int n = DctSize;
        bool transpose = (variant & 4) != 0, flipX = (variant & 1) != 0, flipY = (variant & 2) != 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int sx = flipX ? n - 1 - x : x;
                int sy = flipY ? n - 1 - y : y;
                if (transpose) (sx, sy) = (sy, sx);
                dst[y * n + x] = src[sy * n + sx];
            }
    }

    /// <summary>Ridimensionamento con media d'area (box filter): evita l'aliasing, ottimo per gli hash.</summary>
    public static float[] ResizeArea(float[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new float[dw * dh];
        double sx = (double)sw / dw, sy = (double)sh / dh;
        for (int y = 0; y < dh; y++)
        {
            double y0 = y * sy, y1 = y0 + sy;
            for (int x = 0; x < dw; x++)
            {
                double x0 = x * sx, x1 = x0 + sx;
                double acc = 0, wsum = 0;
                for (int yy = (int)y0; yy < Math.Min(sh, (int)Math.Ceiling(y1)); yy++)
                {
                    double wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                    if (wy <= 0) continue;
                    int row = yy * sw;
                    for (int xx = (int)x0; xx < Math.Min(sw, (int)Math.Ceiling(x1)); xx++)
                    {
                        double wx = Math.Min(xx + 1, x1) - Math.Max(xx, x0);
                        if (wx <= 0) continue;
                        acc += src[row + xx] * wx * wy;
                        wsum += wx * wy;
                    }
                }
                dst[y * dw + x] = (float)(wsum > 0 ? acc / wsum : 0);
            }
        }
        return dst;
    }
}
