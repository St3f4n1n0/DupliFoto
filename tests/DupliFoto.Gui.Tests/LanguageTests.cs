using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DupliFoto.Core;
using DupliFoto.Gui.ViewModels;
using DupliFoto.Gui.Views;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>Italiano e inglese: la finestra cambia lingua senza riaprirsi, e nessun testo resta fuori dalla traduzione.</summary>
[Collection(typeof(InEnglish))]
public sealed class LanguageTests : IDisposable
{
    private readonly SamplePhotos _photos = new();

    public void Dispose()
    {
        _photos.Dispose();
        Lang.Set(Lang.Italian);
    }

    private static string Texts(Window w) =>
        string.Join(" | ", w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)));

    [Fact]
    public Task Choosing_English_translates_the_open_window() => Ui.Run(async () =>
    {
        var vm = MainViewModelTests.NewViewModel(_photos, RunMode.Assisted);
        vm.AddFolders([Path.Combine(_photos.Photos, "WhatsApp")]);
        vm.Folders[0].IsKept = true;
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 900 };
        window.Show();
        await vm.StartCommand.ExecuteAsync(null);
        vm.SelectedPair = vm.Pairs.Single(p => p.DuplicateName == "IMG-20260810-WA0001.jpg");
        await MainViewModelTests.WaitFor(() => vm.Left.Image is not null && vm.Right.Image is not null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Avvia ricerca", Texts(window));
        Assert.Contains("hash percettivo", vm.ReasonText);

        vm.SelectedLanguage = vm.Languages.Single(l => l.Value == Lang.English);
        Dispatcher.UIThread.RunJobs();

        string english = Texts(window);
        foreach (var expected in new[] { "Start search", "Move the right-hand one", "Keep both", "To keep", "To move", "Same picture",
                                         "Pairs found", "photos analysed", "Confidence", "Assisted" })
            Assert.Contains(expected, english);
        foreach (var italian in new[] { "Avvia ricerca", "Tieni entrambe", "Da tenere", "Coppie trovate", "Affidabilità" })
            Assert.DoesNotContain(italian, english);
        // I motivi trovati durante la ricerca sono nelle due lingue: cambiano anche loro, senza rifare la ricerca.
        Assert.Contains("perceptual hash", vm.ReasonText);
        Assert.Equal("The left-hand one is kept: it is in the folder to keep", vm.KeeperReasonText);
        Assert.StartsWith("Search completed in", vm.StatusText);
        ScreenshotTests.SaveFrame(window, "6-inglese");

        vm.SelectedLanguage = vm.Languages.Single(l => l.Value == Lang.Italian);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Avvia ricerca", Texts(window));
        Assert.DoesNotContain("Start search", Texts(window));
        window.Close();
    });

    [Fact]
    public Task The_language_is_remembered() => Ui.Run(() =>
    {
        var store = new Services.SettingsStore(Path.Combine(_photos.Root, "gui.json"));
        var vm = new MainViewModel(store) { CachePath = null };
        Assert.Equal(Lang.Auto, vm.SelectedLanguage.Value); // predefinito: come Windows (qui, nei test, l'italiano)
        Assert.False(Lang.IsEnglish);
        vm.SelectedLanguage = vm.Languages.Single(l => l.Value == Lang.English);
        vm.SaveSettings();
        Lang.Set(Lang.Italian);

        var again = new MainViewModel(store) { CachePath = null };
        Assert.Equal(Lang.English, again.SelectedLanguage.Value);
        Assert.True(Lang.IsEnglish);
        Assert.Equal("Start search", Strings.StartSearch);
    });

    /// <summary>Ogni testo della finestra passa da {l:T ...} o da un binding: un testo scritto direttamente resterebbe in italiano.</summary>
    [Fact]
    public void The_window_has_no_fixed_texts()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "DupliFoto.slnx"))) root = Path.GetDirectoryName(root)!;
        string xaml = File.ReadAllText(Path.Combine(root, "src", "DupliFoto.Gui", "Views", "MainWindow.axaml"));
        var fixedTexts = Regex.Matches(xaml, """\b(?:Text|Content|ToolTip\.Tip|PlaceholderText|AutomationProperties\.Name|Title|Header)="([^"{][^"]*)" """.Trim())
            .Select(m => m.Groups[1].Value)
            .Where(t => t is not "DupliFoto")
            .ToList();
        Assert.Empty(fixedTexts);
        Assert.DoesNotContain("StringFormat='", xaml.Replace("StringFormat='{}{0:N0}'", "").Replace("StringFormat='({0})'", "").Replace("StringFormat='· {0}'", ""));
    }
}
