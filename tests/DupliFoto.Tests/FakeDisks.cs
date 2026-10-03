using DupliFoto.Core.Actions;

namespace DupliFoto.Tests;

/// <summary>
/// Dischi finti: le cartelle sotto <see cref="Stick"/> stanno su una chiavetta (E:), tutto il resto sul disco C:, dove
/// c'è anche la quarantena. Lo spazio libero del disco della quarantena si sceglie a piacere.
/// </summary>
public sealed class FakeDisks : Disks
{
    public string? Stick { get; set; }

    /// <summary>La chiavetta ha un Cestino? Le vere no; qui di norma sì, per provare una cosa alla volta.</summary>
    public bool StickHasRecycleBin { get; set; } = true;

    /// <summary>Spazio libero e capacità del disco della quarantena; null = non si sa.</summary>
    public DiskSpace? Space { get; set; }

    private bool OnStick(string path) => Stick is not null && path.StartsWith(Stick, StringComparison.OrdinalIgnoreCase);

    public override string? DriveOf(string path) => OnStick(path) ? @"E:\" : @"C:\";

    public override DiskSpace? SpaceOf(string path) => Space;

    public override bool HasRecycleBin(string path) => StickHasRecycleBin || !OnStick(path);

    /// <summary>Un disco della quarantena quasi pieno: 5 KB liberi su 1 MB.</summary>
    public static DiskSpace AlmostFull => new(5_000, 1_000_000);
}
