using System.Diagnostics;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Imaging;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Scanning;
using Xunit;

namespace DupliFoto.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("foto", "foto", false)]
    [InlineData("foto (1)", "foto", true)]
    [InlineData("foto (12)", "foto", true)]
    [InlineData("foto - Copia", "foto", true)]
    [InlineData("foto - Copia (2)", "foto", true)]
    [InlineData("foto - Copy", "foto", true)]
    [InlineData("Copia di foto", "foto", true)]
    [InlineData("Copy of foto", "foto", true)]
    [InlineData("foto copy", "foto", true)]
    [InlineData("foto - Copia (2) (1)", "foto", true)]
    [InlineData("IMG_1234", "img_1234", false)]
    [InlineData("IMG_1234-2", "img_1234", false)]
    [InlineData("IMG_1234_1", "img_1234", false)]
    [InlineData("20240512_183011", "20240512_183011", false)]
    public void Normalizes_copy_markers(string input, string expected, bool copy)
    {
        var r = NameNormalizer.Normalize(input);
        Assert.Equal(expected, r.Normalized);
        Assert.Equal(copy, r.HasCopyMarker);
    }

    [Theory]
    [InlineData("IMG_1234-edited")]
    [InlineData("IMG_1234 (modificata)")]
    [InlineData("foto_edited")]
    public void Detects_edited_versions(string input)
    {
        var r = NameNormalizer.Normalize(input);
        Assert.True(r.HasEditMarker);
    }

    [Fact]
    public void Similarity_is_one_for_equal_names_and_lower_otherwise()
    {
        Assert.Equal(1.0, NameNormalizer.Similarity("img_1234", "img_1234"));
        Assert.InRange(NameNormalizer.Similarity("img_1234", "img_1235"), 0.8, 0.99);
        Assert.InRange(NameNormalizer.Similarity("vacanze", "img_1234"), 0.0, 0.3);
    }
}

public class PerceptualHashTests
{
    private static ulong[] Hashes(RgbImage img) => PerceptualHash.ComputePHashVariants(img.ToLuma(), img.Width, img.Height);

    [Fact]
    public void Resized_and_noisy_copy_is_close()
    {
        var a = TestImages.Scene(1);
        var b = TestImages.Resize(TestImages.Scene(1, noise: 6), 160, 120);
        int d = PerceptualHash.Distance(Hashes(a)[0], Hashes(b)[0]);
        Assert.InRange(d, 0, 6);
    }

    [Fact]
    public void Different_scenes_are_far()
    {
        for (int s = 2; s < 12; s++)
        {
            int d = PerceptualHash.Distance(Hashes(TestImages.Scene(1))[0], Hashes(TestImages.Scene(s))[0]);
            Assert.InRange(d, 12, 64);
        }
    }

    [Fact]
    public void Rotated_copy_is_found_through_orientation_variants()
    {
        var a = Hashes(TestImages.Scene(3));
        var r = Hashes(TestImages.Rotate90(TestImages.Scene(3)));
        int direct = PerceptualHash.Distance(a[0], r[0]);
        int best = r.Min(v => PerceptualHash.Distance(a[0], v));
        Assert.InRange(best, 0, 4);
        Assert.True(direct > best, $"diretta {direct}, migliore {best}");
    }

    [Fact]
    public void Sharpness_drops_when_image_is_blurred()
    {
        var sharp = TestImages.Scene(4);
        var blurred = TestImages.Resize(TestImages.Resize(sharp, 80, 60), 320, 240);
        double s1 = PerceptualHash.Sharpness(sharp.ToLuma(), 320, 240);
        double s2 = PerceptualHash.Sharpness(blurred.ToLuma(), 320, 240);
        Assert.True(s1 > s2 * 1.5, $"nitida {s1:0}, sfocata {s2:0}");
    }
}

public class BkTreeTests
{
    [Fact]
    public void Query_matches_brute_force()
    {
        var rnd = new Random(7);
        var hashes = Enumerable.Range(0, 3000).Select(_ => (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 40)).ToList();
        var tree = new BkTree<int>();
        for (int i = 0; i < hashes.Count; i++) tree.Add(hashes[i], i);

        for (int q = 0; q < 50; q++)
        {
            ulong probe = hashes[q] ^ (1UL << q % 64) ^ (1UL << (q * 7) % 64);
            var expected = Enumerable.Range(0, hashes.Count)
                .Where(i => PerceptualHash.Distance(probe, hashes[i]) <= 10).OrderBy(i => i).ToList();
            var actual = tree.Query(probe, 10).Select(r => r.Value).OrderBy(i => i).ToList();
            Assert.Equal(expected, actual);
        }
    }
}

