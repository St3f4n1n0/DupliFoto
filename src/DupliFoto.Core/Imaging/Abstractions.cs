namespace DupliFoto.Core.Imaging;

/// <summary>Immagine RGB a 8 bit, pixel interlacciati R,G,B,R,G,B...</summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Dimensioni non valide");
        if (pixels.Length < width * height * 3) throw new ArgumentException("Buffer dei pixel troppo piccolo", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Luminanza (Rec. 601) come matrice di float 0..255, riga per riga.</summary>
    public float[] ToLuma()
    {
        var luma = new float[Width * Height];
        var p = Pixels;
        for (int i = 0, j = 0; i < luma.Length; i++, j += 3)
            luma[i] = 0.299f * p[j] + 0.587f * p[j + 1] + 0.114f * p[j + 2];
        return luma;
    }
}

/// <summary>Decodifica delle immagini. Implementazione reale: <see cref="MagickImageDecoder"/>.</summary>
public interface IImageDecoder
{
    /// <summary>Dimensioni originali memorizzate nel file (non ruotate). Lettura veloce senza decodifica.</summary>
    (int Width, int Height) ReadDimensions(string path);

    /// <summary>Anteprima già orientata secondo l'EXIF, con lato massimo <paramref name="maxSide"/>.</summary>
    RgbImage DecodeThumbnail(string path, int maxSide);

    /// <summary>Decodifica completa, orientata, per l'hash dei pixel.</summary>
    RgbImage DecodeFull(string path);
}

/// <summary>Metadati di scatto letti dal file.</summary>
public sealed record PhotoMetadata(
    DateTime? TakenAt,
    string? CameraMake,
    string? CameraModel,
    double? Latitude,
    double? Longitude,
    int Richness)
{
    public static readonly PhotoMetadata Empty = new(null, null, null, null, null, 0);
}

public interface IMetadataReader
{
    PhotoMetadata Read(string path);
}

/// <summary>
/// Calcolo di embedding neurali (es. DINOv2) su NPU/GPU/CPU.
/// Implementazione per Windows: DupliFoto.Accel.WindowsMlEmbeddingProvider.
/// </summary>
public interface IEmbeddingProvider : IDisposable
{
    /// <summary>Descrizione leggibile dell'hardware in uso, per il report.</summary>
    string DeviceDescription { get; }

    /// <summary>Quante immagini conviene passare insieme (le GPU/NPU rendono meglio a lotti).</summary>
    int PreferredBatchSize { get; }

    /// <summary>Restituisce un embedding normalizzato L2 per ogni immagine, nello stesso ordine.</summary>
    float[][] Embed(IReadOnlyList<RgbImage> images);
}
