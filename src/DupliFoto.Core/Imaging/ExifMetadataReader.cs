using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace DupliFoto.Core.Imaging;

/// <summary>Legge data di scatto, fotocamera e GPS senza decodificare l'immagine.</summary>
public sealed class ExifMetadataReader : IMetadataReader
{
    public PhotoMetadata Read(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            dirs = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception)
        {
            return PhotoMetadata.Empty; // formato senza EXIF o file danneggiato: non è un errore bloccante
        }

        var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();

        DateTime? taken = null;
        if (sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
        {
            // I centesimi di secondo contano per distinguere gli scatti di una raffica.
            var subsec = sub.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal);
            if (!string.IsNullOrWhiteSpace(subsec) &&
                double.TryParse("0." + subsec.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var frac))
                dt = dt.AddSeconds(frac);
            taken = dt;
        }

        string? make = Clean(ifd0?.GetDescription(ExifDirectoryBase.TagMake));
        string? model = Clean(ifd0?.GetDescription(ExifDirectoryBase.TagModel));

        double? lat = null, lon = null;
        if (gps?.GetGeoLocation() is { } g)
        {
            lat = g.Latitude;
            lon = g.Longitude;
        }

        int richness = (taken is null ? 0 : 3) + (model is null ? 0 : 1) + (lat is null ? 0 : 2)
                       + Math.Min(4, dirs.Sum(d => d.TagCount) / 25);
        return new PhotoMetadata(taken, make, model, lat, lon, richness);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().TrimEnd('\0');
}
