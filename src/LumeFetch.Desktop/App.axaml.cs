using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Services;
using LumeFetch.Desktop.Views;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Infrastructure.YtDlp;
using LumeFetch.Presentation.ViewModels;

namespace LumeFetch.Desktop;

public sealed partial class App : Application, IDisposable
{
    private HttpClient? _httpClient;
    private DownloadManager? _downloadManager;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(20),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LumeFetch/1.0");

            var settingsStore = JsonSettingsStore.CreateDefault();
            var settings = settingsStore.Load();
            // Spotify is deliberately not registered in v1 pending distribution-policy review.
            var ffmpeg = new FFmpegService(settings.FFmpegPath);
            var ytDlp = new YtDlpClient(settings.YtDlpPath, ffmpeg.ExecutablePath, settings.EmbedMetadata, settings.EmbedThumbnail);
            var providers = new ProviderRegistry(
            [
                new YouTubeProvider(ytDlp),
                new InstagramProvider(ytDlp),
                new TikTokProvider(ytDlp),
                new TwitterProvider(ytDlp),
                new RedditProvider(ytDlp),
                new GenericHttpMediaProvider(_httpClient, ffmpeg),
            ]);

            var analyzer = new MediaAnalysisService(providers);
            _downloadManager = new DownloadManager(providers, settings.MaxParallelDownloads);
            var viewModel = new MainWindowViewModel(analyzer, _downloadManager, ffmpeg, settingsStore, settings, _httpClient,
                collections: ytDlp);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        _downloadManager?.Dispose();
        _downloadManager = null;
        _httpClient?.Dispose();
        _httpClient = null;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) => Dispose();
}
