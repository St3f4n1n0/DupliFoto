using DupliFoto.Core.Imaging;
using ImageMagick;

namespace DupliFoto.Tests;

/// <summary>Scrive JPEG e PNG veri, con EXIF facoltativi, a partire dalle immagini sintetiche.</summary>
internal static class RealImages
{
    public sealed record Exif(DateTime TakenAt, string? Subsec = null, double? Latitude = null, double? Longitude = null,
        string Make = "Google", string Model = "Pixel 9");

    public static MagickImage ToMagick(RgbImage img)
    {
        var image = new MagickImage();
        image.ReadPixels(img.Pixels, new PixelReadSettings((uint)img.Width, (uint)img.Height, StorageType.Char, PixelMapping.RGB));
        return image;
    }

    public static void SaveJpeg(RgbImage img, string path, uint quality = 92, Exif? exif = null, ushort? orientation = null)
    {
        using var image = ToMagick(img);
        image.Quality = quality;
        if (exif is not null || orientation is not null)
        {
            var profile = new ExifProfile();
            if (exif is not null)
            {
                profile.SetValue(ExifTag.Make, exif.Make);
                profile.SetValue(ExifTag.Model, exif.Model);
                profile.SetValue(ExifTag.DateTimeOriginal, exif.TakenAt.ToString("yyyy:MM:dd HH:mm:ss"));
                if (exif.Subsec is not null) profile.SetValue(ExifTag.SubsecTimeOriginal, exif.Subsec);
                else if (exif.TakenAt.Millisecond > 0) profile.SetValue(ExifTag.SubsecTimeOriginal, exif.TakenAt.ToString("fff"));
                if (exif.Latitude is { } lat && exif.Longitude is { } lon)
                {
                    profile.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
                    profile.SetValue(ExifTag.GPSLatitude, Dms(Math.Abs(lat)));
                    profile.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
                    profile.SetValue(ExifTag.GPSLongitude, Dms(Math.Abs(lon)));
                }
            }
            if (orientation is { } or) profile.SetValue(ExifTag.Orientation, or);
            image.SetProfile(profile);
            // ImageMagick riscrive il tag EXIF Orientation dalla proprietà dell'immagine: va impostata anche lì.
            if (orientation is { } o) image.Orientation = (OrientationType)o;
        }
        image.Write(path, MagickFormat.Jpeg);
    }

    public static void SavePng(RgbImage img, string path, string? comment = null)
    {
        using var image = ToMagick(img);
        if (comment is not null) image.Comment = comment;
        image.Write(path, MagickFormat.Png24);
    }

    private static Rational[] Dms(double degrees)
    {
        int d = (int)degrees;
        double minutes = (degrees - d) * 60;
        int m = (int)minutes;
        double s = (minutes - m) * 60;
        return [new Rational((uint)d, 1), new Rational((uint)m, 1), new Rational((uint)Math.Round(s * 1000), 1000)];
    }
}
