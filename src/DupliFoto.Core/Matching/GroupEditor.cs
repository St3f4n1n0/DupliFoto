namespace DupliFoto.Core.Matching;

/// <summary>Modifiche ai gruppi decise dall'utente.</summary>
public static class GroupEditor
{
    /// <summary>
    /// Fa di <paramref name="newKeeper"/> la copia da tenere. Le affidabilità degli altri membri vengono
    /// ricalcolate rispetto alla nuova copia (mai lungo una catena A~B~C), come quando il gruppo è nato.
    /// Solo tra cartelle diverse: le copie nella cartella della nuova copia restano dove sono, e quelle
    /// messe da parte nell'altra cartella tornano a essere doppioni.
    /// </summary>
    public static void ChangeKeeper(DuplicateGroup group, PhotoFile newKeeper, ScanOptions o)
    {
        if (ReferenceEquals(newKeeper, group.Keeper)) return;
        if (!group.Duplicates.Any(d => ReferenceEquals(d.File, newKeeper)))
            throw new ArgumentException("Il file non fa parte del gruppo", nameof(newKeeper));

        var others = group.AllFiles.Concat(group.SameFolderCopies).Where(f => !ReferenceEquals(f, newKeeper)).ToList();

        group.Duplicates.Clear();
        group.SameFolderCopies.Clear();
        foreach (var f in others)
        {
            if (SimilarityScorer.Compare(newKeeper, f, o) is { } m)
            {
                group.Duplicates.Add(new GroupMember { File = f, Kind = m.Kind, Confidence = m.Confidence, Reason = m.Reason });
            }
            else
            {
                // Legame solo indiretto con la nuova copia: stessa regola del motore (50%, mai automatico).
                group.Duplicates.Add(new GroupMember
                {
                    File = f,
                    Kind = MatchKind.Burst,
                    Confidence = 50,
                    Reason = "simile ad altre foto del gruppo, ma non direttamente a quella da tenere",
                });
            }
        }
        group.Keeper = newKeeper;
        group.KeeperReason = "scelta da te";
        if (o.CrossFolderOnly) DedupEngine.SetAsideSameFolderCopies(group);
    }
}
