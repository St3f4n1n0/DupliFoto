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

    /// <summary>
    /// Un paesaggio sintetico (cielo, sole, montagne, lago) che somiglia a una foto: serve per gli screenshot
    /// della documentazione. <paramref name="shiftX"/> sposta leggermente l'inquadratura, come in una raffica.
    /// </summary>
    public static RgbImage Landscape(int seed, int w = 1200, int h = 800, double shiftX = 0)
    {
        var rnd = new Random(seed);
        (double r, double g, double b)[][] skies =
        [
            [(52, 120, 200), (170, 210, 240)],   // giorno
            [(40, 50, 110), (245, 150, 90)],     // tramonto
            [(90, 150, 210), (230, 225, 210)],   // mattino velato
        ];
        var sky = skies[seed % skies.Length];
        double sunX = 0.2 + rnd.NextDouble() * 0.6, sunY = 0.18 + rnd.NextDouble() * 0.15, sunR = 0.05;
        var ridges = Enumerable.Range(0, 3).Select(i => (
            Base: 0.42 + i * 0.1, Amp: 0.10 - i * 0.02,
            F1: 2 + rnd.NextDouble() * 3, F2: 7 + rnd.NextDouble() * 6, P1: rnd.NextDouble() * 6, P2: rnd.NextDouble() * 6,
            Color: new[] { (70.0, 95.0, 120.0), (48.0, 92.0, 70.0), (30.0, 70.0, 45.0) }[i])).ToArray();
        double lake = 0.8;
        var noise = new Random(seed * 7);
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            double v = (double)y / h;
            for (int x = 0; x < w; x++)
            {
                double u = (double)x / w + shiftX;
                double yy = v < lake ? v : 2 * lake - v;   // il lago riflette il paesaggio
                double t = Math.Clamp(yy / 0.6, 0, 1);
                double r = sky[0].r + (sky[1].r - sky[0].r) * t, g = sky[0].g + (sky[1].g - sky[0].g) * t, b = sky[0].b + (sky[1].b - sky[0].b) * t;
                double dx = u - sunX, dy = (yy - sunY) * h / w, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < sunR) (r, g, b) = (255, 236, 180);
                else { double glow = Math.Max(0, 1 - d / (sunR * 4)) * 60; r += glow; g += glow * 0.8; b += glow * 0.4; }
                foreach (var m in ridges)
                {
                    double top = m.Base - m.Amp * (Math.Sin(u * m.F1 + m.P1) * 0.7 + Math.Sin(u * m.F2 + m.P2) * 0.3);
                    if (yy > top) { double shade = 1 - (yy - top) * 0.8; (r, g, b) = (m.Color.Item1 * shade, m.Color.Item2 * shade, m.Color.Item3 * shade); }
                }
                if (v >= lake) { r = r * 0.75 + 10; g = g * 0.8 + 20; b = b * 0.85 + 35; }
                double n = noise.Next(-4, 5);
                int k = (y * w + x) * 3;
                px[k] = (byte)Math.Clamp(r + n, 0, 255);
                px[k + 1] = (byte)Math.Clamp(g + n, 0, 255);
                px[k + 2] = (byte)Math.Clamp(b + n, 0, 255);
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
