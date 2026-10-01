using DupliFoto.Core;
using Xunit;

namespace DupliFoto.Tests;

/// <summary>
/// Dove stanno i file di lavoro (accanto all'exe), il passaggio dai posti delle versioni precedenti, la pulizia delle
/// copie scompattate e lo script.
/// </summary>
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
    public void Working_files_always_go_next_to_the_exe()
    {
        string exe = Path.Combine(_dir, "Chiavetta");
        Directory.CreateDirectory(exe);
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto");

        Assert.Equal(Path.Combine(exe, "DupliFoto-dati"), AppFiles.ResolveFolder(exe));
        Assert.True(Directory.Exists(Path.Combine(exe, "DupliFoto-dati")));
        Assert.Empty(Directory.GetFiles(Path.Combine(exe, "DupliFoto-dati")));   // la prova di scrittura non lascia file

        // Accanto all'exe non si può scrivere (qui: al posto della cartella c'è un file): si ripiega su %LOCALAPPDATA%.
        string readOnly = Path.Combine(_dir, "CD");
        Directory.CreateDirectory(readOnly);
        File.WriteAllText(Path.Combine(readOnly, "DupliFoto-dati"), "");
        Assert.Equal(fallback, AppFiles.ResolveFolder(readOnly));
        Assert.Equal(fallback, AppFiles.ResolveFolder(null));

        // Con "dotnet DupliFoto.dll" la cartella del programma è quella della DLL, non quella di dotnet.
        Assert.Equal(exe, AppFiles.ProgramFolder(Path.Combine(_dir, "sdk", "dotnet.exe"), exe + Path.DirectorySeparatorChar));
        Assert.Equal(exe, AppFiles.ProgramFolder(Path.Combine(exe, "DupliFoto.exe"), "altrove"));
    }

    /// <summary>Le cartelle delle versioni precedenti, con i loro file e uno che DupliFoto non ha mai scritto.</summary>
    private (string Folder, string Local, string Roaming) OldVersions()
    {
        string local = Path.Combine(_dir, "Local", "DupliFoto"), roaming = Path.Combine(_dir, "Roaming", "DupliFoto");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(roaming);
        File.WriteAllText(Path.Combine(local, "gui.json"), "impostazioni 0.3.0");
        File.WriteAllText(Path.Combine(local, "cache-v1.json"), "cache");
        File.WriteAllText(Path.Combine(local, "errori.log"), "registro");
        File.WriteAllText(Path.Combine(local, "cache-v1.json.tmp"), "avanzo");
        File.WriteAllText(Path.Combine(local, AppFiles.CleanupScriptName), "script vecchio");
        File.WriteAllText(Path.Combine(roaming, "gui.json"), "impostazioni 0.2");
        return (Path.Combine(_dir, "Chiavetta", "DupliFoto-dati"), local, roaming);
    }

    [Fact]
    public void Files_of_older_versions_move_next_to_the_exe_and_leave_the_PC()
    {
        var (folder, local, roaming) = OldVersions();
        File.WriteAllText(Path.Combine(roaming, "foto.jpg"), "non è di DupliFoto");

        AppFiles.Prepare(folder, local, roaming);

        Assert.Equal("impostazioni 0.3.0", File.ReadAllText(Path.Combine(folder, "gui.json")));   // le più recenti
        Assert.Equal("cache", File.ReadAllText(Path.Combine(folder, "cache-v1.json")));
        Assert.Equal("registro", File.ReadAllText(Path.Combine(folder, "errori.log")));
        Assert.Equal(AppFiles.CleanupScript, File.ReadAllText(Path.Combine(folder, AppFiles.CleanupScriptName)));
        Assert.Equal(4, Directory.GetFiles(folder).Length);
        Assert.False(Directory.Exists(local));                                     // vuota, quindi tolta
        Assert.Equal(["foto.jpg"], Directory.GetFiles(roaming).Select(Path.GetFileName)); // si toccano solo i nostri file
    }

    [Fact]
    public void Newer_files_next_to_the_exe_win_over_the_old_ones()
    {
        var (folder, local, roaming) = OldVersions();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "gui.json"), "impostazioni attuali");

        AppFiles.Prepare(folder, local, roaming);

        Assert.Equal("impostazioni attuali", File.ReadAllText(Path.Combine(folder, "gui.json")));
        Assert.False(Directory.Exists(local));
        Assert.False(Directory.Exists(roaming));
    }

    [Fact]
    public void When_the_exe_folder_is_read_only_the_old_local_folder_stays_in_use()
    {
        var (_, local, roaming) = OldVersions();
        File.Delete(Path.Combine(local, "gui.json"));

        AppFiles.Prepare(local, local, roaming);   // si ripiega proprio su %LOCALAPPDATA%\DupliFoto

        Assert.Equal("impostazioni 0.2", File.ReadAllText(Path.Combine(local, "gui.json")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(local, "cache-v1.json")));
        Assert.False(Directory.Exists(roaming));
    }

    [Fact]
    public void Only_an_extracted_copy_of_DupliFoto_is_removed_at_exit()
    {
        string netBase = Path.Combine(_dir, ".net");
        string copy = Extracted("DupliFoto-9.9.9-x64", "abc123", "finta");
        string exe = Path.Combine(_dir, "Chiavetta", "DupliFoto-9.9.9-x64.exe");

        Assert.Equal(copy, AppFiles.ExtractionOf(copy + Path.DirectorySeparatorChar, exe, netBase));
        Assert.Null(AppFiles.ExtractionOf(copy, Path.Combine(_dir, "Altro.exe"), netBase));         // non è la copia di questo exe
        Assert.Null(AppFiles.ExtractionOf(copy, exe, Path.Combine(_dir, "altrove")));              // non è in %TEMP%\.net
        Assert.Null(AppFiles.ExtractionOf(Path.Combine(_dir, "Chiavetta"), exe, netBase));         // gira da una cartella normale
        Assert.Null(AppFiles.ExtractionOf(Extracted("DupliFoto-9.9.9-x64", "senza", core: null), exe, netBase));
        Assert.Null(AppFiles.ExtractionOf(copy, null, netBase));

        string args = AppFiles.RemovalArguments(copy, netBase);
        // Prima la rinomina, che riesce solo quando nessuno la usa; rd /s solo sulla copia rinominata, le cartelle sopra solo se vuote.
        Assert.Matches("""^/d /s /c "for /l %i in \(1,1,120\) do @\(ren ".+" ".+\.duplifoto-da-cancellare-[0-9a-f]{6}" 2>nul && \(""", args);
        Assert.Contains($"rd /s /q \"{copy}.duplifoto-da-cancellare-", args);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(args, "rd /s"));
        Assert.Contains($"rd \"{Path.GetDirectoryName(copy)}\" 2>nul", args);
        Assert.Contains($"rd \"{netBase}\" 2>nul", args);
        Assert.Throws<ArgumentException>(() => AppFiles.RemovalArguments(Path.Combine(_dir, "100%", "abc"), netBase));
    }

    [Fact]
    public void Cleanup_script_is_plain_ascii_and_never_touches_the_photos()
    {
        string data = Path.Combine(_dir, "dati");
        AppFiles.Prepare(data, Path.Combine(_dir, "nessuna"), Path.Combine(_dir, "nessuna"));
        string script = Path.Combine(data, AppFiles.CleanupScriptName);
        byte[] bytes = File.ReadAllBytes(script);

        Assert.All(bytes, b => Assert.True(b < 128, "solo ASCII: il Prompt dei comandi legge i .bat nella codepage della console"));
        string text = File.ReadAllText(script);
        Assert.Contains("\r\n", text);
        Assert.Contains("DupliFoto.Core.dll", text);                     // riconosce le copie scompattate di ogni versione
        Assert.Contains(@"rd /s /q ""%APPDATA%\DupliFoto""", text);      // impostazioni delle versioni 0.2 e precedenti
        Assert.Contains("(goto) 2>nul & del /f /q \"%~f0\" & rd \"%SELF%\" 2>nul", text);  // per ultimo sé stesso
        Assert.Contains("if /i not \"%%~nxD\"==\"Report\"", text);                       // i report restano
        Assert.DoesNotContain("rd /s /q \"%SELF%\"", text);
        var deletions = text.Split("\r\n").Where(l => l.Contains("rd ") || l.Contains("del ")).ToList();
        Assert.DoesNotContain(deletions, l => l.Contains("Quarantena") || l.Contains("Documenti") || l.Contains(".exe"));

        // Non si riscrive se è già aggiornato.
        File.SetLastWriteTimeUtc(script, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AppFiles.Prepare(data, Path.Combine(_dir, "nessuna"), Path.Combine(_dir, "nessuna"));
        Assert.Equal(2020, File.GetLastWriteTimeUtc(script).Year);
    }
}
