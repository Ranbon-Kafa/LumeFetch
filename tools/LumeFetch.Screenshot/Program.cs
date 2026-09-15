using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Services;
using LumeFetch.Core.Settings;
using LumeFetch.Desktop;
using LumeFetch.Desktop.Views;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Presentation.ViewModels;

namespace LumeFetch.Screenshot;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var finished = new CancellationTokenSource();
        var exitCode = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (args.Contains("--smoke", StringComparer.Ordinal))
                {
                    await NativePipelineSmoke.RunAsync();
                    await SpotifyOAuthSmoke.RunAsync();
                }
                var output = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                    ?? Path.Combine("docs", "screenshots", "lumefetch-main.png");
                await RenderAndVerifyAsync(Path.GetFullPath(output));
                await VerifyResponsiveUiAsync(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await VerifyProcessingAvailabilityAsync(Path.GetDirectoryName(Path.GetFullPath(output))!);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); exitCode = 1; }
            finally { finished.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(finished.Token);
        return exitCode;
    }

    private static async Task RenderAndVerifyAsync(string output)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        var settings = new AppSettings { DownloadDirectory = directory, SmartPaste = false, Language = "en" };
        var registry = new ProviderRegistry([new PreviewProvider()]);
        await using var manager = new DownloadManager(registry);
        var catalog = new PreviewCatalog();
        await using (var releaseManager = new DownloadManager(registry))
        using (var releaseVm = new MainWindowViewModel(new MediaAnalysisService(registry), releaseManager,
            new FFmpegService(), store, settings, collections: catalog))
        {
            Require(!releaseVm.IsSpotifyAvailable, "v1 does not register Spotify");
            await releaseVm.AnalyzeFromPasteAsync("https://open.spotify.com/track/fixture");
            Require(!releaseVm.HasResult && !releaseVm.HasCollection && releaseVm.ErrorMessage?.Contains("disabled", StringComparison.Ordinal) == true,
                "Disabled Spotify URL never falls through to a downloader");
            var browserOpened = false;
            await releaseVm.ConnectSpotifyAsync(_ => { browserOpened = true; return Task.CompletedTask; });
            Require(!browserOpened, "Disabled Spotify never starts authentication");
            var releaseWindow = new MainWindow { Width = 1220, Height = 960, DataContext = releaseVm };
            releaseWindow.Show();
            releaseVm.ShowSettingsCommand.Execute(null);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Capture(releaseWindow, Path.Combine(Path.GetDirectoryName(output)!, "lumefetch-settings.png"));
            releaseWindow.Close();
        }
        using var viewModel = new MainWindowViewModel(new MediaAnalysisService(registry), manager,
            new FFmpegService(), store, settings, collections: catalog, spotifyResolver: catalog, trackSearch: catalog);
        var window = new MainWindow { Width = 1220, Height = 960, DataContext = viewModel };
        window.Show();
        await viewModel.AnalyzeFromPasteAsync("https://example.org/preview.mp4");
        Require(viewModel.HasResult && viewModel.Options.Count == 4 && viewModel.EnqueueCommand.CanExecute(null), "Analyze and quality selection bindings");
        Require(viewModel.Options.Any(option => option.Container == "MP3"), "MP3 choice appears in the main screen");
        viewModel.EnqueueCommand.Execute(null);
        await UntilAsync(() => viewModel.Downloads.Count == 1 && viewModel.Downloads[0].ProgressValue > 0);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Capture(window, output);
        viewModel.ShowSettingsCommand.Execute(null);
        Require(viewModel.IsSettingsOpen, "Settings navigation");
        viewModel.ParallelDownloads = 3;
        viewModel.SaveSettingsCommand.Execute(null);
        await UntilAsync(() => viewModel.SettingsMessage?.StartsWith("Saved.", StringComparison.Ordinal) == true);
        Require(store.Load().MaxParallelDownloads == 3 && manager.MaxParallelDownloads == 3, "Settings persistence and live parallel limit");
        // The public settings screenshot above uses v1 wiring, not the experimental resolver fixture.
        viewModel.ShowHomeCommand.Execute(null);
        var stale = viewModel.AnalyzeFromPasteAsync("https://example.org/slow.mp4");
        viewModel.Url = "not a valid URL";
        await stale;
        Require(!viewModel.HasResult && !viewModel.EnqueueCommand.CanExecute(null), "Stale analysis cannot enqueue previous media");
        await VerifyCollectionsAsync(window, viewModel, store, Path.GetDirectoryName(output)!);
        await viewModel.ShutdownAsync();
        window.Close();
        Console.WriteLine("PASS: headless Avalonia analysis, queue bindings, settings save, navigation, stale-result guard and shutdown.");
        Console.WriteLine(output);
    }

    private static void Capture(MainWindow window, string path)
    {
        // Public screenshots use fixture paths, never the developer's home/workspace name.
        var vm = (MainWindowViewModel)window.DataContext!;
        var actualDirectory = vm.DestinationDirectory;
        var locationLabel = ((LumeFetch.Presentation.Views.MainView)window.Content!).FindControl<TextBlock>("SettingsLocationLabel")!;
        var actualLocation = locationLabel.Text;
        try
        {
            vm.DestinationDirectory = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Downloads", "LumeFetch");
            locationLabel.SetCurrentValue(TextBlock.TextProperty, "LumeFetch · settings.json");
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            vm.DestinationDirectory = actualDirectory;
            locationLabel.SetCurrentValue(TextBlock.TextProperty, actualLocation);
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + message);
    }

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    // Explicit sample data for visual regression. Network/FFmpeg tests live in NativePipelineSmoke.
    private sealed class PreviewProvider : IMediaProvider
    {
        public string Id => "preview";
        public string DisplayName => "UI PREVIEW · SAMPLE DATA";
        public int Priority => 1;
        public bool CanHandle(Uri uri) => true;
        public async Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath.Contains("missing", StringComparison.Ordinal)) throw new InvalidOperationException("Fixture: unavailable media.");
            if (uri.AbsolutePath.Contains("slow", StringComparison.Ordinal)) await Task.Delay(150, CancellationToken.None);
            return new MediaInfo(Id, DisplayName, uri, "A little light, wherever you go", TimeSpan.FromSeconds(214), null, [
                new DownloadOption("1080", "1080p", "mp4", MediaKind.Video, 1920, 1080, "H.264", "AAC", 77400000),
                new DownloadOption("720", "720p", "webm", MediaKind.Video, 1280, 720, "VP9", "Opus", 42200000),
                new DownloadOption("audio", "Audio", "m4a", MediaKind.Audio, AudioCodec: "AAC", EstimatedBytes: 9200000),
                new DownloadOption("mp3", "Audio", "mp3", MediaKind.Audio, AudioCodec: "MP3")]);
        }
        public async Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress> progress, CancellationToken cancellationToken = default)
        {
            progress.Report(new DownloadProgress(49_536_000, 77_400_000, 3_000_000, TimeSpan.FromSeconds(9)));
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Preview transfers only end by cancellation.");
        }
    }
}
