using DupliFoto.Core;
using DupliFoto.Gui.Services;
using DupliFoto.Gui.ViewModels;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>I video nella finestra: spenti di norma, poi coppie con il loro tipo, il loro filtro e niente anteprima.</summary>
public sealed class VideoGuiTests : IDisposable
{
    private readonly SamplePhotos _photos = new();

    public VideoGuiTests()
    {
        var bytes = new byte[300_000];
        new Random(3).NextBytes(bytes);
        File.WriteAllBytes(_photos.P("vacanze.mp4"), bytes);
        File.WriteAllBytes(_photos.P("vacanze (1).mp4"), bytes);
    }

    public void Dispose() => _photos.Dispose();

    [Fact]
    public Task Videos_are_off_by_default_and_the_choice_is_remembered() => Ui.Run(() =>
    {
        var store = new SettingsStore(Path.Combine(_photos.Root, "gui.json"));
        var vm = new MainViewModel(store) { CachePath = null };
        Assert.False(vm.IncludeVideos);
        Assert.True(vm.FullVideoCheck);
        Assert.False(vm.BuildOptions().IncludeVideos);

        vm.IncludeVideos = true;
        vm.QuickVideoCheck = true;
        Assert.Equal(VideoCheck.Quick, vm.BuildOptions().VideoCheck);
        vm.SaveSettings();

        var again = new MainViewModel(store) { CachePath = null };
        Assert.True(again.IncludeVideos);
        Assert.True(again.QuickVideoCheck);
        Assert.False(again.FullVideoCheck);
    });

    [Theory]
    [InlineData(false, "Identici al byte", "100%")]
    [InlineData(true, "Video, verifica rapida", "99%")]
    public Task Identical_videos_show_up_as_a_pair_without_a_preview(bool quick, string kind, string confidence) => Ui.Run(async () =>
    {
        var vm = MainViewModelTests.NewViewModel(_photos, RunMode.Assisted);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Pairs, p => p.Duplicate.IsVideo);    // spenti: i video non si guardano nemmeno

        vm.IncludeVideos = true;
        vm.QuickVideoCheck = quick;
        await vm.StartCommand.ExecuteAsync(null);

        var pair = Assert.Single(vm.Pairs, p => p.Duplicate.IsVideo);
        Assert.Equal(kind, pair.KindLabel);
        Assert.Equal(5, vm.Pairs.Count);                               // le coppie di foto restano le stesse
        vm.SelectedFilter = vm.Filters.Single(f => f.Value == PairFilter.Videos);
        Assert.Same(pair, Assert.Single(vm.VisiblePairs));

        vm.SelectedPair = pair;
        await MainViewModelTests.WaitFor(() => vm.Left.Placeholder.Length > 0 && vm.Right.Placeholder.Length > 0);
        Assert.Null(vm.Left.Image);
        Assert.StartsWith("▶  Video", vm.Right.Placeholder);
        Assert.Contains("MP4", vm.Right.Details);
        Assert.EndsWith(confidence, vm.ConfidenceText);
    });
}
