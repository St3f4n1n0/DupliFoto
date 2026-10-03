using System.Globalization;
using DupliFoto.Core;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Reporting;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>La lingua: scelta automatica, testi nelle due lingue, report e script di pulizia in inglese.</summary>
[Collection(typeof(InEnglish))]
public sealed class LangTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-lingua-" + Guid.NewGuid().ToString("N"));

    public LangTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Lang.Set(Lang.Italian);
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    [Theory]
    [InlineData("it-IT", false)]
    [InlineData("it-CH", false)]
    [InlineData("en-US", true)]
    [InlineData("de-DE", true)]
    [InlineData("fr-FR", true)]
    public void Automatic_is_Italian_only_on_an_Italian_Windows(string culture, bool english) =>
        Assert.Equal(english, Lang.Detect(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Texts_follow_the_language_even_after_they_were_made()
    {
        var a = new PhotoFile { Path = Path.Combine(_dir, "a.jpg"), Size = 10, FullHash = (UInt128)42 };
        var b = new PhotoFile { Path = Path.Combine(_dir, "b.jpg"), Size = 10, FullHash = (UInt128)42 };
        var match = SimilarityScorer.Compare(a, b, new ScanOptions())!.Value;

        Assert.Equal("identici al byte", match.Reason.ToString());
        Assert.Equal("Identici al byte", ReportWriter.KindLabel(MatchKind.ExactBytes));
        int changes = 0;
        Lang.Changed += Count;
        Lang.Set(Lang.English);
        Lang.Set(Lang.English); // nessun cambio, nessun avviso
        Lang.Changed -= Count;
        Assert.Equal(1, changes);
        Assert.Equal("byte-identical", match.Reason.ToString()); // lo stesso motivo, ora in inglese
        Assert.Equal("Byte-identical", ReportWriter.KindLabel(MatchKind.ExactBytes));
        Assert.Equal("one, two", (string)Text.Join(", ", [new("uno", "one"), new("due", "two")]));
        Assert.Equal("1.5 MB", ReportWriter.FormatBytes(1_572_864));
        Lang.Set(Lang.Italian);
        Assert.Equal("1,5 MB", ReportWriter.FormatBytes(1_572_864));

        void Count() => changes++;
    }

    [Fact]
    public void Report_in_English()
    {
        var keep = new PhotoFile { Path = Path.Combine(_dir, "a.jpg"), Size = 10 };
        var dup = new PhotoFile { Path = Path.Combine(_dir, "a (1).jpg"), Size = 10 };
        var result = new ScanResult
        {
            Files = [keep, dup],
            Elapsed = TimeSpan.FromSeconds(3),
            Groups = [new DuplicateGroup
            {
                Id = 1, Keeper = keep, KeeperReason = new("percorso più breve", "shorter path"),
                Duplicates = [new GroupMember { File = dup, Kind = MatchKind.ExactBytes, Confidence = 100, Reason = new("copia", "copy") }],
            }],
        };
        Lang.Set(Lang.English);
        string csv = Path.Combine(_dir, "r.csv"), html = Path.Combine(_dir, "r.html");
        ReportWriter.WriteCsv(result, csv);
        ReportWriter.WriteHtml(result, html);

        var lines = File.ReadAllLines(csv);
        Assert.StartsWith("group;role;kind;confidence;reason;path", lines[0]);
        Assert.StartsWith("1;KEEP;Byte-identical;;\"shorter path\"", lines[1]);
        Assert.StartsWith("1;duplicate;Byte-identical;100;\"copy\"", lines[2]);
        string page = File.ReadAllText(html);
        Assert.Contains("<html lang=\"en\">", page);
        Assert.Contains("Photos analysed", page);
        Assert.DoesNotContain("Foto analizzate", page);
    }

    [Fact]
    public void Cleanup_script_in_English_is_plain_ascii_and_does_the_same()
    {
        Lang.Set(Lang.English);
        string data = Path.Combine(_dir, "data");
        AppFiles.Prepare(data, Path.Combine(_dir, "none"), Path.Combine(_dir, "none"));
        string script = Path.Combine(data, AppFiles.CleanupScriptName);
        Assert.All(File.ReadAllBytes(script), b => Assert.True(b < 128));
        string english = File.ReadAllText(script);
        Assert.Contains("Clean up DupliFoto", english);
        Assert.Contains("choice /c YN", english);

        Lang.Set(Lang.Italian);
        AppFiles.Prepare(data, Path.Combine(_dir, "none"), Path.Combine(_dir, "none"));
        string italian = File.ReadAllText(script);
        Assert.Contains("choice /c SN", italian);
        // Gli stessi comandi: cambiano solo i messaggi.
        static IEnumerable<string> Commands(string s) => s.Split("\r\n").Where(l => !l.TrimStart().StartsWith("echo") &&
            !l.TrimStart().StartsWith("rem") && !l.StartsWith("choice"));
        Assert.Equal(Commands(italian), Commands(english));
    }
}
