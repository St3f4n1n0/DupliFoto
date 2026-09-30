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
/// Applica le decisioni gruppo per gruppo secondo la modalità scelta (usato dalla riga di comando).
/// Le regole di sicurezza sono in <see cref="ActionSession"/> e valgono in TUTTE le modalità.
/// </summary>
public sealed class ActionExecutor(ScanOptions options, IDecisionPrompt? prompt, IProgress<string>? progress = null)
{
    public ActionSummary Execute(ScanResult result)
    {
        if (options.Mode == RunMode.ReadOnly) return new ActionSummary();

        using var session = new ActionSession(options, progress);
        var summary = session.Summary;
        var approvedKinds = new HashSet<MatchKind>();
        bool quit = false;

        foreach (var group in result.Groups)
        {
            if (quit) { summary.AwaitingReview += group.Duplicates.Count; continue; }

            var auto = group.Duplicates.Where(m => ActionPolicy.IsAutomatic(options, m)).ToList();
            var ask = group.Duplicates.Except(auto).ToList();

            if (auto.Count > 0 && Apply(session, group, group.Keeper, auto, automatic: true) is int n)
                summary.AutomaticActions += n;

            if (ask.Count == 0) continue;

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
                    var keeper = answer.NewKeeper ?? group.Keeper;
                    var targets = ReferenceEquals(keeper, group.Keeper)
                        ? ask
                        : group.Duplicates.Where(m => !ReferenceEquals(m.File, keeper))
                            .Append(new GroupMember { File = group.Keeper, Kind = group.Kind, Confidence = group.Confidence, Reason = "scelta dell'utente" })
                            .ToList();
                    summary.ConfirmedActions += Apply(session, group, keeper, targets, automatic: false) ?? 0;
                    break;
            }
        }
        return summary;
    }

    /// <summary>Sposta i membri indicati; <c>null</c> se la copia da tenere non è più disponibile.</summary>
    private static int? Apply(ActionSession session, DuplicateGroup group, PhotoFile keeper, IReadOnlyList<GroupMember> members, bool automatic)
    {
        if (!session.IsKeeperAvailable(keeper))
        {
            session.Summary.Warnings.Add($"Gruppo {group.Id}: la copia da tenere non è più disponibile o è cambiata, gruppo saltato.");
            return null;
        }
        return members.Count(m => session.Move(keeper, m, automatic).Moved);
    }
}
