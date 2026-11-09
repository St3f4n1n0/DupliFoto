using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Matching;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>
/// I video: solo copie identiche, con gli hash. Verifica completa (tutto il file, 100%) o rapida (16 blocchi sparsi,
/// 99%, mai spostati da soli in semi-automatica). Un video non si confronta mai con una foto.
/// </summary>
public sealed class VideoTests : IDisposable
{
    private const int Size = 2_000_000;   // più dei 16 blocchi da 64 KB: la verifica rapida legge solo i campioni
    private const int Chunk = 64 * 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-video-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _video = new byte[Size];

    public VideoTests()
    {
        Directory.CreateDirectory(_dir);
        new Random(7).NextBytes(_video);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>Scrive il video, con un byte cambiato a <paramref name="changedAt"/> se indicato.</summary>
    private void Save(string name, int? changedAt = null)
    {
        var bytes = (byte[])_video.Clone();
        if (changedAt is { } at) bytes[at] ^= 0xFF;
        File.WriteAllBytes(P(name), bytes);
    }

    /// <summary>Un punto letto dalla verifica rapida (dentro il sesto blocco) e uno che non legge (tra il primo e il secondo).</summary>
    private static int Sampled => (int)ExactMatcher.SampleBlocks(Size, Chunk, ScanOptions.VideoSampleBlocks).ElementAt(5).Offset + 10;
    private const int NotSampled = 100_000;

    private ScanOptions Options(bool videos = true, VideoCheck check = VideoCheck.Full, RunMode mode = RunMode.ReadOnly) => new()
    {
        Roots = { _dir },
        IncludeVideos = videos,
        VideoCheck = check,
        Mode = mode,
        QuarantineRoot = P("Q"),
        CachePath = null,
    };

    private static ScanResult Scan(ScanOptions o) => new DedupEngine(new PpmDecoder(), new FakeMetadata()).Run(o);

    private static string[] Names(DuplicateGroup g) => g.AllFiles.Select(f => Path.GetFileName(f.Path)).Order().ToArray();

    [Fact]
    public void Videos_are_left_out_unless_asked()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        var r = Scan(Options(videos: false));
        Assert.Empty(r.Files);
        Assert.Empty(r.Groups);
    }

    [Fact]
    public void The_full_check_finds_byte_identical_videos_only()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        Save("mare ritoccato.mp4", changedAt: NotSampled);
        var r = Scan(Options());

        var g = Assert.Single(r.Groups);
        Assert.Equal(["mare (1).mp4", "mare.mp4"], Names(g));
        Assert.All(g.Duplicates, d => Assert.Equal((MatchKind.ExactBytes, 100.0), (d.Kind, d.Confidence)));
        Assert.All(r.Files, f => Assert.False(f.IsAnalyzed)); // mai decodificati
    }

    [Fact]
    public void The_quick_check_compares_sixteen_blocks_spread_over_the_video()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        Save("diverso in un campione.mp4", changedAt: Sampled);
        Save("diverso fuori dai campioni.mp4", changedAt: NotSampled);
        var r = Scan(Options(check: VideoCheck.Quick));

        // Il limite dichiarato della verifica rapida: una differenza fuori dai 16 blocchi non si vede. Per questo vale 99%.
        var g = Assert.Single(r.Groups);
        Assert.Equal(["diverso fuori dai campioni.mp4", "mare (1).mp4", "mare.mp4"], Names(g));
        Assert.All(g.Duplicates, d => Assert.Equal((MatchKind.VideoSampled, 99.0), (d.Kind, d.Confidence)));
        Assert.All(r.Files, f => Assert.Null(f.FullHash));     // nessun video letto per intero
    }

    [Fact]
    public void A_video_and_a_photo_are_never_copies_even_with_the_same_bytes()
    {
        Save("mare.mp4");
        Save("mare.jpg");
        Assert.Empty(Scan(Options()).Groups);
        Assert.Empty(Scan(Options(check: VideoCheck.Quick)).Groups);
    }

    [Fact]
    public void Semi_automatic_moves_identical_videos_but_never_quick_matches()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        var full = Options(mode: RunMode.SemiAutomatic);
        Assert.Equal(1, new ActionExecutor(full, prompt: null).Execute(Scan(full)).Moved);

        Save("mare (1).mp4");
        var quick = Options(check: VideoCheck.Quick, mode: RunMode.SemiAutomatic);
        var s = new ActionExecutor(quick, prompt: null).Execute(Scan(quick));
        Assert.Equal(0, s.Moved);
        Assert.Equal(1, s.AwaitingReview);
        Assert.True(File.Exists(P("mare (1).mp4")));
    }

    [Fact]
    public void Before_moving_a_quick_match_its_sixteen_blocks_are_compared_again()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        var o = Options(check: VideoCheck.Quick, mode: RunMode.Assisted);
        var g = Assert.Single(Scan(o).Groups);
        var dup = g.Duplicates.Single();

        // Cambia un byte in un campione, con peso e data come prima.
        var when = File.GetLastWriteTimeUtc(dup.File.Path);
        Save(Path.GetFileName(dup.File.Path), changedAt: Sampled);
        File.SetLastWriteTimeUtc(dup.File.Path, when);

        using var session = new ActionSession(o);
        Assert.Equal(MoveResult.ByteCheckFailed, session.Move(g.Keeper, dup, automatic: false).Result);
        Assert.True(File.Exists(dup.File.Path));
    }

    [Fact]
    public void Choosing_the_other_copy_keeps_the_quick_match()
    {
        Save("mare.mp4");
        Save("mare (1).mp4");
        var o = Options(check: VideoCheck.Quick);
        var g = Assert.Single(Scan(o).Groups);
        GroupEditor.ChangeKeeper(g, g.Duplicates[0].File, o);
        Assert.Equal((MatchKind.VideoSampled, 99.0), (g.Duplicates[0].Kind, g.Duplicates[0].Confidence));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16 * Chunk)]
    public void A_short_video_is_sampled_whole(long size)
    {
        Assert.Equal([(0L, (int)size)], ExactMatcher.SampleBlocks(size, Chunk, 16));
    }

    [Fact]
    public void The_sixteen_blocks_go_from_the_start_to_the_end()
    {
        var blocks = ExactMatcher.SampleBlocks(Size, Chunk, 16).ToList();
        Assert.Equal(16, blocks.Count);
        Assert.Equal(0, blocks[0].Offset);
        Assert.Equal(Size - Chunk, blocks[^1].Offset);
        Assert.All(blocks, b => Assert.Equal(Chunk, b.Count));
        Assert.Equal(blocks.Select(b => b.Offset).Order(), blocks.Select(b => b.Offset));
    }
}
