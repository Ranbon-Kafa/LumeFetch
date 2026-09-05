using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LumeFetch.Core.Collections;
using LumeFetch.Core.Resolvers;
using LumeFetch.Desktop.Localization;
using LumeFetch.Desktop.ViewModels;
using LumeFetch.Desktop.Views;
using LumeFetch.Infrastructure.Settings;

namespace LumeFetch.Screenshot;

internal static partial class Program
{
    private static async Task VerifyCollectionsAsync(MainWindow window, MainWindowViewModel vm, JsonSettingsStore store, string output)
    {
        await vm.AnalyzeFromPasteAsync("https://www.youtube.com/playlist?list=fixture");
        Require(vm.HasCollection && !vm.HasResult && vm.CollectionRows.Count == 4, "Playlist preview");
        Require(!vm.CollectionRows[3].CanSelect && !vm.BatchEnqueueCommand.CanExecute(null), "Unavailable rows and explicit download permission");
        Capture(window, Path.Combine(output, "lumefetch-playlist.png"));
        var before = vm.Downloads.Count;
        vm.RightsConfirmed = true;
        vm.BatchEnqueueCommand.Execute(null);
        await UntilAsync(() => !vm.IsBatchBusy);
        Dispatcher.UIThread.RunJobs();
        Require(vm.Downloads.Count == before + 2, "Per-item failure does not prevent later playlist items");
        Require(vm.CollectionRows[0].IsEnqueued && vm.CollectionRows[2].IsEnqueued && !vm.CollectionRows[1].IsEnqueued, "Per-item batch results");
        vm.BatchEnqueueCommand.Execute(null);
        await UntilAsync(() => !vm.IsBatchBusy);
        Dispatcher.UIThread.RunJobs();
        Require(vm.Downloads.Count == before + 2, "Queued rows are not duplicated by retry");

        await vm.AnalyzeFromPasteAsync("https://open.spotify.com/playlist/0123456789abcdefghijkl");
        Require(vm.IsSpotifyCollection && vm.CollectionRows.Count == 3 && vm.CollectionRows.All(row => row.DownloadUri is null), "Spotify is metadata, never a downloader");
        vm.MatchTracksCommand.Execute(null);
        await UntilAsync(() => !vm.IsBatchBusy);
        Dispatcher.UIThread.RunJobs();
        Require(vm.CollectionRows[0].SelectedCandidate?.Model.Confidence == 100 && vm.CollectionRows[0].IsSelected, "High confidence match");
        Require(vm.CollectionRows[1].SelectedCandidate?.Model.Confidence == 80 && !vm.CollectionRows[1].IsSelected, "Medium match needs manual selection");
        Require(!vm.CollectionRows[2].IsSelected && vm.CollectionRows[2].DownloadUri is null, "No match cannot download");
        Capture(window, Path.Combine(output, "lumefetch-spotify-en.png"));
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "tr");
        Capture(window, Path.Combine(output, "lumefetch-spotify-tr.png"));
        Require(window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == Localizer.Current["MainTitle"]), "Static XAML labels change language");
        Require(vm.CollectionRows[0].Status == Localizer.Current["HighConfidence"], "Dynamic status changes language");
        Require(vm.CollectionRows[0].IsSelected && !vm.CollectionRows[1].IsSelected, "Language change preserves selections");
        vm.BatchQuality = vm.BatchQualities.Single(quality => quality.Value == CollectionQuality.Mp3);
        Capture(window, Path.Combine(output, "lumefetch-mp3-batch-tr.png"));
        window.Width = 980; window.Height = 680;
        Capture(window, Path.Combine(output, "lumefetch-compact-tr.png"));
        window.Width = 1220; window.Height = 960;
        vm.RightsConfirmed = true;
        vm.CollectionRows[1].IsSelected = true;
        before = vm.Downloads.Count;
        vm.BatchEnqueueCommand.Execute(null);
        await UntilAsync(() => !vm.IsBatchBusy);
        Dispatcher.UIThread.RunJobs();
        Require(vm.Downloads.Count == before + 2, "Confirmed Spotify rows enqueue their YouTube counterparts");
        Require(vm.Downloads.Count(download => download.Meta.Contains("MP3", StringComparison.OrdinalIgnoreCase)) == 2, "Spotify batch MP3 preference reaches the queue");

        await vm.AnalyzeFromPasteAsync("https://www.youtube.com/playlist?list=slow");
        vm.RightsConfirmed = true;
        before = vm.Downloads.Count;
        vm.BatchEnqueueCommand.Execute(null);
        vm.Url = "invalid new input";
        await UntilAsync(() => !vm.BatchEnqueueCommand.IsRunning);
        Require(vm.Downloads.Count == before && !vm.HasCollection && vm.BatchMessage is null, "Superseded preparation cannot enqueue or replace new UI state");
        var pending = vm.AnalyzeFromPasteAsync("https://example.org/slow.mp4");
        vm.DisconnectSpotifyCommand.Execute(null);
        await pending;
        Require(!vm.IsAnalyzing && !vm.HasResult, "Disconnect cannot leave analysis stuck");

        vm.ShowSettingsCommand.Execute(null);
        vm.SaveSettingsCommand.Execute(null);
        await UntilAsync(() => vm.SettingsMessage == Localizer.Current["SettingsSaved"]);
        Require(store.Load().Language == "tr", "Language persists");
        Capture(window, Path.Combine(output, "lumefetch-settings-tr.png"));
        var settingsScroll = window.FindControl<ScrollViewer>("SettingsScrollView")!;
        settingsScroll.Offset = new Avalonia.Vector(0, settingsScroll.Extent.Height);
        Capture(window, Path.Combine(output, "lumefetch-spotify-setup-tr.png"));
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "en");
        Console.WriteLine("PASS: playlists, partial failure/retry, Spotify matching/confirmation, TR/EN live bindings, persisted language, canceled batch isolation.");
    }

    private sealed class PreviewCatalog : IMediaCollectionProvider, IMediaResolver, ITrackSearch
    {
        public string Id => "spotify-fixture";
        public string DisplayName => "Spotify fixture";
        public bool CanExpand(Uri uri) => uri.Host == "www.youtube.com" && uri.AbsolutePath == "/playlist";
        public bool CanResolve(Uri uri) => uri.Host == "open.spotify.com";
        public Task<MediaPlaylist> ExpandAsync(Uri uri, int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CollectionEntry> entries = uri.Query.Contains("slow", StringComparison.Ordinal)
                ? [new(1, "Slow fixture", new Uri("https://example.org/slow.mp4"), null)]
                : [
                    new(1, "Coastal light — sample footage", new Uri("https://example.org/one.mp4"), TimeSpan.FromSeconds(214)),
                    new(2, "Unavailable at download time — test fixture", new Uri("https://example.org/missing.mp4"), null),
                    new(3, "A quiet morning — sample footage", new Uri("https://example.org/two.mp4"), TimeSpan.FromSeconds(120)),
                    new(4, "Private item — skipped", null, null, "Unavailable")];
            return Task.FromResult(new MediaPlaylist("Creative moments · UI SAMPLE DATA", uri, entries));
        }
        public Task<ResolvedCatalog> ResolveAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResolvedCatalog("Evening lights · UI SAMPLE DATA", [
                new(new CatalogTrack("Northern Light", ["Aurora"], "Sample album", TimeSpan.FromSeconds(180), null, uri), null, []),
                new(new CatalogTrack("Quiet Harbor", ["Aurora"], "Sample album", TimeSpan.FromSeconds(180), null, uri), null, []),
                new(new CatalogTrack("Unreleased Demo", ["Aurora"], "Sample album", null, null, uri), null, [])], uri));
        public async Task<IReadOnlyList<TrackSearchResult>> SearchAsync(CatalogTrack track, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken);
            if (track.Title == "Unreleased Demo") return [];
            return [
                new(new Uri("https://www.youtube.com/watch?v=abcdefghijk"), track.Title + " (Official Audio)",
                    "Aurora - Topic", TimeSpan.FromSeconds(track.Title == "Northern Light" ? 180 : 190), true),
                new(new Uri("https://www.youtube.com/watch?v=ABCDEFGHIJK"), track.Title + " live", "Aurora",
                    TimeSpan.FromSeconds(230), false)];
        }
    }
}
