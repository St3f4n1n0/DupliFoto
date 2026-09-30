using DupliFoto.Core.Matching;

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
/// 4. niente viene cancellato: solo spostato in quarantena o nel Cestino, e annotato nel registro.
/// Non è thread-safe: gli spostamenti vanno fatti uno alla volta.
/// </summary>
public sealed class ActionSession(ScanOptions options, IProgress<string>? progress = null) : IDisposable
{
    private readonly string _stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
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
        if (member.Kind == MatchKind.ExactBytes && !ExactMatcher.FilesAreIdentical(keeper.Path, f.Path))
            return Warn(MoveResult.ByteCheckFailed, $"La verifica byte per byte è fallita, saltato: {f.Path}");

        try
        {
            string? movedTo = Dispose(f.Path);
            _journal ??= new ActionJournal(JournalPath);
            Summary.JournalPath = JournalPath;
            _journal.Write(new JournalEntry(DateTime.UtcNow, options.Disposal, f.Path, movedTo, f.Size,
                keeper.Path, member.Kind, member.Confidence, automatic));
            _removed.Add(f.Path);
            Summary.Moved++;
            Summary.BytesFreed += f.Size;
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

    private static bool IsUnchanged(PhotoFile f)
    {
        var fi = new FileInfo(f.Path);
        return fi.Exists && fi.Length == f.Size && fi.LastWriteTimeUtc == f.LastWriteUtc;
    }

    private string? Dispose(string path)
    {
        if (options.Disposal == DisposalMethod.RecycleBin)
        {
            RecycleBin.Send(path);
            return null;
        }

        // In quarantena si ricrea la struttura originale: C:\Foto\a.jpg -> <quarantena>\<data>\C\Foto\a.jpg
        string relative = Path.GetFullPath(path).Replace(":", "").TrimStart('\\', '/');
        string dest = Path.Combine(SessionDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (int i = 2; File.Exists(dest); i++)
            dest = Path.Combine(Path.GetDirectoryName(dest)!, $"{Path.GetFileNameWithoutExtension(relative)} ~{i}{Path.GetExtension(relative)}");
        File.Move(path, dest);
        return dest;
    }

    public void Dispose() => _journal?.Dispose();
}

internal static class RecycleBin
{
    public static void Send(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Il Cestino è disponibile solo su Windows: usa la quarantena.");
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
