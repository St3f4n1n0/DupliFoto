using DupliFoto.Core.Matching;

namespace DupliFoto.Core.Actions;

/// <summary>Risposta dell'utente per un gruppo.</summary>
public enum UserChoice { Apply, Skip, ApplyToAllOfThisKind, Quit }

/// <summary>Una domanda da porre all'utente: il gruppo e i soli membri su cui serve una decisione.</summary>
public sealed record PromptRequest(DuplicateGroup Group, IReadOnlyList<GroupMember> Members);

public sealed record PromptAnswer(UserChoice Choice, PhotoFile? NewKeeper = null);

/// <summary>Conferma gruppo per gruppo (riga di comando). La GUI decide coppia per coppia con <see cref="ActionSession"/>.</summary>
public interface IDecisionPrompt
{
    PromptAnswer Ask(PromptRequest request);

    /// <summary>
    /// Il disco della quarantena è quasi pieno (<paramref name="reason"/>): cancellare per sempre i doppioni che non
    /// entrano più? Va chiesto con due conferme. Chi non lo implementa risponde no, e gli spostamenti si fermano.
    /// </summary>
    bool AllowPermanentDeletion(string reason) => false;
}

public sealed class ActionSummary
{
    public int Moved { get; set; }
    /// <summary>Quanti dei <see cref="Moved"/> sono stati cancellati per sempre, perché la quarantena era piena.</summary>
    public int DeletedForGood { get; set; }
    public long BytesFreed { get; set; }
    public int AutomaticActions { get; set; }
    public int ConfirmedActions { get; set; }
    public int SkippedByUser { get; set; }
    public int AwaitingReview { get; set; }
    public List<string> Warnings { get; } = new();
    public string? JournalPath { get; set; }
}

/// <summary>
/// Applica le decisioni gruppo per gruppo secondo la modalità scelta (usato dalla riga di comando).
/// Le regole di sicurezza sono in <see cref="ActionSession"/> e valgono in TUTTE le modalità.
/// </summary>
/// <param name="disks">Dischi, spazio e Cestino: di norma quelli veri; nei test si fingono.</param>
public sealed class ActionExecutor(ScanOptions options, IDecisionPrompt? prompt, IProgress<string>? progress = null, Disks? disks = null)
{
    /// <summary>Il disco della quarantena si è quasi riempito e nessuno ha scelto di cancellare: ci si ferma.</summary>
    private bool _suspended;

    public ActionSummary Execute(ScanResult result)
    {
        if (options.Mode == RunMode.ReadOnly) return new ActionSummary();

        using var session = new ActionSession(options, progress, disks);
        var summary = session.Summary;
        var approvedKinds = new HashSet<MatchKind>();
        bool quit = false;

        foreach (var group in result.Groups)
        {
            if (quit || _suspended) { summary.AwaitingReview += group.Duplicates.Count; continue; }

            var auto = group.Duplicates.Where(m => ActionPolicy.IsAutomatic(options, m)).ToList();
            var ask = group.Duplicates.Except(auto).ToList();

            if (auto.Count > 0 && Apply(session, group, group.Keeper, auto, automatic: true) is int n)
                summary.AutomaticActions += n;

            if (ask.Count == 0) continue;
            if (_suspended) { summary.AwaitingReview += ask.Count; continue; }

            // Anche con "sì a tutti", sotto il 60% si chiede sempre.
            if (approvedKinds.Contains(group.Kind) && ask.All(m => m.Confidence >= 60))
            {
                summary.ConfirmedActions += Apply(session, group, group.Keeper, ask, automatic: false) ?? 0;
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
                    var targets = ask;
                    if (answer.NewKeeper is { } keeper && !ReferenceEquals(keeper, group.Keeper))
                    {
                        // Gli altri membri si rivalutano rispetto alla nuova copia (e, solo tra cartelle diverse,
                        // quelli nella sua stessa cartella restano dove sono).
                        GroupEditor.ChangeKeeper(group, keeper, options);
                        targets = group.Duplicates;
                    }
                    summary.ConfirmedActions += Apply(session, group, group.Keeper, targets, automatic: false) ?? 0;
                    break;
            }
        }
        return summary;
    }

    /// <summary>Sposta i membri indicati; <c>null</c> se la copia da tenere non è più disponibile.</summary>
    private int? Apply(ActionSession session, DuplicateGroup group, PhotoFile keeper, IReadOnlyList<GroupMember> members, bool automatic)
    {
        if (!session.IsKeeperAvailable(keeper))
        {
            session.Summary.Warnings.Add(Lang.T($"Gruppo {group.Id}: la copia da tenere non è più disponibile o è cambiata, gruppo saltato.",
                $"Group {group.Id}: the copy to keep is no longer available or has changed, group skipped."));
            return null;
        }
        int moved = 0;
        for (int i = 0; i < members.Count; i++)
        {
            var outcome = session.Move(keeper, members[i], automatic);
            if (outcome.Result == MoveResult.QuarantineFull)
            {
                // Disco della quarantena quasi pieno: si cancella per sempre solo se qualcuno lo conferma due volte.
                if (prompt?.AllowPermanentDeletion(outcome.Message) != true)
                {
                    _suspended = true;
                    session.Summary.Warnings.Add(outcome.Message + Lang.T(
                        " Libera spazio e rilancia: i doppioni rimasti sono nel report.",
                        " Free up some space and run again: the remaining duplicates are in the report."));
                    session.Summary.AwaitingReview += members.Count - i;
                    break;
                }
                session.DeleteWhenQuarantineFull = true;
                outcome = session.Move(keeper, members[i], automatic);
            }
            if (outcome.Moved) moved++;
        }
        return moved;
    }
}
