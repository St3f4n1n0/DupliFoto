using System.Runtime.InteropServices;
using DupliFoto.Core.Matching;
using DupliFoto.Core.Scanning;

namespace DupliFoto.Core.Actions;

/// <summary>Esito dello spostamento di un singolo file.</summary>
public enum MoveResult
{
    Moved,
    /// <summary>Il file è già stato spostato in questa sessione (o coincide con la copia da tenere).</summary>
    AlreadyHandled,
    /// <summary>La copia da tenere non esiste più o è cambiata: non si tocca nulla.</summary>
    KeeperUnavailable,
    /// <summary>Il file è cambiato dopo la scansione (peso o data).</summary>
    ChangedSinceScan,
    /// <summary>La verifica byte per byte dei file "identici" è fallita.</summary>
    ByteCheckFailed,
    /// <summary>È lo stesso file della copia da tenere, raggiunto da un altro percorso: spostarlo toglierebbe entrambi.</summary>
    SameFile,
    /// <summary>Errore del file system (permessi, file aperto, disco pieno...).</summary>
    Failed,
}

public sealed record MoveOutcome(MoveResult Result, string Message)
{
    public bool Moved => Result == MoveResult.Moved;
}

/// <summary>Le regole delle modalità, condivise da riga di comando e interfaccia grafica.</summary>
public static class ActionPolicy
{
    /// <summary>Un doppione si può spostare senza chiedere, nella modalità scelta?</summary>
    public static bool IsAutomatic(ScanOptions o, GroupMember m) => o.Mode switch
    {
        RunMode.SemiAutomatic => m.Kind == MatchKind.ExactBytes,
        RunMode.Automatic => m.Confidence >= o.EffectiveAutoThreshold && m.Kind != MatchKind.Burst,
        _ => false,
    };
}

/// <summary>
/// Una sessione di spostamenti con un solo registro. Ogni singolo spostamento applica TUTTE le regole di sicurezza:
/// 1. la copia da tenere deve esistere ed essere invariata;
/// 2. il file da spostare deve essere invariato dalla scansione (peso e data);
/// 3. i file "identici" vengono riconfrontati byte per byte subito prima;
/// 4. non deve essere lo stesso file della copia da tenere raggiunto da un altro percorso, e dopo lo spostamento
///    la copia da tenere deve essere ancora al suo posto (altrimenti il file torna subito dov'era);
/// 5. niente viene cancellato: solo spostato in quarantena o nel Cestino, e annotato nel registro.
/// Non è thread-safe: gli spostamenti vanno fatti uno alla volta.
/// </summary>
public sealed class ActionSession(ScanOptions options, IProgress<string>? progress = null) : IDisposable
{
    // Data e ora, più un suffisso casuale: due sessioni ravvicinate (per esempio "annulla" e subito dopo
    // un nuovo spostamento) non devono mai condividere registro e cartella.
    private readonly string _stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private ActionJournal? _journal;

    public ActionSummary Summary { get; } = new();

    /// <summary>Il registro per annullare. Il file viene creato solo al primo spostamento.</summary>
    public string JournalPath => Path.Combine(options.QuarantineRoot, $"registro-{_stamp}.jsonl");

    private string SessionDir => Path.Combine(options.QuarantineRoot, _stamp);

    public bool WasRemoved(PhotoFile f) => _removed.Contains(f.Path);

    /// <summary>La copia da tenere è ancora al suo posto e invariata?</summary>
    public bool IsKeeperAvailable(PhotoFile keeper) => !WasRemoved(keeper) && IsUnchanged(keeper);

    /// <summary>Sposta <paramref name="member"/> tenendo <paramref name="keeper"/>, dopo tutte le verifiche.</summary>
    public MoveOutcome Move(PhotoFile keeper, GroupMember member, bool automatic)
    {
        var f = member.File;
        if (ReferenceEquals(f, keeper) || WasRemoved(f))
            return new MoveOutcome(MoveResult.AlreadyHandled, "già spostato");

        if (!IsKeeperAvailable(keeper))
            return Warn(MoveResult.KeeperUnavailable, $"La copia da tenere non è più disponibile o è cambiata, non tocco: {f.Path}");
        if (!IsUnchanged(f))
            return Warn(MoveResult.ChangedSinceScan, $"Modificato dopo la scansione, saltato: {f.Path}");
        if (FileIdentity.AreSameFile(keeper.Path, f.Path))
            return Warn(MoveResult.SameFile, $"È lo stesso file della copia da tenere ({keeper.Path}), raggiunto da un altro percorso: non lo sposto: {f.Path}");
        if (member.Kind == MatchKind.ExactBytes && !ExactMatcher.FilesAreIdentical(keeper.Path, f.Path))
            return Warn(MoveResult.ByteCheckFailed, $"La verifica byte per byte è fallita, saltato: {f.Path}");

        try
        {
            string? movedTo = Dispose(f.Path);

            // Ultima difesa: se ora la copia da tenere non è più al suo posto, i due percorsi portavano allo stesso
            // file per una strada non riconosciuta prima. Il file torna subito dov'era.
            if (!IsUnchanged(keeper) && movedTo is not null && TryPutBack(movedTo, f.Path))
                return Warn(MoveResult.SameFile, $"Era lo stesso file della copia da tenere ({keeper.Path}): rimesso subito al suo posto: {f.Path}");

            _journal ??= new ActionJournal(JournalPath);
            Summary.JournalPath = JournalPath;
            _journal.Write(new JournalEntry(DateTime.UtcNow, options.Disposal, f.Path, movedTo, f.Size,
                keeper.Path, member.Kind, member.Confidence, automatic));
            _removed.Add(f.Path);
            Summary.Moved++;
            Summary.BytesFreed += f.Size;
            if (!IsUnchanged(keeper)) // non si è potuto rimettere a posto: è nel registro, "annulla" lo riporta
                Summary.Warnings.Add($"ATTENZIONE: dopo aver spostato {f.Path} la copia da tenere {keeper.Path} non c'è più. " +
                                     (movedTo is null ? "Ripristina il file dal Cestino." : "Riportalo al suo posto con «annulla»."));
            progress?.Report($"{(automatic ? "[auto]" : "[ok]  ")} {f.Path}");
            return new MoveOutcome(MoveResult.Moved, movedTo is null ? "nel Cestino" : $"in quarantena: {movedTo}");
        }
        catch (Exception ex)
        {
            return Warn(MoveResult.Failed, $"Impossibile spostare {f.Path}: {ex.Message}");
        }
    }

