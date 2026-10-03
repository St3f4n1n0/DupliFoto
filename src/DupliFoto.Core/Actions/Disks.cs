namespace DupliFoto.Core.Actions;

/// <summary>
/// Le domande che gli spostamenti fanno ai dischi: su quale disco sta un percorso, quanto spazio resta, se c'è un
/// Cestino. Nei test si finge un PC con una chiavetta o con il disco della quarantena quasi pieno.
/// </summary>
public class Disks
{
    public static readonly Disks System = new();

    /// <summary>
    /// Sotto questa quota di spazio libero il disco della quarantena è "quasi pieno": gli spostamenti verso di lui
    /// si sospendono, prima di riempirlo.
    /// </summary>
    public const double MinFreeFraction = 0.10;

    /// <summary>La radice del disco ("C:\", "\\server\condivisa"), o null se non si sa.</summary>
    public virtual string? DriveOf(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)); }
        catch (Exception) { return null; }
    }

    /// <summary>Spazio libero e capacità del disco, o null se non si possono sapere (per esempio in rete).</summary>
    public virtual DiskSpace? SpaceOf(string path)
    {
        try
        {
            var drive = new DriveInfo(DriveOf(path)!);
            return drive.IsReady && drive.TotalSize > 0 ? new DiskSpace(drive.AvailableFreeSpace, drive.TotalSize) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Vedi <see cref="RecycleBin.IsAvailableFor"/>.</summary>
    public virtual bool HasRecycleBin(string path) => RecycleBin.IsAvailableFor(path);

    /// <summary>
    /// Due percorsi sullo stesso disco? Allora spostare un file vuol dire solo cambiargli nome: è immediato e non
    /// occupa spazio. Tra due dischi diversi invece il file viene copiato per intero e poi tolto.
    /// </summary>
    public bool SameDrive(string a, string b) =>
        DriveOf(a) is { } da && DriveOf(b) is { } db && string.Equals(da, db, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Spazio libero e capacità di un disco, in byte.</summary>
public readonly record struct DiskSpace(long Free, long Total)
{
    /// <summary>Dopo aver aggiunto <paramref name="adding"/> byte, resterebbe meno del 10% della capacità?</summary>
    public bool IsLow(long adding = 0) => Free - adding < Total * Disks.MinFreeFraction;
}
