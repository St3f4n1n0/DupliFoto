using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DupliFoto.Core;

/// <summary>
/// I file di lavoro di DupliFoto (impostazioni, cache delle analisi, registro errori) stanno in una cartella sola:
/// - di norma %LOCALAPPDATA%\DupliFoto;
/// - versione portatile: se accanto all'exe c'è il file "DupliFoto.portable", nella cartella "DupliFoto-dati" accanto all'exe.
/// Lì c'è anche «Pulisci DupliFoto.bat», che toglie dal PC tutti i file di lavoro di tutte le versioni.
/// Le foto in quarantena e i report non sono file di lavoro: restano dove l'utente li ha messi.
/// </summary>
public static class AppFiles
{
    public const string PortableMarker = "DupliFoto.portable";
    public const string PortableFolderName = "DupliFoto-dati";
    public const string CleanupScriptName = "Pulisci DupliFoto.bat";

    /// <summary>Un file che c'è solo nelle copie scompattate di DupliFoto (app e riga di comando, di ogni versione).</summary>
    private const string Signature = "DupliFoto.Core.dll";

    /// <summary>Il suffisso delle copie scompattate rinominate per cancellarle (anche dallo script di pulizia).</summary>
    private const string DoomedSuffix = ".duplifoto-da-cancellare";

    private static readonly string? ExeFolder = Path.GetDirectoryName(Environment.ProcessPath);

    public static bool IsPortable { get; } = IsPortableFolder(ExeFolder);
    public static string Folder { get; } = ResolveFolder(ExeFolder);

    /// <summary>Dove le versioni 0.2 e precedenti salvavano le impostazioni (in %APPDATA%, non in %LOCALAPPDATA%).</summary>
    public static string LegacySettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DupliFoto", "gui.json");

    internal static bool IsPortableFolder(string? exeFolder) =>
        exeFolder is not null && File.Exists(Path.Combine(exeFolder, PortableMarker));

    internal static string ResolveFolder(string? exeFolder) => IsPortableFolder(exeFolder)
        ? Path.Combine(exeFolder!, PortableFolderName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto");

    /// <summary>Crea la cartella dei file di lavoro e vi mette (o aggiorna) «Pulisci DupliFoto.bat».</summary>
    public static void Prepare() => Prepare(Folder);

    internal static void Prepare(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string script = Path.Combine(folder, CleanupScriptName);
            if (!File.Exists(script) || File.ReadAllText(script) != CleanupScript)
                File.WriteAllText(script, CleanupScript, Encoding.ASCII);
        }
        catch (Exception) { /* senza lo script DupliFoto funziona lo stesso */ }
    }

    /// <summary>
    /// Toglie le copie scompattate delle versioni precedenti: l'exe portabile, al primo avvio, si scompatta in
    /// %TEMP%\.net\&lt;nome dell'exe&gt;\&lt;codice&gt; e quella cartella resta. Si tengono la copia in uso e le altre della
    /// stessa versione (l'app e la riga di comando). Ogni cartella viene prima rinominata: se una versione vecchia
    /// è ancora aperta, i suoi file sono in uso, Windows rifiuta e la cartella resta intatta.
    /// </summary>
    public static int RemoveOldExtractions() =>
        RemoveOldExtractions(Path.Combine(Path.GetTempPath(), ".net"), AppContext.BaseDirectory, VersionOf(typeof(AppFiles).Assembly.Location));

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
    /// Toglie tutte le copie scompattate (di ogni versione, riconosciute da DupliFoto.Core.dll) e le cartelle dei file
    /// di lavoro, compresa quella in cui si trova; non tocca la quarantena né i report. "/si" salta la conferma.
    /// </summary>
    internal static readonly string CleanupScript = string.Join("\r\n",
        "@echo off",
        "setlocal",
        "rem Pulisci DupliFoto: toglie da questo PC i file di lavoro di DupliFoto, di tutte le versioni.",
        "rem Non tocca le foto in quarantena ne' i report.",
        "set \"SELF=%~dp0\"",
        "set \"SELF=%SELF:~0,-1%\"",
        "echo.",
        "echo   Pulisci DupliFoto",
        "echo   =================",
        "echo.",
        "echo   Cancello i file di lavoro di DupliFoto, di tutte le versioni:",
        "echo    - le copie del programma scompattate in %TEMP%\\.net",
        "echo    - impostazioni, cache delle analisi e registro errori:",
        "echo        %SELF%",
        "echo        %LOCALAPPDATA%\\DupliFoto",
        "echo        %APPDATA%\\DupliFoto   (versioni 0.2 e precedenti)",
        "echo.",
        "echo   NON tocco le foto in quarantena (Immagini\\DupliFoto-Quarantena, o la cartella",
        "echo   scelta nell'app) ne' i report in Documenti\\DupliFoto: sono tuoi.",
        "echo   Gli exe di DupliFoto restano dove sono.",
        "echo.",
        "echo   Prima chiudi DupliFoto.",
        "echo.",
        "if /i \"%~1\"==\"/si\" goto pulisci",
        "choice /c SN /m \"  Procedo\"",
        "if errorlevel 2 exit /b 1",
        ":pulisci",
        "cd /d \"%TEMP%\"",
        "set \"INUSO=\"",
        "rem Una copia scompattata e' di DupliFoto se contiene DupliFoto.Core.dll. Prima la si rinomina:",
        "rem se quella versione e' aperta, Windows rifiuta e la copia resta intatta.",
        "for /d %%A in (\"%TEMP%\\.net\\*\") do (",
        "  for /d %%B in (\"%%A\\*\") do (",
        "    if exist \"%%B\\DupliFoto.Core.dll\" (",
        "      ren \"%%B\" \"%%~nxB.duplifoto-da-cancellare\" 2>nul && (rd /s /q \"%%B.duplifoto-da-cancellare\" 2>nul) || set \"INUSO=1\"",
        "    )",
        "  )",
        "  rem Avanzi di una pulizia interrotta",
        "  for /d %%B in (\"%%A\\*.duplifoto-da-cancellare*\") do rd /s /q \"%%B\" 2>nul",
        "  rd \"%%A\" 2>nul",
        ")",
        "if exist \"%APPDATA%\\DupliFoto\" rd /s /q \"%APPDATA%\\DupliFoto\"",
        "if /i not \"%SELF%\"==\"%LOCALAPPDATA%\\DupliFoto\" if exist \"%LOCALAPPDATA%\\DupliFoto\" rd /s /q \"%LOCALAPPDATA%\\DupliFoto\"",
        "echo.",
        "if defined INUSO (",
        "  echo   Alcune copie del programma sono in uso: chiudi DupliFoto e lancia di nuovo questo file.",
        ") else (",
        "  echo   Fatto: i file di lavoro di DupliFoto sono stati tolti.",
        ")",
        "echo.",
        "if /i not \"%~1\"==\"/si\" pause",
        "rem Per ultima la cartella di questo file, con il file stesso.",
        "(goto) 2>nul & rd /s /q \"%SELF%\"",
        "");
}
