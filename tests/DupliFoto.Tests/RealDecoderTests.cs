using DupliFoto.Core;
using DupliFoto.Core.Imaging;
using ImageMagick;
using PerceptualHash = DupliFoto.Core.Imaging.PerceptualHash;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>
/// Test con le librerie vere (Magick.NET e MetadataExtractor) su JPEG e PNG generati al momento:
/// verificano a runtime ciò che il resto dei test simula con PPM e metadati finti.
/// </summary>
public sealed class RealDecoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-real-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Shot = new(2026, 8, 10, 18, 30, 0);

    public RealDecoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Exif_reader_gets_date_with_subseconds_camera_and_gps()
    {
        RealImages.SaveJpeg(TestImages.Scene(1), P("a.jpg"), exif: new RealImages.Exif(Shot, Subsec: "25", Latitude: 45.4642, Longitude: -9.19));

        var m = new ExifMetadataReader().Read(P("a.jpg"));

        Assert.Equal(Shot.AddSeconds(0.25), m.TakenAt);
        Assert.Equal("Google", m.CameraMake);
        Assert.Equal("Pixel 9", m.CameraModel);
        Assert.Equal(45.4642, m.Latitude!.Value, 3);
        Assert.Equal(-9.19, m.Longitude!.Value, 3);
        Assert.True(m.Richness >= 6);
    }

    [Fact]
    public void Exif_reader_returns_empty_metadata_for_files_without_exif_or_not_images()
    {
        RealImages.SaveJpeg(TestImages.Scene(1), P("a.jpg"));
        File.WriteAllText(P("finta.jpg"), "non sono una foto");

        var m = new ExifMetadataReader().Read(P("a.jpg"));
        Assert.Null(m.TakenAt);
        Assert.Null(m.CameraModel);
        Assert.Equal(PhotoMetadata.Empty, new ExifMetadataReader().Read(P("finta.jpg")));
    }

    [Fact]
    public void Decoder_reads_dimensions_and_scales_thumbnails()
    {
        RealImages.SaveJpeg(TestImages.Scene(2, w: 1200, h: 900), P("grande.jpg"));
        var d = new MagickImageDecoder();

        Assert.Equal((1200, 900), d.ReadDimensions(P("grande.jpg")));

        var thumb = d.DecodeThumbnail(P("grande.jpg"), 256);
        Assert.Equal(256, thumb.Width);
        Assert.Equal(192, thumb.Height);
        Assert.Equal(256 * 192 * 3, thumb.Pixels.Length);

        var full = d.DecodeFull(P("grande.jpg"));
        Assert.Equal((1200, 900), (full.Width, full.Height));
    }

    [Fact]
    public void Decoder_never_enlarges_small_images()
    {
        RealImages.SaveJpeg(TestImages.Scene(6, w: 300, h: 200), P("piccola.jpg"));
        var d = new MagickImageDecoder();

        var big = d.DecodeThumbnail(P("piccola.jpg"), 1400);   // anteprima per la GUI
        Assert.Equal((300, 200), (big.Width, big.Height));
        var small = d.DecodeThumbnail(P("piccola.jpg"), 256);  // anteprima per gli hash
        Assert.Equal((256, 171), (small.Width, small.Height));
    }

    [Fact]
    public void Decoder_applies_exif_orientation()
    {
        // Pixel salvati "coricati" (240×320) con Orientation = 6: raddrizzata diventa 320×240.
        var upright = TestImages.Scene(3);
        var stored = TestImages.Rotate90(TestImages.Rotate90(TestImages.Rotate90(upright)));
        RealImages.SaveJpeg(stored, P("coricata.jpg"), orientation: 6);
        var d = new MagickImageDecoder();

        Assert.Equal((240, 320), d.ReadDimensions(P("coricata.jpg")));  // dimensioni nel file, non ruotate
        var thumb = d.DecodeThumbnail(P("coricata.jpg"), 256);
        Assert.True(thumb.Width > thumb.Height, $"anteprima {thumb.Width}×{thumb.Height}");

        var h1 = PerceptualHash.ComputePHash(upright.ToLuma(), upright.Width, upright.Height);
        var h2 = PerceptualHash.ComputePHash(thumb.ToLuma(), thumb.Width, thumb.Height);
        Assert.InRange(PerceptualHash.Distance(h1, h2), 0, 6);
    }

    [Fact]
    public void Decoder_returns_rgb_for_grayscale_and_alpha_images()
    {
        using (var gray = RealImages.ToMagick(TestImages.Scene(4)))
        {
            gray.Grayscale();
            gray.Write(P("grigia.png"), MagickFormat.Png);
        }
        using (var alpha = RealImages.ToMagick(TestImages.Scene(5)))
        {
            alpha.Alpha(AlphaOption.Set);
            alpha.Write(P("trasparente.png"), MagickFormat.Png32);
        }
        var d = new MagickImageDecoder();

        var g = d.DecodeFull(P("grigia.png"));
        Assert.Equal(320 * 240 * 3, g.Pixels.Length);
        for (int i = 0; i < g.Pixels.Length; i += 3 * 97)
        {
            Assert.Equal(g.Pixels[i], g.Pixels[i + 1]);
            Assert.Equal(g.Pixels[i], g.Pixels[i + 2]);
        }

        var a = d.DecodeFull(P("trasparente.png"));
        Assert.Equal(320 * 240 * 3, a.Pixels.Length);
    }

    [Fact]
    public void Engine_finds_every_level_on_real_jpeg_and_png_files()
    {
        var gps = new RealImages.Exif(Shot, Latitude: 45.4642, Longitude: 9.19);
        RealImages.SaveJpeg(TestImages.Scene(10, w: 640, h: 480), P("mare.jpg"), exif: gps);
        File.Copy(P("mare.jpg"), P("mare (1).jpg"));                                                  // identica
        RealImages.SaveJpeg(TestImages.Scene(10, w: 320, h: 240), P("IMG-20260810-WA0001.jpg"), quality: 60); // ricompressa
        RealImages.SaveJpeg(TestImages.Rotate90(TestImages.Scene(10, w: 640, h: 480)), P("mare ruotata.jpg")); // ruotata

        RealImages.SavePng(TestImages.Scene(20), P("fiore.png"));
        RealImages.SavePng(TestImages.Scene(20), P("fiore-esportata.png"), comment: "esportata da un altro programma"); // stessi pixel

        RealImages.SaveJpeg(TestImages.Scene(30, w: 640, h: 480), P("raffica_1.jpg"), exif: gps with { TakenAt = Shot.AddMinutes(5) });
        RealImages.SaveJpeg(TestImages.Resize(TestImages.Resize(TestImages.Scene(30, w: 640, h: 480, shiftX: 0.02), 180, 135), 640, 480),
            P("raffica_2.jpg"), exif: gps with { TakenAt = Shot.AddMinutes(5).AddSeconds(1.5) });              // mossa

        RealImages.SaveJpeg(TestImages.Scene(40, w: 640, h: 480), P("montagna.jpg"), exif: gps with { TakenAt = Shot.AddHours(1) });

        var o = new ScanOptions { Roots = { _dir }, QuarantineRoot = P("Quarantena"), CachePath = null };
        var r = new DedupEngine(new MagickImageDecoder(), new ExifMetadataReader()).Run(o);

        Assert.Equal(0, r.UnreadableFiles);
        string Name(PhotoFile f) => Path.GetFileName(f.Path);

        var exact = Assert.Single(r.Groups, g => g.Kind == MatchKind.ExactBytes);
        Assert.Equal("mare.jpg", Name(exact.Keeper));

        var sea = Assert.Single(r.Groups, g => g.AllFiles.Any(f => Name(f) == "IMG-20260810-WA0001.jpg"));
        Assert.Equal("mare.jpg", Name(sea.Keeper));
        Assert.All(sea.Duplicates, m => Assert.Equal(MatchKind.Perceptual, m.Kind));
        Assert.Contains(sea.Duplicates, m => Name(m.File) == "mare ruotata.jpg");
        Assert.All(sea.Duplicates, m => Assert.InRange(m.Confidence, 80, 98));

        var flower = Assert.Single(r.Groups, g => g.Kind == MatchKind.IdenticalPixels);
        Assert.Equal(99, flower.Confidence);

        var burst = Assert.Single(r.Groups, g => g.Kind == MatchKind.Burst);
        Assert.Equal("raffica_1.jpg", Name(burst.Keeper));
        Assert.InRange(burst.Confidence, 60, 89);

        Assert.DoesNotContain(r.Groups, g => g.AllFiles.Any(f => Name(f) == "montagna.jpg"));
        Assert.Equal(4, r.Groups.Count);
    }
}
