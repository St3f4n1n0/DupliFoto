using System.IO.Hashing;

namespace DupliFoto.Core.Imaging;

/// <summary>Estrae da una foto tutto ciò che serve ai livelli 2-4: una sola decodifica ridotta per file.</summary>
public sealed class ImageAnalyzer(IImageDecoder decoder, IMetadataReader metadata)
{
    public void Analyze(PhotoFile f, int thumbnailSide)
    {
        var meta = metadata.Read(f.Path);
        f.TakenAt = meta.TakenAt;
        f.CameraMake = meta.CameraMake;
        f.CameraModel = meta.CameraModel;
        f.Latitude = meta.Latitude;
        f.Longitude = meta.Longitude;
        f.MetadataRichness = meta.Richness;

        var (w, h) = decoder.ReadDimensions(f.Path);
        var thumb = decoder.DecodeThumbnail(f.Path, thumbnailSide);

        // Le dimensioni lette dal file non tengono conto dell'orientamento EXIF;
        // l'anteprima sì. Se l'orientamento non coincide, i lati vanno scambiati.
        if (w != h && (w > h) != (thumb.Width > thumb.Height)) (w, h) = (h, w);
        f.Width = w;
        f.Height = h;

        var luma = thumb.ToLuma();
        f.PHashVariants = PerceptualHash.ComputePHashVariants(luma, thumb.Width, thumb.Height);
        f.DHash = PerceptualHash.ComputeDHash(luma, thumb.Width, thumb.Height);
        f.Sharpness = PerceptualHash.Sharpness(luma, thumb.Width, thumb.Height);
    }

    /// <summary>Hash dei pixel a piena risoluzione: pesante, eseguito solo sui candidati "stessi pixel".</summary>
    public void ComputePixelHash(PhotoFile f)
    {
        var img = decoder.DecodeFull(f.Path);
        var hasher = new XxHash128();
        Span<byte> header = stackalloc byte[8];
        BitConverter.TryWriteBytes(header[..4], img.Width);
        BitConverter.TryWriteBytes(header[4..], img.Height);
        hasher.Append(header);
        hasher.Append(img.Pixels.AsSpan(0, img.Width * img.Height * 3));
        f.PixelHash = hasher.GetCurrentHashAsUInt128();
    }

    public RgbImage DecodeForEmbedding(PhotoFile f, int side) => decoder.DecodeThumbnail(f.Path, side);
}
