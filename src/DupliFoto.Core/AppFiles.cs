using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DupliFoto.Core;

/// <summary>
/// Tutto quello che DupliFoto scrive per sé (impostazioni, cache delle analisi, registro errori, report) sta nella cartella
/// "DupliFoto-dati" accanto all'exe, che sia su una chiavetta o sul disco: sul PC non resta niente di sparso. Solo se lì
/// non si può scrivere (un CD, una cartella protetta come Programmi) si ripiega su %LOCALAPPDATA%\DupliFoto.
/// Nella cartella c'è anche «Pulisci DupliFoto.bat», che toglie dal PC i file di lavoro di tutte le versioni.
/// Restano fuori solo la copia del programma che l'exe scompatta in %TEMP% (la toglie l'app alla chiusura, vedi
/// <see cref="RemoveExtractionAtExit"/>) e le foto in quarantena, che sono dell'utente.
/// </summary>
public static class AppFiles
{
    public const string FolderName = "DupliFoto-dati";
    public const string CleanupScriptName = "Pulisci DupliFoto.bat";
    public const string ReportFolderName = "Report";

    /// <summary>Un file che c'è solo nelle copie scompattate di DupliFoto (app e riga di comando, di ogni versione).</summary>
    private const string Signature = "DupliFoto.Core.dll";

    /// <summary>Il suffisso delle copie scompattate rinominate per cancellarle (anche dallo script di pulizia).</summary>
    private const string DoomedSuffix = ".duplifoto-da-cancellare";

    /// <summary>Dove le versioni fino alla 0.3.0 tenevano i file di lavoro; la 0.2 e le precedenti le impostazioni in Roaming.</summary>
    private static readonly string OldLocalFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto");
    private static readonly string OldRoamingFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DupliFoto");

    /// <summary>I file che le versioni precedenti scrivevano nelle loro cartelle: si toccano solo questi, per nome.</summary>
    private static readonly string[] OldFiles = ["gui.json", "cache-v1.json", "errori.log"];
    private static readonly string[] OldLeftovers = [CleanupScriptName, "cache-v1.json.tmp"];

    private static readonly string? ExeFolder = ProgramFolder(Environment.ProcessPath, AppContext.BaseDirectory);

    /// <summary>La cartella dei file di lavoro: "DupliFoto-dati" accanto all'exe (se lì non si può scrivere, %LOCALAPPDATA%\DupliFoto).</summary>
    public static string Folder { get; } = ResolveFolder(ExeFolder);

    /// <summary>False quando accanto all'exe non si può scrivere e i file di lavoro sono in %LOCALAPPDATA%\DupliFoto.</summary>
    public static bool IsNextToExe => !SameFolder(Folder, OldLocalFolder);