public class SimilarityScorerTests
{
    private static PhotoFile F(string path, ulong phash = 0, DateTime? t = null, string model = "Pixel 9") => new()
    {
        Path = path,
        Size = 1000,
        PHashVariants = Enumerable.Repeat(phash, 8).ToArray(),
        DHash = 0,
        Width = 4000,
        Height = 3000,
        TakenAt = t,
        CameraModel = model,
        NormalizedName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
    };

    [Fact]
    public void Identical_bytes_score_100()
    {
        var a = F("/f/a.jpg"); var b = F("/f/b.jpg");
        a.FullHash = b.FullHash = 42;
        Assert.Equal(100, SimilarityScorer.Compare(a, b, new ScanOptions())!.Value.Confidence);
    }

    [Fact]
    public void Raw_plus_jpeg_is_never_a_duplicate()
    {
        var jpg = F("/f/IMG_1.jpg"); var raw = F("/f/IMG_1.cr3");
        Assert.Null(SimilarityScorer.Compare(jpg, raw, new ScanOptions()));
    }

    [Fact]
    public void Perceptual_scores_between_90_and_98()
    {
        var m = SimilarityScorer.Compare(F("/f/a.jpg", 0), F("/f/b.jpg", 0b111), new ScanOptions())!.Value;
        Assert.Equal(MatchKind.Perceptual, m.Kind);
        Assert.InRange(m.Confidence, 90, 98);
    }

    [Fact]
    public void Burst_never_exceeds_89()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0);
        var m = SimilarityScorer.Compare(F("/f/a.jpg", 0, t), F("/f/b.jpg", 0xFFFFUL, t.AddSeconds(1)), new ScanOptions())!.Value;
        Assert.Equal(MatchKind.Burst, m.Kind);
        Assert.InRange(m.Confidence, 60, 89);
    }

    [Fact]
    public void Near_identical_shots_with_different_times_are_bursts_not_copies()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0);
        var m = SimilarityScorer.Compare(F("/f/a.jpg", 0, t), F("/f/b.jpg", 0, t.AddSeconds(1)), new ScanOptions())!.Value;
        Assert.Equal(MatchKind.Burst, m.Kind);
    }

    [Fact]
    public void Burst_requires_same_camera()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0);
        Assert.Null(SimilarityScorer.Compare(F("/f/a.jpg", 0, t, "A"), F("/f/b.jpg", 0xFFFFUL, t.AddSeconds(1), "B"), new ScanOptions()));
    }

    [Fact]
    public void Edited_version_is_capped_at_80()
    {
        var a = F("/f/a.jpg"); var b = F("/f/a-edited.jpg");
        b.HasEditMarker = true;
        Assert.True(SimilarityScorer.Compare(a, b, new ScanOptions())!.Value.Confidence <= 80);
    }
}