    private MoveOutcome Warn(MoveResult result, string message)
    {
        Summary.Warnings.Add(message);
        return new MoveOutcome(result, message);
    }

    private static bool TryPutBack(string movedTo, string original)
    {
        try
        {
            if (File.Exists(original)) return false;
            File.Move(movedTo, original);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool IsUnchanged(PhotoFile f)
    {
        var fi = new FileInfo(f.Path);
        return fi.Exists && fi.Length == f.Size && fi.LastWriteTimeUtc == f.LastWriteUtc;
    }

    private string? Dispose(string path)
    {
        if (options.Disposal == DisposalMethod.RecycleBin)
        {
            RetryWhileLocked(() => RecycleBin.Send(path));
            return null;
        }

        // In quarantena si ricrea la struttura originale: C:\Foto\a.jpg -> <quarantena>\<data>\C\Foto\a.jpg
        string relative = Path.GetFullPath(path).Replace(":", "").TrimStart('\\', '/');
        string dest = Path.Combine(SessionDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (int i = 2; File.Exists(dest); i++)
            dest = Path.Combine(Path.GetDirectoryName(dest)!, $"{Path.GetFileNameWithoutExtension(relative)} ~{i}{Path.GetExtension(relative)}");
        RetryWhileLocked(() => File.Move(path, dest));
        return dest;
    }

    /// <summary>
    /// Su Windows un file può restare aperto per un attimo da altri programmi (antivirus, indicizzazione,
    /// anteprime di Esplora risorse): si ritenta qualche volta prima di arrendersi.
    /// </summary>
    private static void RetryWhileLocked(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException ex) when (attempt < 5 && IsTransientLock(ex))
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    // ERROR_SHARING_VIOLATION (32) ed ERROR_LOCK_VIOLATION (33)
    private static bool IsTransientLock(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    public void Dispose() => _journal?.Dispose();
}

/// <summary>
/// Cestino di Windows. Mai una cancellazione definitiva: se Windows non può mettere il file nel Cestino
/// (unità di rete o rimovibile, Cestino disattivato o troppo piccolo), il file resta dov'è.
/// </summary>
internal static class RecycleBin
{
    public static void Send(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Il Cestino è disponibile solo su Windows: usa la quarantena.");

        // Sulle unità di rete e rimovibili Windows non ha un Cestino: "eliminare" vorrebbe dire cancellare davvero.
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.DriveType != DriveType.Fixed)
            throw new IOException($"L'unità {drive.Name} non ha un Cestino ({drive.DriveType}): usa la quarantena.");

        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            // ALLOWUNDO = Cestino. WANTNUKEWARNING: se il file verrebbe cancellato per sempre (Cestino disattivato
            // o pieno), Windows chiede conferma invece di procedere in silenzio.
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT
                     | NativeMethods.FOF_NOERRORUI | NativeMethods.FOF_WANTNUKEWARNING,
        };
        // Le API della shell vogliono un thread STA; i thread del pool (e il Main della console) sono MTA.
        int rc = -1;
        Exception? error = null;
        var sta = new Thread(() =>
        {
            try { rc = NativeMethods.SHFileOperation(ref op); }
            catch (Exception ex) { error = ex; }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        if (error is not null) throw new IOException($"Cestino non disponibile: {error.Message}", error);
        if (rc != 0 || op.fAnyOperationsAborted)
            throw new IOException(
                $"Windows non ha spostato il file nel Cestino (codice {rc}{(op.fAnyOperationsAborted ? ", annullato" : "")}).",
                rc is 32 or 33 ? unchecked((int)0x80070000) | rc : -1); // file bloccato: si potrà ritentare
        if (File.Exists(path))
            throw new IOException("Il file è ancora al suo posto dopo lo spostamento nel Cestino.");
    }

    private static class NativeMethods
    {
        public const uint FO_DELETE = 3;
        public const ushort FOF_SILENT = 0x0004;
        public const ushort FOF_NOCONFIRMATION = 0x0010;
        public const ushort FOF_ALLOWUNDO = 0x0040;
        public const ushort FOF_NOERRORUI = 0x0400;
        public const ushort FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;   // elenco terminato da un doppio carattere nullo
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
