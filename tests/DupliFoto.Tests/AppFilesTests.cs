using DupliFoto.Core;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>Dove stanno i file di lavoro, la versione portatile, la pulizia delle versioni vecchie e lo script.</summary>
public sealed class AppFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "duplifoto-file-" + Guid.NewGuid().ToString("N"));

    public AppFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* pulizia best effort */ }
    }

    /// <summary>Una copia scompattata finta: %TEMP%\.net\&lt;nome dell'exe&gt;\&lt;codice&gt;, con o senza la DLL che la riconosce.</summary>
    private string Extracted(string exeName, string id, string? core)
    {
        string folder = Path.Combine(_dir, ".net", exeName, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "altro.dll"), "x");
        if (core == "finta") File.WriteAllText(Path.Combine(folder, "DupliFoto.Core.dll"), "versione vecchia");
        else if (core is not null) File.Copy(core, Path.Combine(folder, "DupliFoto.Core.dll"));
        return folder;
    }

    [Fact]
    public void Old_extracted_copies_go_away_and_the_current_version_stays()
    {
        string realCore = typeof(AppFiles).Assembly.Location;
        string? version = AppFiles.VersionOf(realCore);
        Assert.False(string.IsNullOrEmpty(version));

        string current = Extracted("DupliFoto-9.9.9-x64", "attuale", realCore);
        string sameVersionCli = Extracted("duplifoto-cli-9.9.9-x64", "cli", realCore);  // stessa versione, l'altro exe
        Extracted("DupliFoto-0.1.0-x64", "vecchia", "finta");
        Extracted("foto", "rinominata", "finta");                                      // un exe vecchio rinominato
        string leftover = Path.Combine(_dir, ".net", "DupliFoto-0.1.1-x64", "abc.duplifoto-da-cancellare-123456");
        Directory.CreateDirectory(leftover);                                           // pulizia interrotta, senza più la DLL
        File.WriteAllText(Path.Combine(leftover, "resto.dll"), "x");
        string otherApp = Extracted("AltroProgramma", "suo", core: null);              // non è di DupliFoto

        int removed = AppFiles.RemoveOldExtractions(Path.Combine(_dir, ".net"), current, version);

        Assert.Equal(3, removed);
        Assert.True(Directory.Exists(current));
        Assert.True(Directory.Exists(sameVersionCli));
        Assert.True(Directory.Exists(otherApp));
        Assert.Equal(["AltroProgramma", "DupliFoto-9.9.9-x64", "duplifoto-cli-9.9.9-x64"],
            Directory.GetDirectories(Path.Combine(_dir, ".net")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Portable_marker_keeps_the_files_next_to_the_exe()
    {
        string exe = Path.Combine(_dir, "Chiavetta");
        Directory.CreateDirectory(exe);
        string normal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto");

        Assert.Equal(normal, AppFiles.ResolveFolder(exe));
        File.WriteAllText(Path.Combine(exe, AppFiles.PortableMarker), "");
        Assert.Equal(Path.Combine(exe, "DupliFoto-dati"), AppFiles.ResolveFolder(exe));
        Assert.Equal(normal, AppFiles.ResolveFolder(null));
    }

    [Fact]
    public void Cleanup_script_is_plain_ascii_and_never_touches_the_photos()
    {
        string data = Path.Combine(_dir, "dati");
        AppFiles.Prepare(data);
        string script = Path.Combine(data, AppFiles.CleanupScriptName);
        byte[] bytes = File.ReadAllBytes(script);

        Assert.All(bytes, b => Assert.True(b < 128, "solo ASCII: il Prompt dei comandi legge i .bat nella codepage della console"));
        string text = File.ReadAllText(script);
        Assert.Contains("\r\n", text);
        Assert.Contains("DupliFoto.Core.dll", text);                     // riconosce le copie scompattate di ogni versione
        Assert.Contains(@"rd /s /q ""%APPDATA%\DupliFoto""", text);      // impostazioni delle versioni 0.2 e precedenti
        Assert.Contains("(goto) 2>nul & rd /s /q \"%SELF%\"", text);      // per ultima la sua cartella
        var deletions = text.Split("\r\n").Where(l => l.Contains("rd ") || l.Contains("del ")).ToList();
        Assert.DoesNotContain(deletions, l => l.Contains("Quarantena") || l.Contains("Documenti") || l.Contains(".exe"));

        // Non si riscrive se è già aggiornato.
        File.SetLastWriteTimeUtc(script, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AppFiles.Prepare(data);
        Assert.Equal(2020, File.GetLastWriteTimeUtc(script).Year);
    }
}
