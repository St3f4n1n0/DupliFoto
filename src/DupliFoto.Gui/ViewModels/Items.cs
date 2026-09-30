using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupliFoto.Core;
using DupliFoto.Core.Reporting;
using DupliFoto.Gui.Services;

namespace DupliFoto.Gui.ViewModels;

/// <summary>Una voce di un menu a tendina: il valore e come lo si mostra.</summary>
public sealed record Choice<T>(T Value, string Label, string Description = "")
{
    public override string ToString() => Label;
}

/// <summary>Una cartella da analizzare.</summary>
public sealed partial class FolderItem(string path, Action<FolderItem> remove) : ObservableObject
{
    public string Path { get; } = path;

    /// <summary>Nei doppioni vince la copia che sta in una cartella preferita.</summary>
    [ObservableProperty] private bool _isPreferred;

    [RelayCommand]
    private void Remove() => remove(this);
}

public enum PairStatus
{
    /// <summary>Modalità sola lettura: la coppia compare solo nel report.</summary>
    ReportOnly,
    /// <summary>Serve una decisione dell'utente.</summary>
    Pending,
    Moved,
    /// <summary>L'utente ha deciso di tenere entrambe le foto.</summary>
    Skipped,
    /// <summary>Una regola di sicurezza ha impedito lo spostamento (file cambiato, copia da tenere sparita...).</summary>
    Blocked,
}

/// <summary>
/// Una coppia "copia da tenere / doppione", come le righe di Awesome Duplicate Photo Finder.
/// L'affidabilità è sempre quella del doppione rispetto alla copia da tenere del suo gruppo.
/// </summary>
public sealed partial class PairItem(DuplicateGroup group, GroupMember member) : ObservableObject
{
    public DuplicateGroup Group { get; } = group;
    public PhotoFile Keeper { get; } = group.Keeper;
    public GroupMember Member { get; } = member;
    public PhotoFile Duplicate => Member.File;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsPending), nameof(IsMoved), nameof(IsBlocked))]
    private PairStatus _status;

    [ObservableProperty] private string _note = "";

    public bool IsPending => Status == PairStatus.Pending;
    public bool IsMoved => Status == PairStatus.Moved;
    public bool IsBlocked => Status == PairStatus.Blocked;

    public string StatusText => Status switch
    {
        PairStatus.ReportOnly => "Solo report",
        PairStatus.Pending => "Da decidere",
        PairStatus.Moved => "Spostato",
        PairStatus.Skipped => "Tenute entrambe",
        PairStatus.Blocked => "Non toccato",
        _ => Status.ToString(),
    };

    public string KeeperName => System.IO.Path.GetFileName(Keeper.Path);
    public string KeeperFolder => Keeper.Directory;
    public string DuplicateName => System.IO.Path.GetFileName(Duplicate.Path);
    public string DuplicateFolder => Duplicate.Directory;
    public string KindLabel => ReportWriter.KindLabel(Member.Kind);
    public string ConfidenceText => $"{Member.Confidence:0}%";
    public bool IsHigh => Member.Confidence >= 99;
    public bool IsMid => Member.Confidence is >= 90 and < 99;
    public bool IsLow => Member.Confidence < 90;
}

/// <summary>Metà dello schermo di confronto: una foto con le sue informazioni.</summary>
public sealed partial class PhotoPanel(string role, bool isKeeper) : ObservableObject
{
    public string Role { get; } = role;
    public bool IsKeeper { get; } = isKeeper;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPhoto))] private string? _path;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowPlaceholder))] private Bitmap? _image;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowPlaceholder))] private bool _isLoading;
    [ObservableProperty] private string _placeholder = "";

    public bool HasPhoto => Path is not null;
    public bool ShowPlaceholder => Image is null && !IsLoading;

    public void Show(PhotoFile? f)
    {
        Path = f?.Path;
        Image = null;
        Placeholder = "";
        if (f is null)
        {
            FileName = Folder = Details = "";
            return;
        }
        FileName = System.IO.Path.GetFileName(f.Path);
        Folder = f.Directory;

        var parts = new List<string> { f.Extension.TrimStart('.').ToUpperInvariant() };
        if (f.Width > 0) parts.Add($"{f.Width} × {f.Height}");
        parts.Add(ReportWriter.FormatBytes(f.Size));
        if (f.TakenAt is { } t) parts.Add(t.ToString("dd/MM/yyyy HH:mm:ss"));
        if (f.CameraModel is { } m) parts.Add(m);
        if (f.Sharpness > 0) parts.Add($"nitidezza {f.Sharpness:0}");
        Details = string.Join("  ·  ", parts);
    }

    [RelayCommand]
    private void Open()
    {
        if (Path is not null) Shell.Open(Path);
    }

    [RelayCommand]
    private void Reveal()
    {
        if (Path is not null) Shell.Reveal(Path);
    }
}
