using System.Text;
using DupliFoto.Core.Imaging;

namespace DupliFoto.Tests;

/// <summary>Immagini sintetiche deterministiche, per testare gli algoritmi senza foto reali.</summary>
internal static class TestImages
{
    /// <summary>Una "scena": sfondo sfumato e alcune forme colorate, determinate dal seme.</summary>
    public static RgbImage Scene(int seed, int w = 320, int h = 240, double shiftX = 0, int noise = 0, int brightness = 0)
    {
        var rnd = new Random(seed);
        var shapes = Enumerable.Range(0, 6).Select(_ => (
            X: rnd.NextDouble(), Y: rnd.NextDouble(), R: 0.08 + rnd.NextDouble() * 0.2,
            C: new[] { rnd.Next(256), rnd.Next(256), rnd.Next(256) }, Square: rnd.Next(2) == 0)).ToArray();
        var bg = new[] { rnd.Next(256), rnd.Next(256), rnd.Next(256) };
        var noiseRnd = new Random(seed * 31 + noise);
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (double)x / w + shiftX, v = (double)y / h;
                var c = new double[] { bg[0] * (1 - v), bg[1] * u, bg[2] * (0.5 + 0.5 * v) };
                foreach (var s in shapes)
                {
                    double dx = u - s.X, dy = (v - s.Y) * h / w;
                    bool inside = s.Square ? Math.Abs(dx) < s.R && Math.Abs(dy) < s.R : dx * dx + dy * dy < s.R * s.R;
                    if (inside) c = [s.C[0], s.C[1], s.C[2]];
                }
                for (int k = 0; k < 3; k++)
                {
                    double val = c[k] + brightness + (noise > 0 ? noiseRnd.Next(-noise, noise + 1) : 0);
                    px[(y * w + x) * 3 + k] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        return new RgbImage(w, h, px);
    }

    public static RgbImage Rotate90(RgbImage img)
    {
        int w = img.Width, h = img.Height;
        var dst = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int nx = h - 1 - y, ny = x; // nuova immagine: larghezza h, altezza w
                Array.Copy(img.Pixels, (y * w + x) * 3, dst, (ny * h + nx) * 3, 3);
            }
        return new RgbImage(h, w, dst);
    }

    public static RgbImage Resize(RgbImage img, int w, int h)
    {
        var dst = new byte[w * h * 3];
        for (int c = 0; c < 3; c++)
        {
            var plane = new float[img.Width * img.Height];
            for (int i = 0; i < plane.Length; i++) plane[i] = img.Pixels[i * 3 + c];
            var r = PerceptualHash.ResizeArea(plane, img.Width, img.Height, w, h);
            for (int i = 0; i < r.Length; i++) dst[i * 3 + c] = (byte)Math.Clamp(Math.Round(r[i]), 0, 255);
        }
        return new RgbImage(w, h, dst);
    }

    public static void SavePpm(RgbImage img, string path, string? comment = null)
    {
        using var fs = File.Create(path);
        var header = $"P6\n{(comment is null ? "" : "# " + comment + "\n")}{img.Width} {img.Height}\n255\n";
        fs.Write(Encoding.ASCII.GetBytes(header));
        fs.Write(img.Pixels, 0, img.Width * img.Height * 3);
    }

    public static RgbImage LoadPpm(string path)
    {
        var data = File.ReadAllBytes(path);
        int pos = 0;
        string Token()
        {
            while (true)
            {
                while (char.IsWhiteSpace((char)data[pos])) pos++;
                if (data[pos] != '#') break;
                while (data[pos] != '\n') pos++;
            }
            int start = pos;
            while (!char.IsWhiteSpace((char)data[pos])) pos++;
            return Encoding.ASCII.GetString(data, start, pos - start);
        }
        if (Token() != "P6") throw new InvalidDataException("Non è un PPM");
        int w = int.Parse(Token()), h = int.Parse(Token());
        Token();
        pos++;
        var px = new byte[w * h * 3];
        Array.Copy(data, pos, px, 0, px.Length);
        return new RgbImage(w, h, px);
    }
}

/// <summary>Decoder per file PPM, usato al posto di Magick.NET nei test.</summary>
internal sealed class PpmDecoder : IImageDecoder
{
    public (int Width, int Height) ReadDimensions(string path)
    {
        var img = TestImages.LoadPpm(path);
        return (img.Width, img.Height);
    }

    public RgbImage DecodeThumbnail(string path, int maxSide)
    {
        var img = TestImages.LoadPpm(path);
        double s = Math.Min(1.0, (double)maxSide / Math.Max(img.Width, img.Height));
        return TestImages.Resize(img, Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s)));
    }

    public RgbImage DecodeFull(string path) => TestImages.LoadPpm(path);
}

/// <summary>Metadati finti, assegnati per nome di file.</summary>
internal sealed class FakeMetadata : IMetadataReader
{
    public Dictionary<string, PhotoMetadata> ByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PhotoMetadata Read(string path) =>
        ByFileName.TryGetValue(Path.GetFileName(path), out var m) ? m : PhotoMetadata.Empty;
}
