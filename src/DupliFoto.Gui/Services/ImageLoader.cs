using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DupliFoto.Core.Imaging;

namespace DupliFoto.Gui.Services;

/// <summary>
/// Anteprime per il confronto, decodificate con lo stesso Magick.NET del motore:
/// si vedono anche HEIC, AVIF e RAW, già raddrizzati secondo l'EXIF.
/// </summary>
public sealed class ImageLoader(IImageDecoder decoder, int maxSide = 1400, int capacity = 16)
{
    private readonly object _lock = new();
    private readonly LinkedList<(string Path, Bitmap Image)> _cache = new(); // i più recenti in testa

    public async Task<Bitmap?> LoadAsync(string path, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var hit = _cache.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (hit.Image is not null)
            {
                _cache.Remove(hit);
                _cache.AddFirst(hit);
                return hit.Image;
            }
        }

        var bitmap = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return ToBitmap(decoder.DecodeThumbnail(path, maxSide));
        }, ct);

        lock (_lock)
        {
            _cache.AddFirst((path, bitmap));
            // Le bitmap uscite dalla cache non vengono chiuse a mano: potrebbero essere ancora a schermo.
            while (_cache.Count > capacity) _cache.RemoveLast();
        }
        return bitmap;
    }

    /// <summary>Da RGB interlacciato (motore) a BGRA (Avalonia).</summary>
    public static WriteableBitmap ToBitmap(RgbImage img)
    {
        var bitmap = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = bitmap.Lock();
        var row = new byte[img.Width * 4];
        for (int y = 0; y < img.Height; y++)
        {
            int src = y * img.Width * 3;
            for (int x = 0, d = 0; x < img.Width; x++, d += 4, src += 3)
            {
                row[d] = img.Pixels[src + 2];
                row[d + 1] = img.Pixels[src + 1];
                row[d + 2] = img.Pixels[src];
                row[d + 3] = 255;
            }
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return bitmap;
    }
}
