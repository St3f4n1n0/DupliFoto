using System.Collections.Concurrent;
using System.Text.Json;

namespace DupliFoto.Core.Scanning;

/// <summary>
/// Cache su disco dei risultati di analisi, indicizzata per (percorso, peso, data di modifica).
/// Se un file non è cambiato, alla scansione successiva non viene né riletto né decodificato.
/// </summary>
public sealed class AnalysisCache
{
    public sealed class Entry
    {
        public long Size { get; set; }
        public long WriteTicks { get; set; }
        public ulong? Partial { get; set; }
        public UInt128? Full { get; set; }
        public UInt128? Pixel { get; set; }
        public ulong[]? PHashes { get; set; }
        public ulong? DHash { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Sharpness { get; set; }
        public DateTime? TakenAt { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
        public int Richness { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _path;

    public AnalysisCache(string? path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto", "cache-v1.json");

    public int Count => _entries.Count;

    public void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            using var fs = File.OpenRead(_path);
            var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(fs);
            if (data is null) return;
            foreach (var kv in data) _entries[kv.Key] = kv.Value;
        }
        catch (Exception)
        {
            _entries.Clear(); // cache corrotta: si riparte da zero, non è un problema
        }
    }

    public void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        using (var fs = File.Create(tmp))
            JsonSerializer.Serialize(fs, new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>Rimuove le voci di file che non esistono più tra quelli scansionati sotto le stesse radici.</summary>
    public void Prune(IEnumerable<string> roots, IReadOnlySet<string> existing)
    {
        var rootList = roots.ToList();
        foreach (var key in _entries.Keys)
            if (rootList.Any(r => FileScanner.IsUnder(key, r)) && !existing.Contains(key))
                _entries.TryRemove(key, out _);
    }

    public void ApplyTo(PhotoFile f)
    {
        if (!_entries.TryGetValue(f.Path, out var e)) return;
        if (e.Size != f.Size || e.WriteTicks != f.LastWriteUtc.Ticks)
        {
            _entries.TryRemove(f.Path, out _);
            return;
        }
        f.PartialHash = e.Partial;
        f.FullHash = e.Full;
        f.PixelHash = e.Pixel;
        if (e.PHashes is { Length: 8 })
        {
            f.PHashVariants = e.PHashes;
            f.DHash = e.DHash;
            f.Width = e.Width;
            f.Height = e.Height;
            f.Sharpness = e.Sharpness;
            f.TakenAt = e.TakenAt;
            f.CameraMake = e.Make;
            f.CameraModel = e.Model;
            f.Latitude = e.Lat;
            f.Longitude = e.Lon;
            f.MetadataRichness = e.Richness;
        }
    }

    public void Store(PhotoFile f)
    {
        _entries[f.Path] = new Entry
        {
            Size = f.Size,
            WriteTicks = f.LastWriteUtc.Ticks,
            Partial = f.PartialHash,
            Full = f.FullHash,
            Pixel = f.PixelHash,
            PHashes = f.PHashVariants,
            DHash = f.DHash,
            Width = f.Width,
            Height = f.Height,
            Sharpness = f.Sharpness,
            TakenAt = f.TakenAt,
            Make = f.CameraMake,
            Model = f.CameraModel,
            Lat = f.Latitude,
            Lon = f.Longitude,
            Richness = f.MetadataRichness,
        };
    }
}
