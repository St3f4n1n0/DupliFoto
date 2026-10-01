using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Scanning;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>
/// "Solo tra cartelle diverse" e la cartella da tenere: una cartella già catalogata (A) e una da sistemare (B).
/// Ognuna ha anche doppioni suoi, che in questa modalità non vanno toccati.
/// </summary>
public sealed class CrossFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-cartelle-" + Guid.NewGuid().ToString("N"));
    private readonly string _a, _b;

    public CrossFolderTests()
    {
        _a = Path.Combine(_dir, "Catalogate");
        _b = Path.Combine(_dir, "Da sistemare");
        Directory.CreateDirectory(_a);
        Directory.CreateDirectory(_b);

        TestImages.SavePpm(TestImages.Scene(10), Path.Combine(_a, "mare.ppm"));
        File.Copy(Path.Combine(_a, "mare.ppm"), Path.Combine(_a, "mare (1).ppm"));     // doppione dentro A
        TestImages.SavePpm(TestImages.Scene(30), Path.Combine(_a, "montagna.ppm"));

        File.Copy(Path.Combine(_a, "mare.ppm"), Path.Combine(_b, "IMG_0001.ppm"));      // copie di A in B
        File.Copy(Path.Combine(_a, "mare.ppm"), Path.Combine(_b, "IMG_0002.ppm"));
        TestImages.SavePpm(TestImages.Resize(TestImages.Scene(30), 160, 120), Path.Combine(_b, "montagna-piccola.ppm"));
        TestImages.SavePpm(TestImages.Scene(50), Path.Combine(_b, "bosco.ppm"));
        File.Copy(Path.Combine(_b, "bosco.ppm"), Path.Combine(_b, "bosco (1).ppm"));    // doppione solo dentro B
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    private ScanOptions Options(RunMode mode, string? keep) => new()
    {
        Roots = { _a, _b },
        Extensions = { ".ppm" },
        Mode = mode,
        AutoThreshold = 90,
        CrossFolderOnly = true,
        PreferredFolders = keep is null ? [] : [keep],
        QuarantineRoot = Path.Combine(_dir, "Quarantena"),
        CachePath = Path.Combine(_dir, "cache.json"),
    };

    private static ScanResult Scan(ScanOptions o) => new DedupEngine(new PpmDecoder(), new FakeMetadata()).Run(o);

    private static string[] Left(string folder) =>
        Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Only_pairs_between_different_folders_are_reported()
    {
        var r = Scan(Options(RunMode.ReadOnly, keep: null));

        Assert.NotEmpty(r.Groups);
        Assert.All(r.Groups, g => Assert.All(g.Duplicates, d => Assert.NotEqual(g.Keeper.Root, d.File.Root)));
        Assert.DoesNotContain(r.Groups, g => g.AllFiles.Any(f => f.Path.Contains("bosco")));   // doppione solo dentro B
        Assert.Equal(_a, Assert.Single(r.Files, f => f.Path.EndsWith("mare.ppm")).Root);
    }

    [Fact]
    public void Keeping_folder_A_cleans_from_B_the_photos_already_in_A()
    {
        var o = Options(RunMode.Automatic, keep: _a);
        var s = new ActionExecutor(o, prompt: null).Execute(Scan(o));

        Assert.Equal(["mare (1).ppm", "mare.ppm", "montagna.ppm"], Left(_a));        // A intatta, anche il suo doppione
        Assert.Equal(["bosco (1).ppm", "bosco.ppm"], Left(_b));                       // in B resta solo ciò che A non ha
        Assert.Equal(3, s.Moved);
    }

    [Fact]
    public void Keeping_folder_B_moves_the_copies_in_A_and_leaves_B_alone()
    {
        var o = Options(RunMode.Automatic, keep: _b);
        var s = new ActionExecutor(o, prompt: null).Execute(Scan(o));

        Assert.Empty(Left(_a));                                                         // tutte copie di foto che sono in B
        Assert.Equal(["IMG_0001.ppm", "IMG_0002.ppm", "bosco (1).ppm", "bosco.ppm", "montagna-piccola.ppm"], Left(_b));
        Assert.Equal(3, s.Moved);
    }

    [Fact]
    public void Swapping_the_keeper_brings_back_the_copies_set_aside()
    {
        var o = Options(RunMode.Assisted, keep: _a);
        var sea = Scan(o).Groups.Single(g => g.Kind == MatchKind.ExactBytes);
        Assert.Equal(_a, sea.Keeper.Root);
        Assert.Single(sea.SameFolderCopies);                                            // l'altra copia in A: non è un doppione
        Assert.Equal(2, sea.Duplicates.Count);                                          // le due copie in B

        GroupEditor.ChangeKeeper(sea, sea.Duplicates[0].File, o);

        Assert.Equal(_b, sea.Keeper.Root);
        Assert.All(sea.Duplicates, d => Assert.Equal(_a, d.File.Root));                 // ora vanno via le due copie in A
        Assert.Equal(2, sea.Duplicates.Count);
        Assert.Equal(_b, Assert.Single(sea.SameFolderCopies).Root);                     // e l'altra copia in B resta
    }

    [Fact]
    public void Nested_folders_belong_to_the_most_specific_one()
    {
        string photos = Path.Combine(_dir, "Foto"), catalogued = Path.Combine(photos, "Catalogate");
        Directory.CreateDirectory(catalogued);
        TestImages.SavePpm(TestImages.Scene(70), Path.Combine(photos, "IMG_0100.ppm"));
        File.Copy(Path.Combine(photos, "IMG_0100.ppm"), Path.Combine(catalogued, "Matrimonio 001.ppm"));
        // La copia catalogata è la più vecchia: se "Foto" se la prendesse, a parità di cartella vincerebbe lei.
        File.SetLastWriteTimeUtc(Path.Combine(catalogued, "Matrimonio 001.ppm"), DateTime.UtcNow.AddDays(-1));

        ScanOptions Nested(string keep) => new()
        {
            Roots = { photos, catalogued },
            Extensions = { ".ppm" },
            PreferredFolders = { keep },
            QuarantineRoot = Path.Combine(_dir, "Quarantena"),
        };

        var keepPhotos = Scan(Nested(photos)).Groups.Single();
        Assert.Equal(catalogued, keepPhotos.Duplicates.Single().File.Root);
        Assert.Equal("IMG_0100.ppm", Path.GetFileName(keepPhotos.Keeper.Path));        // "Foto" non si prende le foto di Catalogate

        var keepCatalogued = Scan(Nested(catalogued)).Groups.Single();
        Assert.Equal("Matrimonio 001.ppm", Path.GetFileName(keepCatalogued.Keeper.Path));
        Assert.Equal("si trova nella cartella da tenere", keepCatalogued.KeeperReason);
    }
}