public sealed class EngineEndToEndTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _photos;
    private readonly FakeMetadata _meta = new();

    public EngineEndToEndTests()
    {
        _photos = Path.Combine(_dir, "Foto");
        Directory.CreateDirectory(Path.Combine(_photos, "WhatsApp"));

        var t = new DateTime(2026, 8, 10, 18, 30, 0);
        void Save(RgbImage img, string name, DateTime? when = null, string? comment = null)
        {
            TestImages.SavePpm(img, Path.Combine(_photos, name), comment);
            _meta.ByFileName[Path.GetFileName(name)] = new PhotoMetadata(when, "Google", "Pixel 9", 45.4, 9.19, when is null ? 0 : 4);
        }

        Save(TestImages.Scene(10), "mare.ppm", t);                                                  // originale
        File.Copy(Path.Combine(_photos, "mare.ppm"), Path.Combine(_photos, "mare (1).ppm"));        // copia identica
        _meta.ByFileName["mare (1).ppm"] = _meta.ByFileName["mare.ppm"];
        Save(TestImages.Scene(10), "mare-senza-gps.ppm", comment: "metadati diversi");              // stessi pixel
        Save(TestImages.Resize(TestImages.Scene(10, noise: 5), 160, 120), "WhatsApp/IMG-WA0001.ppm"); // ricompressa

        Save(TestImages.Scene(20), "tramonto_1.ppm", t.AddMinutes(5));                               // raffica: nitida
        Save(TestImages.Resize(TestImages.Resize(TestImages.Scene(20, shiftX: 0.02), 90, 68), 320, 240),
             "tramonto_2.ppm", t.AddMinutes(5).AddSeconds(1.5));                                     // raffica: mossa

        Save(TestImages.Scene(30), "montagna.ppm", t.AddHours(1));                                   // unica
    }

    private ScanOptions Options(RunMode mode) => new()
    {
        Roots = { _photos },
        Extensions = { ".ppm" },
        Mode = mode,
        QuarantineRoot = Path.Combine(_dir, "Quarantena"),
        CachePath = Path.Combine(_dir, "cache.json"),
    };

    private ScanResult Scan(ScanOptions o) => new DedupEngine(new PpmDecoder(), _meta).Run(o);

    [Fact]
    public void Finds_every_level_with_the_right_confidence()
    {
        var r = Scan(Options(RunMode.ReadOnly));

        var exact = Assert.Single(r.Groups, g => g.Kind == MatchKind.ExactBytes);
        Assert.Equal("mare.ppm", Path.GetFileName(exact.Keeper.Path));          // il nome senza "(1)" vince
        Assert.Equal("mare (1).ppm", Path.GetFileName(exact.Duplicates.Single().File.Path));

        var sea = Assert.Single(r.Groups, g => g.AllFiles.Any(f => f.Path.EndsWith("IMG-WA0001.ppm")));
        Assert.Contains(sea.Duplicates, d => d.Kind == MatchKind.IdenticalPixels && d.Confidence == 99);
        Assert.Contains(sea.Duplicates, d => d.Kind == MatchKind.Perceptual && d.Confidence is >= 90 and < 99);
        Assert.NotEqual("IMG-WA0001.ppm", Path.GetFileName(sea.Keeper.Path));  // la versione WhatsApp non vince

        var burst = Assert.Single(r.Groups, g => g.Kind == MatchKind.Burst);
        Assert.Equal("tramonto_1.ppm", Path.GetFileName(burst.Keeper.Path));     // lo scatto più nitido
        Assert.InRange(burst.Confidence, 60, 89);

        Assert.DoesNotContain(r.Groups, g => g.AllFiles.Any(f => f.Path.EndsWith("montagna.ppm")));
    }

    [Fact]
    public void Read_only_mode_touches_nothing()
    {
        var before = Directory.GetFiles(_photos, "*", SearchOption.AllDirectories).Length;
        var o = Options(RunMode.ReadOnly);
        var s = new ActionExecutor(o, prompt: null).Execute(Scan(o));
        Assert.Equal(0, s.Moved);
        Assert.Equal(before, Directory.GetFiles(_photos, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Semi_automatic_moves_only_exact_copies_and_undo_restores_them()
    {
        var o = Options(RunMode.SemiAutomatic);
        var s = new ActionExecutor(o, prompt: null).Execute(Scan(o));

        Assert.Equal(1, s.Moved);
        Assert.False(File.Exists(Path.Combine(_photos, "mare (1).ppm")));
        Assert.True(File.Exists(Path.Combine(_photos, "mare.ppm")));
        Assert.True(File.Exists(Path.Combine(_photos, "mare-senza-gps.ppm")));   // 99%: non è al 100%, resta
        Assert.True(s.AwaitingReview >= 3);

        var undo = ActionJournal.Undo(s.JournalPath!);
        Assert.Equal(1, undo.Restored);
        Assert.True(File.Exists(Path.Combine(_photos, "mare (1).ppm")));
    }

    [Fact]
    public void Automatic_mode_respects_threshold_and_never_auto_moves_bursts()
    {
        var o = Options(RunMode.Automatic);
        o.AutoThreshold = 90;
        var s = new ActionExecutor(o, prompt: null).Execute(Scan(o));

        Assert.False(File.Exists(Path.Combine(_photos, "mare (1).ppm")));
        Assert.False(File.Exists(Path.Combine(_photos, "mare-senza-gps.ppm")));
        Assert.False(File.Exists(Path.Combine(_photos, "WhatsApp", "IMG-WA0001.ppm")));
        Assert.True(File.Exists(Path.Combine(_photos, "tramonto_2.ppm")));        // scatto multiplo: solo proposto
        Assert.Equal(3, s.AutomaticActions);
    }

    /// <summary>
    /// La stessa foto in tre copie, e anche la sua versione WhatsApp in tre copie: la copia tenuta tra le tre
    /// WhatsApp è a sua volta un doppione dell'originale. In qualunque ordine si applichino i gruppi, la modalità
    /// automatica non toglie mai l'ultima copia: ogni file spostato rimanda a una copia che è ancora al suo posto.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Automatic_mode_never_removes_the_last_copy(bool reverseGroups)
    {
        File.Copy(Path.Combine(_photos, "mare.ppm"), Path.Combine(_photos, "mare (2).ppm"));
        _meta.ByFileName["mare (2).ppm"] = _meta.ByFileName["mare.ppm"];
        File.Copy(Path.Combine(_photos, "WhatsApp", "IMG-WA0001.ppm"), Path.Combine(_photos, "WhatsApp", "IMG-WA0001 (1).ppm"));
        File.Copy(Path.Combine(_photos, "WhatsApp", "IMG-WA0001.ppm"), Path.Combine(_photos, "WhatsApp", "IMG-WA0001 (2).ppm"));

        var o = Options(RunMode.Automatic);
        o.AutoThreshold = 90; // il minimo: anche le versioni ricompresse vengono spostate da sole
        var r = Scan(o);
        if (reverseGroups) r = new ScanResult { Files = r.Files, Groups = r.Groups.Reverse().ToList(), Elapsed = r.Elapsed };
        var s = new ActionExecutor(o, prompt: null).Execute(r);

        var moved = ActionJournal.Read(s.JournalPath!).ToDictionary(e => e.OriginalPath, e => e.KeptFile, StringComparer.OrdinalIgnoreCase);
        foreach (var (file, kept) in moved)
        {
            var survivor = kept;
            for (int hops = 0; moved.TryGetValue(survivor, out var next); hops++)
            {
                Assert.True(hops < moved.Count, "catena circolare nel registro");
                survivor = next;
            }
            Assert.True(File.Exists(survivor), $"{file} spostato tenendo {kept}, ma di quella foto non resta nessuna copia");
        }
        Assert.True(File.Exists(Path.Combine(_photos, "mare.ppm")));             // la copia migliore resta sempre
        // In ordine normale vanno via le due copie di mare.ppm, le due della versione WhatsApp, poi quella
        // versione e i "stessi pixel". Al contrario la versione WhatsApp va via per prima, e allora le sue
        // copie restano: la loro copia da tenere non c'è più.
        Assert.Equal(reverseGroups ? 4 : 6, s.Moved);
    }

    [Fact]
    public void A_file_changed_after_the_scan_is_not_touched()
    {
        var o = Options(RunMode.SemiAutomatic);
        var result = Scan(o);
        File.SetLastWriteTimeUtc(Path.Combine(_photos, "mare (1).ppm"), DateTime.UtcNow.AddMinutes(1));
        var s = new ActionExecutor(o, prompt: null).Execute(result);
        Assert.Equal(0, s.Moved);
        Assert.Contains(s.Warnings, w => w.Contains("Modificato dopo la scansione"));
    }

    [Fact]
    public void User_answers_are_respected()
    {
        var o = Options(RunMode.Assisted);
        var prompt = new ScriptedPrompt(req => req.Group.Kind == MatchKind.Burst
            ? new PromptAnswer(UserChoice.Skip)
            : new PromptAnswer(UserChoice.Apply));
        var s = new ActionExecutor(o, prompt).Execute(Scan(o));

        Assert.Equal(0, s.AutomaticActions);
        Assert.Equal(3, s.ConfirmedActions);
        Assert.True(File.Exists(Path.Combine(_photos, "tramonto_2.ppm")));
        Assert.True(prompt.Calls >= 3);
    }

    [Fact]
    public void Second_scan_uses_the_cache()
    {
        var o = Options(RunMode.ReadOnly);
        Scan(o);
        var decoder = new CountingDecoder(new PpmDecoder());
        new DedupEngine(decoder, _meta).Run(o, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, decoder.Thumbnails);
    }

    [Fact]
    public void Session_moves_single_pairs_and_refuses_when_the_keeper_is_gone()
    {
        var o = Options(RunMode.Assisted);
        var r = Scan(o);
        var sea = r.Groups.Single(g => g.AllFiles.Any(f => f.Path.EndsWith("IMG-WA0001.ppm")));
        var whatsapp = sea.Duplicates.Single(d => d.File.Path.EndsWith("IMG-WA0001.ppm"));
        var sameData = sea.Duplicates.Single(d => d.Kind == MatchKind.IdenticalPixels);

        using var session = new ActionSession(o);
        Assert.False(File.Exists(session.JournalPath));                          // nessun registro finché non si sposta nulla

        Assert.Equal(MoveResult.Moved, session.Move(sea.Keeper, whatsapp, automatic: false).Result);
        Assert.False(File.Exists(whatsapp.File.Path));
        Assert.Equal(MoveResult.AlreadyHandled, session.Move(sea.Keeper, whatsapp, automatic: false).Result);

        // L'utente sposta la copia da tenere di un altro gruppo: da lì in poi quel gruppo non si tocca più.
        var exact = r.Groups.Single(g => g.Kind == MatchKind.ExactBytes);
        var original = new GroupMember { File = exact.Keeper, Kind = MatchKind.ExactBytes, Confidence = 100, Reason = Text.Empty };
        Assert.Equal(MoveResult.Moved, session.Move(exact.Duplicates[0].File, original, automatic: false).Result);
        Assert.Equal(MoveResult.KeeperUnavailable, session.Move(sea.Keeper, sameData, automatic: false).Result);
        Assert.True(File.Exists(sameData.File.Path));

        Assert.Equal(2, session.Summary.Moved);
        session.Dispose();
        Assert.Equal(2, ActionJournal.Undo(session.JournalPath).Restored);
        Assert.True(File.Exists(whatsapp.File.Path));
    }

    [Fact]
    public void Changing_the_keeper_rescores_members_against_the_new_one()
    {
        var o = Options(RunMode.Assisted);
        var sea = Scan(o).Groups.Single(g => g.AllFiles.Any(f => f.Path.EndsWith("IMG-WA0001.ppm")));
        var oldKeeper = sea.Keeper;
        var whatsapp = sea.Duplicates.Single(d => d.File.Path.EndsWith("IMG-WA0001.ppm")).File;

        GroupEditor.ChangeKeeper(sea, whatsapp, o);

        Assert.Same(whatsapp, sea.Keeper);
        Assert.Equal(3, sea.AllFiles.Count());
        var back = sea.Duplicates.Single(d => ReferenceEquals(d.File, oldKeeper));
        Assert.Equal(MatchKind.Perceptual, back.Kind);
        Assert.All(sea.Duplicates, d => Assert.Equal(MatchKind.Perceptual, d.Kind)); // "stessi pixel" valeva solo rispetto alla vecchia copia
        Assert.All(sea.Duplicates, d => Assert.InRange(d.Confidence, 90, 98));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    private sealed class ScriptedPrompt(Func<PromptRequest, PromptAnswer> answer) : IDecisionPrompt
    {
        public int Calls { get; private set; }
        public PromptAnswer Ask(PromptRequest request) { Calls++; return answer(request); }
    }

    private sealed class CountingDecoder(IImageDecoder inner) : IImageDecoder
    {
        public int Thumbnails;
        public (int Width, int Height) ReadDimensions(string path) => inner.ReadDimensions(path);
        public RgbImage DecodeThumbnail(string path, int maxSide) { Interlocked.Increment(ref Thumbnails); return inner.DecodeThumbnail(path, maxSide); }
        public RgbImage DecodeFull(string path) => inner.DecodeFull(path);
    }
}

public sealed class SafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-safety-" + Guid.NewGuid().ToString("N"));

    public SafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    [Fact]
    public void Scanner_ignores_links_and_never_loops()
    {
        string photos = Path.Combine(_dir, "Foto");
        Directory.CreateDirectory(Path.Combine(photos, "Sotto"));
        TestImages.SavePpm(TestImages.Scene(1), Path.Combine(photos, "a.ppm"));
        TestImages.SavePpm(TestImages.Scene(2), Path.Combine(photos, "Sotto", "b.ppm"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(photos, "Sotto", "giro"), photos);                     // ciclo
            File.CreateSymbolicLink(Path.Combine(photos, "collegamento.ppm"), Path.Combine(photos, "a.ppm"));  // doppione finto
        }
        catch (Exception) { return; } // Windows senza privilegi per i collegamenti simbolici: niente da provare

        var o = new ScanOptions { Roots = { photos }, Extensions = { ".ppm" }, QuarantineRoot = Path.Combine(_dir, "Q") };
        var files = DupliFoto.Core.Scanning.FileScanner.Scan(o, ct: TestContext.Current.CancellationToken);

        Assert.Equal(["a.ppm", "b.ppm"], files.Select(f => Path.GetFileName(f.Path)).Order());
    }

    [Fact]
    public void A_preferred_folder_does_not_include_folders_with_a_similar_name()
    {
        string foto = Path.Combine(_dir, "Foto"), foto2 = Path.Combine(_dir, "Foto2");
        var a = new PhotoFile { Path = Path.Combine(foto2, "img.jpg"), Size = 10, Width = 100, Height = 100 };
        var b = new PhotoFile { Path = Path.Combine(foto, "img.jpg"), Size = 10, Width = 100, Height = 100 };
        var o = new ScanOptions { PreferredFolders = { foto } };

        Assert.Same(b, KeeperPolicy.Choose([a, b], isBurst: false, o).Keeper);
        Assert.True(DupliFoto.Core.Scanning.FileScanner.IsUnder(b.Path, foto));
        Assert.False(DupliFoto.Core.Scanning.FileScanner.IsUnder(a.Path, foto));
    }

    [Fact]
    public void Two_sessions_never_share_the_journal()
    {
        var o = new ScanOptions { QuarantineRoot = Path.Combine(_dir, "Q") };
        using var s1 = new ActionSession(o);
        using var s2 = new ActionSession(o);
        Assert.NotEqual(s1.JournalPath, s2.JournalPath);
    }

    /// <summary>
    /// Un secondo percorso verso la cartella <paramref name="target"/>: una giunzione su Windows (non servono
    /// privilegi, come per le unità SUBST e le cartelle di rete aggiunte due volte), un collegamento simbolico altrove.
    /// </summary>
    private static void CreateFolderAlias(string alias, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{alias}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            })!;
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(alias, target);
        }
    }

    private static PhotoFile Existing(string path) =>
        new() { Path = path, Size = new FileInfo(path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(path) };

    [Fact]
    public void The_same_file_reached_through_another_path_is_never_moved()
    {
        string real = Path.Combine(_dir, "Foto"), alias = Path.Combine(_dir, "Stessa cartella");
        Directory.CreateDirectory(real);
        File.WriteAllBytes(Path.Combine(real, "a.jpg"), [1, 2, 3]);
        CreateFolderAlias(alias, real);
        var keeper = Existing(Path.Combine(real, "a.jpg"));
        var twin = Existing(Path.Combine(alias, "a.jpg"));

        using var session = new ActionSession(new ScanOptions { QuarantineRoot = Path.Combine(_dir, "Q") });
        var outcome = session.Move(keeper, new GroupMember { File = twin, Kind = MatchKind.ExactBytes, Confidence = 100, Reason = Text.Empty }, automatic: true);

        // Su Windows lo riconosce l'identità del file prima di spostarlo; altrove il controllo dopo lo spostamento
        // vede sparire la copia da tenere e rimette subito il file al suo posto.
        Assert.Equal(MoveResult.SameFile, outcome.Result);
        Assert.True(File.Exists(keeper.Path));
        Assert.Equal(0, session.Summary.Moved);
        Assert.False(File.Exists(session.JournalPath));
    }

    [Fact]
    public void A_folder_added_twice_through_another_path_never_loses_its_photos()
    {
        string real = Path.Combine(_dir, "Foto"), alias = Path.Combine(_dir, "Stessa cartella");
        Directory.CreateDirectory(real);
        TestImages.SavePpm(TestImages.Scene(1), Path.Combine(real, "a.ppm"));
        TestImages.SavePpm(TestImages.Scene(2), Path.Combine(real, "b.ppm"));
        CreateFolderAlias(alias, real);
        var o = new ScanOptions
        {
            Roots = { real, alias },
            Extensions = { ".ppm" },
            Mode = RunMode.Automatic,
            AutoThreshold = 90,
            QuarantineRoot = Path.Combine(_dir, "Q"),
            CachePath = Path.Combine(_dir, "cache.json"),
        };

        var r = new DedupEngine(new PpmDecoder(), new FakeMetadata()).Run(o, ct: TestContext.Current.CancellationToken);
        var s = new ActionExecutor(o, prompt: null).Execute(r);

        Assert.Equal(0, s.Moved);
        Assert.True(File.Exists(Path.Combine(real, "a.ppm")));
        Assert.True(File.Exists(Path.Combine(real, "b.ppm")));
        if (OperatingSystem.IsWindows())
        {
            // Su Windows i due percorsi vengono riconosciuti già nella ricerca: ogni foto contata una volta, nessun doppione.
            Assert.Equal(2, r.Files.Count);
            Assert.Empty(r.Groups);
        }
    }

    [Fact]
    public void Recycle_bin_moves_the_file_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return; // solo su Windows (la CI lo esegue su Windows Server)
        string path = Path.Combine(_dir, "da-cestinare.jpg");
        File.WriteAllBytes(path, [1, 2, 3]);
        var file = new PhotoFile { Path = path, Size = 3, LastWriteUtc = File.GetLastWriteTimeUtc(path) };
        string keeperPath = Path.Combine(_dir, "tieni.jpg");
        File.WriteAllBytes(keeperPath, [1, 2, 3]);
        var keeper = new PhotoFile { Path = keeperPath, Size = 3, LastWriteUtc = File.GetLastWriteTimeUtc(keeperPath) };

        using var session = new ActionSession(new ScanOptions { Disposal = DisposalMethod.RecycleBin, QuarantineRoot = Path.Combine(_dir, "Q") });
        var outcome = session.Move(keeper, new GroupMember { File = file, Kind = MatchKind.ExactBytes, Confidence = 100, Reason = Text.Empty }, automatic: false);

        Assert.Equal(MoveResult.Moved, outcome.Result);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(keeperPath));
    }

    [Fact]
    public void A_quarantine_of_an_earlier_version_is_never_searched()
    {
        // Le versioni fino alla 0.3.1 mettevano la quarantena in Immagini: chi analizza Immagini non deve ritrovarci
        // i doppioni già spostati, anche se ora la quarantena è altrove.
        string pictures = Path.Combine(_dir, "Immagini");
        string old = Path.Combine(pictures, "DupliFoto-Quarantena", "20260901-101500-abc123", "C", "Foto");
        Directory.CreateDirectory(old);
        File.WriteAllBytes(Path.Combine(pictures, "mare.jpg"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(old, "mare (1).jpg"), [1, 2, 3, 4]);

        var o = new ScanOptions { Roots = { pictures }, QuarantineRoot = Path.Combine(_dir, "app", "DupliFoto-Quarantena") };
        var files = FileScanner.Scan(o, ct: TestContext.Current.CancellationToken);
        Assert.Equal([Path.Combine(pictures, "mare.jpg")], files.Select(f => f.Path));
    }

    [Fact]
    public void Only_fixed_drives_have_a_recycle_bin()
    {
        Assert.Equal(OperatingSystem.IsWindows(), RecycleBin.IsAvailableFor(_dir)); // la cartella temporanea è sul disco C:
        Assert.False(RecycleBin.IsAvailableFor(@"\\server\condivisa\Foto"));       // in rete: "eliminare" cancellerebbe davvero
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(64 * 1024)]
    [InlineData(64 * 1024 + 5)]
    [InlineData(2 * 64 * 1024)]
    [InlineData(5 * 64 * 1024 + 17)]
    public void The_quick_check_compares_start_and_end_of_the_file(int size)
    {
        const int chunk = 64 * 1024;
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        string a = Path.Combine(_dir, "a.jpg"), b = Path.Combine(_dir, "b.jpg");
        File.WriteAllBytes(a, bytes);
        File.WriteAllBytes(b, bytes);
        Assert.True(ExactMatcher.HeadAndTailAreIdentical(a, b, chunk));
        if (size == 0) return;

        foreach (int at in new[] { 0, Math.Min(chunk, size) - 1, size - 1, Math.Max(0, size - chunk) })
        {
            var changed = (byte[])bytes.Clone();
            changed[at] ^= 0xFF;
            File.WriteAllBytes(b, changed);
            Assert.False(ExactMatcher.HeadAndTailAreIdentical(a, b, chunk), $"byte {at} di {size}");
        }
        File.WriteAllBytes(b, bytes[..^1]);
        Assert.False(ExactMatcher.HeadAndTailAreIdentical(a, b, chunk));
    }

    /// <summary>
    /// Due copie identiche; poi nel doppione cambia un byte, a <paramref name="offset"/> byte dall'inizio, con peso e
    /// data rimessi come prima: come un programma che riscrive i metadati e conserva la data.
    /// </summary>
    private (PhotoFile Keeper, GroupMember Twin) IdenticalPairThenChanged(int offset)
    {
        var bytes = new byte[600 * 1024];
        new Random(1).NextBytes(bytes);
        string keeperPath = Path.Combine(_dir, "tieni.jpg"), twinPath = Path.Combine(_dir, "copia.jpg");
        File.WriteAllBytes(keeperPath, bytes);
        File.WriteAllBytes(twinPath, bytes);
        var keeper = Existing(keeperPath);
        var twin = Existing(twinPath);
        bytes[offset] ^= 0xFF;
        File.WriteAllBytes(twinPath, bytes);
        File.SetLastWriteTimeUtc(twinPath, twin.LastWriteUtc);
        return (keeper, new GroupMember { File = twin, Kind = MatchKind.ExactBytes, Confidence = 100, Reason = Text.Empty });
    }

    [Fact]
    public void By_default_identical_files_are_checked_quickly_at_start_and_end()
    {
        var (keeper, twin) = IdenticalPairThenChanged(offset: 100); // i metadati stanno all'inizio del file
        using var session = new ActionSession(new ScanOptions { QuarantineRoot = Path.Combine(_dir, "Q") });
        var outcome = session.Move(keeper, twin, automatic: true);

        Assert.Equal(MoveResult.ByteCheckFailed, outcome.Result);
        Assert.Contains("Inizio o fine del file", outcome.Message);
        Assert.True(File.Exists(twin.File.Path));
        Assert.Equal(0, session.Summary.Moved);
    }

    [Theory]
    [InlineData(false, MoveResult.Moved)]
    [InlineData(true, MoveResult.ByteCheckFailed)]
    public void The_full_check_also_reads_the_middle_of_the_file(bool full, MoveResult expected)
    {
        var (keeper, twin) = IdenticalPairThenChanged(offset: 300 * 1024);
        using var session = new ActionSession(new ScanOptions { QuarantineRoot = Path.Combine(_dir, "Q"), VerifyBeforeMove = full });
        var outcome = session.Move(keeper, twin, automatic: true);

        // Il controllo rapido non legge il centro del file (lo ha già confrontato l'analisi, con l'hash completo);
        // la verifica completa sì. In ogni caso il file va in quarantena, da cui si può annullare.
        Assert.Equal(expected, outcome.Result);
        Assert.Equal(!full, !File.Exists(twin.File.Path));
        Assert.True(File.Exists(keeper.Path));
    }
}

public sealed class LockedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-lock-" + Guid.NewGuid().ToString("N"));

    public LockedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    private PhotoFile Photo(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return new PhotoFile { Path = path, Size = 3, LastWriteUtc = File.GetLastWriteTimeUtc(path) };
    }

    [Fact]
    public void A_briefly_locked_file_is_moved_once_released()
    {
        var keeper = Photo("tieni.jpg");
        var dup = Photo("doppione.jpg");
        // Come fa un antivirus o un'anteprima: il file resta aperto (senza condivisione in cancellazione) per un attimo.
        // Un thread dedicato, non il pool: con i test in parallelo il pool può tardare lo sblocco di secondi.
        var stream = new FileStream(dup.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() => { Thread.Sleep(300); stream.Dispose(); });
        release.Start();

        using var session = new ActionSession(new ScanOptions { QuarantineRoot = Path.Combine(_dir, "Q") });
        var outcome = session.Move(keeper, new GroupMember { File = dup, Kind = MatchKind.Perceptual, Confidence = 95, Reason = Text.Empty }, automatic: false);
        release.Join();

        Assert.True(outcome.Moved, outcome.Message);
        Assert.False(File.Exists(dup.Path));
    }
}
