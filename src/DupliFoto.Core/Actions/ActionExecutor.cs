using DupliFoto.Core.Matching;

namespace DupliFoto.Core.Actions;

/// <summary>Risposta dell'utente per un gruppo.</summary>
public enum UserChoice { Apply, Skip, ApplyToAllOfThisKind, Quit }

/// <summary>Una domanda da porre all'utente: il gruppo e i soli membri su cui serve una decisione.</summary>
public sealed record PromptRequest(DuplicateGroup Group, IReadOnlyList<GroupMember> Members);

public sealed record PromptAnswer(UserChoice Choice, PhotoFile? NewKeeper = null);

/// <summary>Interfaccia per chiedere conferma: console oggi, finestra grafica domani.</summary>
public interface IDecisionPrompt
{
    PromptAnswer Ask(PromptRequest request);
}

public sealed class ActionSummary
{
    public int Moved { get; set; }
    public long BytesFreed { get; set; }
    public int AutomaticActions { get; set; }
    public int ConfirmedActions { get; set; }
    public int SkippedByUser { get; set; }
    public int AwaitingReview { get; set; }
    public List<string> Warnings { get; } = new();
    public string? JournalPath { get; set; }
}

/// <summary>
/// Applica le decisioni secondo la modalità scelta. Regole di sicurezza, valide in TUTTE le modalità:
/// 1. la copia da tenere deve esistere ed essere invariata, altrimenti il gruppo viene saltato;
/// 2. ogni file da rimuovere deve essere invariato dalla scansione (peso e data);
/// 3. i file "identici" vengono riconfrontati byte per byte subito prima dell'azione;
/// 4. niente viene cancellato: solo spostato in quarantena o nel Cestino, con registro.
/// </summary>
public sealed class ActionExecutor(ScanOptions options, IDecisionPrompt? prompt, IProgress<string>? progress = null)
{
    public ActionSummary Execute(ScanResult result)
    {
        var summary = new ActionSummary();
        if (options.Mode == RunMode.ReadOnly) return summary;

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string sessionDir = Path.Combine(options.QuarantineRoot, stamp);
        using var journal = new ActionJournal(Path.Combine(options.QuarantineRoot, $"registro-{stamp}.jsonl"));
        summary.JournalPath = journal.Path;

        var approvedKinds = new HashSet<MatchKind>();
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool quit = false;

        foreach (var group in result.Groups)
        {
            if (quit) { summary.AwaitingReview += group.Duplicates.Count; continue; }

            var auto = group.Duplicates.Where(IsAutomatic).ToList();
            var ask = group.Duplicates.Except(auto).ToList();

            if (auto.Count > 0)
            {
                if (Apply(group, group.Keeper, auto, automatic: true, sessionDir, journal, summary, removed) is int n)
                    summary.AutomaticActions += n;
            }

            if (ask.Count == 0) continue;

            // Anche con "sì a tutti", sotto il 60% si chiede sempre.
            if (approvedKinds.Contains(group.Kind) && ask.All(m => m.Confidence >= 60))
            {
                summary.ConfirmedActions += Apply(group, group.Keeper, ask, false, sessionDir, journal, summary, removed) ?? 0;
                continue;
            }

            if (prompt is null)
            {
                summary.AwaitingReview += ask.Count;
                continue;
            }

            var answer = prompt.Ask(new PromptRequest(group, ask));
            switch (answer.Choice)
            {
                case UserChoice.Quit:
                    quit = true;
                    summary.AwaitingReview += ask.Count;
                    break;
                case UserChoice.Skip:
                    summary.SkippedByUser += ask.Count;
                    break;
                case UserChoice.ApplyToAllOfThisKind:
                    approvedKinds.Add(group.Kind);
                    goto case UserChoice.Apply;
                case UserChoice.Apply:
                    var keeper = answer.NewKeeper ?? group.Keeper;
                    var targets = ReferenceEquals(keeper, group.Keeper)
                        ? ask
                        : group.Duplicates.Where(m => !ReferenceEquals(m.File, keeper))
                            .Append(new GroupMember { File = group.Keeper, Kind = group.Kind, Confidence = group.Confidence, Reason = "scelta dell'utente" })
                            .ToList();
                    summary.ConfirmedActions += Apply(group, keeper, targets, false, sessionDir, journal, summary, removed) ?? 0;
                    break;
            }
        }
        return summary;
    }

    /// <summary>Un membro si può trattare senza chiedere?</summary>
    private bool IsAutomatic(GroupMember m) => options.Mode switch
    {
        RunMode.SemiAutomatic => m.Kind == MatchKind.ExactBytes,
        RunMode.Automatic => m.Confidence >= options.EffectiveAutoThreshold && m.Kind != MatchKind.Burst,
        _ => false,
    };

    private int? Apply(DuplicateGroup group, PhotoFile keeper, IReadOnlyList<GroupMember> members, bool automatic,
        string sessionDir, ActionJournal journal, ActionSummary summary, HashSet<string> removed)
    {
        if (removed.Contains(keeper.Path) || !IsUnchanged(keeper))
        {
            summary.Warnings.Add($"Gruppo {group.Id}: la copia da tenere non è più disponibile o è cambiata, gruppo saltato.");
            return null;
        }

        int count = 0;
        foreach (var m in members)
        {
            var f = m.File;
            if (ReferenceEquals(f, keeper) || removed.Contains(f.Path)) continue;
            if (!IsUnchanged(f))
            {
                summary.Warnings.Add($"Modificato dopo la scansione, saltato: {f.Path}");
                continue;
            }
            if (m.Kind == MatchKind.ExactBytes && !ExactMatcher.FilesAreIdentical(keeper.Path, f.Path))
            {
                summary.Warnings.Add($"La verifica byte per byte è fallita, saltato: {f.Path}");
                continue;
            }

            try
            {
                string? movedTo = Dispose(f.Path, sessionDir);
                journal.Write(new JournalEntry(DateTime.UtcNow, options.Disposal, f.Path, movedTo, f.Size,
                    keeper.Path, m.Kind, m.Confidence, automatic));
                removed.Add(f.Path);
                summary.Moved++;
                summary.BytesFreed += f.Size;
                count++;
                progress?.Report($"{(automatic ? "[auto]" : "[ok]  ")} {f.Path}");
            }
            catch (Exception ex)
            {
                summary.Warnings.Add($"Impossibile spostare {f.Path}: {ex.Message}");
            }
        }
        return count;
    }

    private static bool IsUnchanged(PhotoFile f)
    {
        var fi = new FileInfo(f.Path);
        return fi.Exists && fi.Length == f.Size && fi.LastWriteTimeUtc == f.LastWriteUtc;
    }

    private string? Dispose(string path, string sessionDir)
    {
        if (options.Disposal == DisposalMethod.RecycleBin)
        {
            RecycleBin.Send(path);
            return null;
        }

        // In quarantena si ricrea la struttura originale: C:\Foto\a.jpg -> <quarantena>\<data>\C\Foto\a.jpg
        string relative = Path.GetFullPath(path).Replace(":", "").TrimStart('\\', '/');
        string dest = Path.Combine(sessionDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (int i = 2; File.Exists(dest); i++)
            dest = Path.Combine(Path.GetDirectoryName(dest)!, $"{Path.GetFileNameWithoutExtension(relative)} ~{i}{Path.GetExtension(relative)}");
        File.Move(path, dest);
        return dest;
    }
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
