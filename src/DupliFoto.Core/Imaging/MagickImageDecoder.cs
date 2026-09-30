using ImageMagick;

namespace DupliFoto.Core.Imaging;

/// <summary>
/// Decodifica tramite Magick.NET: JPEG, PNG, HEIC/HEIF, AVIF, WebP, JPEG XL, TIFF e i principali RAW.
/// </summary>
public sealed class MagickImageDecoder : IImageDecoder
{
    public (int Width, int Height) ReadDimensions(string path)
    {
        var info = new MagickImageInfo(path);
        return ((int)info.Width, (int)info.Height);
    }

    public RgbImage DecodeThumbnail(string path, int maxSide)
    {
        var settings = new MagickReadSettings();
        // Per i JPEG chiede a libjpeg di decodificare direttamente a scala ridotta (1/2, 1/4, 1/8):
        // è il singolo accorgimento che accelera di più la scansione.
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{maxSide * 2}x{maxSide * 2}");

        using var image = new MagickImage(path, settings);
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry((uint)maxSide, (uint)maxSide));
        return ToRgb(image);
    }

    public RgbImage DecodeFull(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        return ToRgb(image);
    }

    private static RgbImage ToRgb(MagickImage image)
    {
        if (image.ColorSpace != ColorSpace.sRGB && image.ColorSpace != ColorSpace.Gray)
            image.ColorSpace = ColorSpace.sRGB; // es. JPEG CMYK
        using var pixels = image.GetPixelsUnsafe();
        byte[] data = pixels.ToByteArray(PixelMapping.RGB)
                      ?? throw new InvalidOperationException("Impossibile leggere i pixel");
        return new RgbImage((int)image.Width, (int)image.Height, data);
    }
}