    /// <summary>I report: nella cartella dei file di lavoro, o in Documenti\DupliFoto se questa è in %LOCALAPPDATA%.</summary>
    public static string ReportFolder => IsNextToExe
        ? Path.Combine(Folder, ReportFolderName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DupliFoto");

    /// <summary>La cartella dell'exe; con "dotnet DupliFoto.dll" (in sviluppo) quella della DLL, non quella di dotnet.</summary>
    internal static string? ProgramFolder(string? processPath, string baseDirectory)
    {
        if (processPath is null) return null;
        string name = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.TrimEndingDirectorySeparator(baseDirectory)
            : Path.GetDirectoryName(processPath);
    }

    internal static string ResolveFolder(string? exeFolder) =>
        exeFolder is not null && CanWriteIn(Path.Combine(exeFolder, FolderName)) is { } folder ? folder : OldLocalFolder;

    /// <summary>Crea la cartella (se manca) e prova a scriverci un file, subito tolto. Null se non si può.</summary>
    private static string? CanWriteIn(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            using (File.Create(Path.Combine(folder, $".prova-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose)) { }
            return folder;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// All'avvio: mette (o aggiorna) «Pulisci DupliFoto.bat» nella cartella dei file di lavoro e vi porta impostazioni,
    /// cache e registro errori delle versioni precedenti, togliendo i loro file dal PC. Va chiamato prima di leggere le
    /// impostazioni. Pochi file piccoli: non rallenta l'avvio.
    /// </summary>
    public static void Prepare() => Prepare(Folder, OldLocalFolder, OldRoamingFolder);

    internal static void Prepare(string folder, string oldLocal, string oldRoaming)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string script = Path.Combine(folder, CleanupScriptName);
            if (!File.Exists(script) || File.ReadAllText(script) != CleanupScript)
                File.WriteAllText(script, CleanupScript, Encoding.ASCII);
        }
        catch (Exception) { /* senza lo script DupliFoto funziona lo stesso */ }

        // Le impostazioni della 0.3.0 (in Local) sono più recenti di quelle della 0.2 (in Roaming): la prima trovata vince.
        string[] sources = SameFolder(folder, oldLocal) ? [oldRoaming] : [oldLocal, oldRoaming];
        foreach (string old in sources)
        {
            foreach (string name in OldFiles) Adopt(Path.Combine(old, name), Path.Combine(folder, name));
            foreach (string name in OldLeftovers) TryDelete(Path.Combine(old, name));
            try { if (Directory.Exists(old) && !Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old); }
            catch (Exception) { /* resta una cartella vuota: la toglie «Pulisci DupliFoto.bat» */ }
        }
    }

    /// <summary>
    /// Porta un file di una versione precedente nella cartella nuova, se lì non c'è già, e lo toglie dal vecchio posto.
    /// Prima la copia con un nome provvisorio e poi la rinomina: chi legge il file nuovo non lo trova mai a metà.
    /// </summary>
    private static void Adopt(string old, string current)
    {
        try
        {
            if (!File.Exists(old)) return;
            if (!File.Exists(current))
            {
                string partial = current + ".da-versione-precedente";
                File.Copy(old, partial, overwrite: true);
                File.Move(partial, current);
            }
            File.Delete(old);
        }
        catch (Exception) { /* resta dov'è: si riprova al prossimo avvio */ }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (Exception) { /* in uso: si riprova al prossimo avvio */ }
    }

    /// <summary>
    /// Toglie le copie scompattate delle versioni precedenti: l'exe portabile, al primo avvio, si scompatta in
    /// %TEMP%\.net\&lt;nome dell'exe&gt;\&lt;codice&gt; e quella cartella resta. Si tengono la copia in uso e le altre della
    /// stessa versione (l'app e la riga di comando). Ogni cartella viene prima rinominata: se una versione vecchia
    /// è ancora aperta, i suoi file sono in uso, Windows rifiuta e la cartella resta intatta.
    /// </summary>
    public static int RemoveOldExtractions() =>
        RemoveOldExtractions(ExtractionBase, LoadedFrom, VersionOf(typeof(AppFiles).Assembly.Location));

    /// <summary>
    /// La cartella da cui girano le DLL del programma: nell'exe portabile, che scompatta tutto, la copia scompattata.
    /// Si guarda dove è stata caricata questa DLL, che non dipende da come .NET imposta la cartella di base.
    /// </summary>
    private static string LoadedFrom =>
        Path.GetDirectoryName(typeof(AppFiles).Assembly.Location) is { Length: > 0 } folder ? folder : AppContext.BaseDirectory;

    /// <summary>Dove l'exe portabile si scompatta: %TEMP%\.net, salvo la variabile di .NET che lo sposta.</summary>
    private static string ExtractionBase =>
        Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Path.GetTempPath(), ".net");

    /// <summary>
    /// Alla chiusura: toglie la copia del programma che l'exe ha scompattato all'avvio (%TEMP%\.net\&lt;nome dell'exe&gt;\&lt;codice&gt;),
    /// così sul PC non resta niente. Finché il programma è aperto quei file sono in uso: li toglie un Prompt dei comandi
    /// nascosto, che ogni secondo prova a rinominare la cartella e, appena ci riesce, la cancella. La rinomina riesce solo
    /// quando nessuno la usa più, né questo processo né un'altra finestra di DupliFoto aperta nel frattempo; dopo due
    /// minuti rinuncia (la toglieranno «Pulisci DupliFoto.bat» o l'avvio di un'altra versione).
    /// Il prezzo: al prossimo avvio l'exe si scompatta di nuovo, qualche secondo in più.
    /// </summary>
    /// <returns>True se la pulizia è partita; false fuori da Windows o quando il programma non gira da una copia scompattata.</returns>
    public static bool RemoveExtractionAtExit()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (ExtractionOf(LoadedFrom, Environment.ProcessPath, ExtractionBase) is not { } copy) return false;
        try
        {
            using var cleaner = Process.Start(new ProcessStartInfo("cmd.exe", RemovalArguments(copy, ExtractionBase))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(), // non la cartella dell'exe: la chiavetta si deve poter togliere
            });
            return cleaner is not null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// La copia scompattata da cui gira questo processo: &lt;base&gt;\&lt;nome dell'exe&gt;\&lt;codice&gt;, con la DLL che la
    /// riconosce. Null quando il programma gira da una cartella normale (in sviluppo, nei test).
    /// </summary>
    internal static string? ExtractionOf(string baseDirectory, string? processPath, string extractionBase)
    {
        if (processPath is null) return null;
        string copy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (Path.GetDirectoryName(copy) is not { } app || Path.GetDirectoryName(app) is not { } root) return null;
        if (!SameFolder(root, extractionBase)) return null;
        string appName = Path.GetFileName(app);
        if (!string.Equals(appName, Path.GetFileNameWithoutExtension(processPath), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(appName, Path.GetFileName(processPath), StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(Path.Combine(copy, Signature)) ? copy : null;
    }

    /// <summary>
    /// Il comando per cmd.exe: fino a 120 tentativi, uno al secondo, di rinominare la copia; appena riesce la cancella,
    /// e con lei le cartelle sopra se restano vuote (rd senza /s non toglie mai una cartella con dentro qualcosa).
    /// </summary>
    internal static string RemovalArguments(string copy, string extractionBase)
    {
        string app = Path.GetDirectoryName(copy)!;
        string doomedName = $"{Path.GetFileName(copy)}{DoomedSuffix}-{Guid.NewGuid().ToString("N")[..6]}";
        string doomed = Path.Combine(app, doomedName);
        string root = Path.TrimEndingDirectorySeparator(extractionBase);
        // Un % nel percorso il Prompt dei comandi lo leggerebbe come variabile: in quel caso si lascia stare.
        if ((copy + root).Contains('%')) throw new ArgumentException("Percorso con %", nameof(copy));
        return $"/d /s /c \"for /l %i in (1,1,120) do @(ren \"{copy}\" \"{doomedName}\" 2>nul && " +
               $"(rd /s /q \"{doomed}\" & rd \"{app}\" 2>nul & rd \"{root}\" 2>nul & exit) || ping -n 2 127.0.0.1 >nul)\"";
    }

    internal static int RemoveOldExtractions(string extractionBase, string currentFolder, string? currentVersion)
    {
        if (!Directory.Exists(extractionBase)) return 0;
        int removed = 0;
        foreach (var app in Subfolders(extractionBase))
        {
            bool touched = false;
            foreach (var copy in Subfolders(app))
            {
                // Avanzi di una pulizia interrotta: hanno già il suffisso, e forse non più la DLL che li riconosce.
                bool leftover = Path.GetFileName(copy).Contains(DoomedSuffix, StringComparison.OrdinalIgnoreCase);
                if (!leftover)
                {
                    string signature = Path.Combine(copy, Signature);
                    if (!File.Exists(signature) || SameFolder(copy, currentFolder)) continue;
                    if (currentVersion is not null && VersionOf(signature) == currentVersion) continue;
                }
                try
                {
                    string doomed = copy;
                    if (!leftover)
                    {
                        doomed = $"{copy}{DoomedSuffix}-{Guid.NewGuid().ToString("N")[..6]}";
                        Directory.Move(copy, doomed);
                    }
                    touched = true;
                    Directory.Delete(doomed, recursive: true);
                    removed++;
                }
                catch (Exception) { /* in uso, o un file è protetto: si riprova al prossimo avvio */ }
            }
            if (touched)
            {
                try { if (!Directory.EnumerateFileSystemEntries(app).Any()) Directory.Delete(app); }
                catch (Exception) { /* non vuota o in uso: va bene così */ }
            }
        }
        return removed;
    }

    private static IEnumerable<string> Subfolders(string folder)
    {
        try { return Directory.GetDirectories(folder); }
        catch (Exception) { return []; }
    }

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>La versione pubblicata scritta in una DLL di DupliFoto ("0.2.0"), senza il commit dopo il "+".</summary>
    internal static string? VersionOf(string dll)
    {
        try { return FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Split('+')[0] is { Length: > 0 } v ? v : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// «Pulisci DupliFoto.bat». Solo caratteri ASCII: il Prompt dei comandi legge i .bat nella codepage della console.
    /// Toglie tutte le copie scompattate (di ogni versione, riconosciute da DupliFoto.Core.dll), le cartelle dei file
    /// di lavoro delle versioni precedenti e, per ultima, la cartella in cui si trova, tranne i report. Non tocca la
    /// quarantena né l'exe. "/si" (o "/yes") salta la conferma.
    /// </summary>
    internal static string CleanupScript => Script(Lang.IsEnglish);

    /// <param name="en">In inglese: con l'app in inglese, anche lo script parla inglese (il nome del file resta lo stesso).</param>
    private static string Script(bool en)
    {
        string T(string it, string english) => en ? english : it;
        return string.Join("\r\n",
            "@echo off",
            "setlocal",
            T("rem Pulisci DupliFoto: toglie da questo PC i file di lavoro di DupliFoto, di tutte le versioni.",
              "rem Pulisci DupliFoto (Clean up DupliFoto): removes DupliFoto's working files from this PC, for all versions."),
            T("rem Non tocca le foto in quarantena ne' i report.", "rem It does not touch the photos in quarantine or the reports."),
            "set \"SELF=%~dp0\"",
            "set \"SELF=%SELF:~0,-1%\"",
            "echo.",
            T("echo   Pulisci DupliFoto", "echo   Clean up DupliFoto"),
            T("echo   =================", "echo   =================="),
            "echo.",
            T("echo   Cancello i file di lavoro di DupliFoto, di tutte le versioni:",
              "echo   Deleting DupliFoto's working files, for all versions:"),
            T("echo    - le copie del programma scompattate in %TEMP%\\.net",
              "echo    - the copies of the program unpacked into %TEMP%\\.net"),
            T("echo    - impostazioni, cache delle analisi e registro errori:",
              "echo    - settings, analysis cache and error log:"),
            "echo        %SELF%",
            T("echo        %LOCALAPPDATA%\\DupliFoto   (versioni 0.3.0 e precedenti)",
              "echo        %LOCALAPPDATA%\\DupliFoto   (versions 0.3.0 and earlier)"),
            T("echo        %APPDATA%\\DupliFoto        (versioni 0.2 e precedenti)",
              "echo        %APPDATA%\\DupliFoto        (versions 0.2 and earlier)"),
            "echo.",
            T("echo   NON tocco le foto in quarantena (Immagini\\DupliFoto-Quarantena, o la cartella",
              "echo   I do NOT touch the photos in quarantine (Pictures\\DupliFoto-Quarantena, or the"),
            T("echo   scelta nell'app) ne' i report (la cartella Report qui, o Documenti\\DupliFoto):",
              "echo   folder chosen in the app) or the reports (the Report folder here, or"),
            T("echo   sono tuoi. Gli exe di DupliFoto restano dove sono.",
              "echo   Documents\\DupliFoto): they are yours. The DupliFoto exe files stay where they are."),
            "echo.",
            T("echo   Prima chiudi DupliFoto.", "echo   Close DupliFoto first."),
            "echo.",
            "if /i \"%~1\"==\"/si\" goto pulisci",
            "if /i \"%~1\"==\"/yes\" goto pulisci",
            T("choice /c SN /m \"  Procedo\"", "choice /c YN /m \"  Go ahead\""),
            "if errorlevel 2 exit /b 1",
            ":pulisci",
            "cd /d \"%TEMP%\"",
            "set \"INUSO=\"",
            T("rem Una copia scompattata e' di DupliFoto se contiene DupliFoto.Core.dll. Prima la si rinomina:",
              "rem An unpacked copy belongs to DupliFoto if it contains DupliFoto.Core.dll. It is renamed first:"),
            T("rem se quella versione e' aperta, Windows rifiuta e la copia resta intatta.",
              "rem if that version is open, Windows refuses and the copy stays intact."),
            "for /d %%A in (\"%TEMP%\\.net\\*\") do (",
            "  for /d %%B in (\"%%A\\*\") do (",
            "    if exist \"%%B\\DupliFoto.Core.dll\" (",
            "      ren \"%%B\" \"%%~nxB.duplifoto-da-cancellare\" 2>nul && (rd /s /q \"%%B.duplifoto-da-cancellare\" 2>nul) || set \"INUSO=1\"",
            "    )",
            "  )",
            T("  rem Avanzi di una pulizia interrotta", "  rem Leftovers of an interrupted clean-up"),
            "  for /d %%B in (\"%%A\\*.duplifoto-da-cancellare*\") do rd /s /q \"%%B\" 2>nul",
            "  rd \"%%A\" 2>nul",
            ")",
            "rd \"%TEMP%\\.net\" 2>nul",
            "if exist \"%APPDATA%\\DupliFoto\" rd /s /q \"%APPDATA%\\DupliFoto\"",
            "if /i not \"%SELF%\"==\"%LOCALAPPDATA%\\DupliFoto\" if exist \"%LOCALAPPDATA%\\DupliFoto\" rd /s /q \"%LOCALAPPDATA%\\DupliFoto\"",
            "echo.",
            "if defined INUSO (",
            T("  echo   Alcune copie del programma sono in uso: chiudi DupliFoto e lancia di nuovo questo file.",
              "  echo   Some copies of the program are in use: close DupliFoto and run this file again."),
            ") else (",
            T("  echo   Fatto: i file di lavoro di DupliFoto sono stati tolti.",
              "  echo   Done: DupliFoto's working files have been removed."),
            ")",
            "echo.",
            "if /i not \"%~1\"==\"/si\" if /i not \"%~1\"==\"/yes\" pause",
            T("rem Per ultima la cartella di questo file, tranne i report; poi il file stesso, e la cartella se resta vuota",
              "rem Last, the folder of this file except the reports; then the file itself, and the folder if it is empty"),
            T("rem (se ci sono i report resta, e non e' un errore: \"(call )\" riporta il codice di uscita a 0).",
              "rem (with reports in it the folder stays, which is not an error: \"(call )\" sets the exit code back to 0)."),
            "for %%F in (\"%SELF%\\*\") do if /i not \"%%~nxF\"==\"%~nx0\" del /f /q \"%%F\" 2>nul",
            "for /d %%D in (\"%SELF%\\*\") do if /i not \"%%~nxD\"==\"Report\" rd /s /q \"%%D\" 2>nul",
            "(goto) 2>nul & del /f /q \"%~f0\" & rd \"%SELF%\" 2>nul || (call )",
            "");
    }
}
