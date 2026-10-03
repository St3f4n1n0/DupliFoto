using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupliFoto.Core;
using DupliFoto.Core.Reporting;
using DupliFoto.Gui.Services;

namespace DupliFoto.Gui.ViewModels;

/// <summary>Una voce di un menu a tendina, come la si mostra: nella finestra la disegna un modello unico per tutte.</summary>
public interface IChoice : System.ComponentModel.INotifyPropertyChanged
{
    string Label { get; }
    string Description { get; }
    /// <summary>Dopo un cambio di lingua.</summary>
    void Refresh();
}

/// <summary>Una voce di un menu a tendina: il valore e come lo si mostra, nelle due lingue.</summary>
public sealed class Choice<T>(T value, string labelIt, string labelEn, string descriptionIt = "", string descriptionEn = "")
    : ObservableObject, IChoice
{
    public T Value { get; } = value;
    public string Label => Lang.T(labelIt, labelEn);
    public string Description => Lang.T(descriptionIt, descriptionEn);
    public void Refresh() => OnPropertyChanged(string.Empty);
    public override string ToString() => Label;
}

/// <summary>Una cartella da analizzare.</summary>
public sealed partial class FolderItem(string path, Action<FolderItem> remove) : ObservableObject
{
    public string Path { get; } = path;

    /// <summary>L'ultima cartella del percorso ("Catalogate"); per un'unità intera, il percorso.</summary>
    public string ShortName { get; } =
        System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } n ? n : path;

    /// <summary>Come si chiama nell'interfaccia: il nome breve, o il percorso intero se due cartelle si chiamano uguale.</summary>
    [ObservableProperty] private string _name = "";

    /// <summary>Le copie che stanno in questa cartella si tengono sempre. Al massimo una cartella.</summary>
    [ObservableProperty] private bool _isKept;

    public string KeepTip => Lang.T($"Tieni sempre le copie che stanno in {Path}", $"Always keep the copies in {Path}");
    public string KeepName => Lang.T($"Tieni le copie in {Name}", $"Keep the copies in {Name}");

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(KeepName));

    public void Refresh() => OnPropertyChanged(string.Empty);

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
/// Una coppia "copia da tenere / doppione": una riga dell'elenco e ciò che si vede nel confronto.
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
        PairStatus.ReportOnly => Lang.T("Solo report", "Report only"),
        PairStatus.Pending => Lang.T("Da decidere", "To decide"),
        PairStatus.Moved => Lang.T("Spostato", "Moved"),
        PairStatus.Skipped => Lang.T("Tenute entrambe", "Kept both"),
        PairStatus.Blocked => Lang.T("Non toccato", "Left alone"),
        _ => Status.ToString(),
    };

    /// <summary>Dopo un cambio di lingua.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

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
public sealed partial class PhotoPanel(bool isKeeper) : ObservableObject
{
    private PhotoFile? _file;

    public string Role => IsKeeper ? Lang.T("Da tenere", "To keep") : Lang.T("Da spostare", "To move");
    public bool IsKeeper { get; } = isKeeper;
    /// <summary>La cartella aggiunta da cui viene la foto, quando le cartelle sono più di una.</summary>
    [ObservableProperty] private string _rootName = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPhoto))] private string? _path;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowPlaceholder))] private Bitmap? _image;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowPlaceholder))] private bool _isLoading;
    [ObservableProperty] private string _placeholder = "";

    public bool HasPhoto => Path is not null;
    public bool ShowPlaceholder => Image is null && !IsLoading;

    public void Show(PhotoFile? f, string? rootName = null)
    {
        _file = f;
        Path = f?.Path;
        RootName = rootName ?? "";
        Image = null;
        Placeholder = "";
        if (f is null)
        {
            FileName = Folder = Details = "";
            return;
        }
        FileName = System.IO.Path.GetFileName(f.Path);
        Folder = f.Directory;
        Details = Describe(f);
    }

    /// <summary>Dopo un cambio di lingua: i dettagli e l'etichetta, senza ricaricare la foto.</summary>
    public void Refresh()
    {
        if (_file is not null) Details = Describe(_file);
        OnPropertyChanged(nameof(Role));
    }

    private static string Describe(PhotoFile f)
    {
        var parts = new List<string> { f.Extension.TrimStart('.').ToUpperInvariant() };
        if (f.Width > 0) parts.Add($"{f.Width} × {f.Height}");
        parts.Add(ReportWriter.FormatBytes(f.Size));
        if (f.TakenAt is { } t) parts.Add(t.ToString(Lang.T("dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss")));
        if (f.CameraModel is { } m) parts.Add(m);
        if (f.Sharpness > 0) parts.Add(Lang.T($"nitidezza {f.Sharpness:0}", $"sharpness {f.Sharpness:0}"));
        return string.Join("  ·  ", parts);
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
