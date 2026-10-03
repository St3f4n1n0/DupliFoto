using DupliFoto.Core;
using DupliFoto.Core.Matching;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>L'avanzamento delle fasi lunghe: "fatti N di M", senza sommergere l'interfaccia di messaggi.</summary>
public sealed class ProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-avanzamento-" + Guid.NewGuid().ToString("N"));

    public ProgressTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    /// <summary>Raccoglie i messaggi subito, senza passare dal thread dell'interfaccia.</summary>
    private sealed class Messages : IProgress<string>
    {
        private readonly List<string> _all = [];
        public IReadOnlyList<string> All { get { lock (_all) return _all.ToList(); } }
        public void Report(string value) { lock (_all) _all.Add(value); }
    }

    [Fact]
    public void Hashes_report_how_many_are_done_out_of_how_many()
    {
        var files = new List<PhotoFile>();
        for (int i = 0; i < 6; i++)
        {
            string path = Path.Combine(_dir, $"foto{i}.jpg");
            File.WriteAllBytes(path, Enumerable.Repeat((byte)(i % 2), 300_000).ToArray()); // tre copie di due contenuti
            files.Add(new PhotoFile { Path = path, Size = 300_000 });
        }
        var messages = new Messages();

        var groups = ExactMatcher.FindExactGroups(files, new ScanOptions(), messages, TestContext.Current.CancellationToken);

        Assert.Equal(2, groups.Count);
        Assert.Contains("Hash parziali: 6 di 6 file...", messages.All);
        Assert.Contains(messages.All, m => m.StartsWith("Hash completi: 6 di 6 file (") && m.EndsWith(" di 1,7 MB)..."));
    }

    [Fact]
    public void Many_quick_steps_give_few_messages_and_always_the_last_one()
    {
        var messages = new Messages();
        var counter = new ProgressCounter(messages, 100_000, (done, total, _) => $"{done} di {total}");
        Parallel.For(0, 100_000, _ => counter.Add());
        counter.Finish();

        Assert.True(messages.All.Count < 20, $"{messages.All.Count} messaggi");
        Assert.Equal("100000 di 100000", messages.All[^1]);
    }
}
