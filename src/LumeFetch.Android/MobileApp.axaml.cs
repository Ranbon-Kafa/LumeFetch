using System.Net;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LumeFetch.Android.Runtime;
using LumeFetch.Android.Services;
using LumeFetch.Android.Storage;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Processing;
using LumeFetch.Core.Services;
using LumeFetch.Core.Settings;
using LumeFetch.Infrastructure.Downloads;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Infrastructure.YtDlp;
using LumeFetch.Presentation.ViewModels;
using LumeFetch.Presentation.Views;
using DownloadManager = LumeFetch.Core.Downloads.DownloadManager;

namespace LumeFetch.Android;

public sealed partial class MobileApp : Avalonia.Application, IDisposable
{
    private HttpClient? _http;
    private DownloadManager? _manager;
    private AndroidDownloadOutput? _output;
    private AndroidQueueLifetime? _queueLifetime;
    private string? _pendingUrl;
#if DEBUG
    private AndroidTools? _debugTools;
    private bool _smokeRequested;
    private bool _smokeStarted;
    internal void RequestRuntimeSmoke()
    {
        _smokeRequested = true;
        if (_debugTools is null || _smokeStarted) return;
        _smokeStarted = true;
        _ = Task.Run(() => AndroidRuntimeSmoke.RunAsync(_debugTools, global::Android.App.Application.Context.FilesDir!.AbsolutePath));
    }
#endif
    public MainWindowViewModel? ViewModel { get; private set; }
    internal DownloadManager? Queue => _manager;
    internal void SuspendTransfers(string message) => _queueLifetime?.Suspend(message);

    public void Dispose()
    {
        ViewModel?.Dispose();
        _queueLifetime?.Dispose();
        _manager?.Dispose();
        _http?.Dispose();
        _output?.Dispose();
    }

    internal void ReceiveUrl(string url)
    {
        if (ViewModel is null) { _pendingUrl = url; return; }
        Dispatcher.UIThread.Post(() => { ViewModel.IsSettingsOpen = false; ViewModel.Url = url; });
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            // A single-view lifetime attaches its root once. Keep that root and swap its content.
            var host = new ContentControl { Background = Brush.Parse("#080C12") };
            TopLevel.SetAutoSafeAreaPadding(host, true);
            host.Content = new Border
            {
                Background = Brush.Parse("#080C12"),
                Padding = new Avalonia.Thickness(24),
                Child = new TextBlock { Text = "LumeFetch · Preparing bundled tools…", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
            };
            lifetime.MainView = host;
            _ = InitializeRuntimeAsync(host);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeRuntimeAsync(ContentControl host)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var package = context.PackageManager?.GetPackageInfo(context.PackageName!, 0);
            var packageVersion = package?.VersionName ?? "development";
            var androidApi = (int)global::Android.OS.Build.VERSION.SdkInt;
            var buildCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? package?.LongVersionCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
            var processingContext = $"Android API {androidApi} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} · {Environment.SystemPageSize} B pages\nLumeFetch {packageVersion} · build {buildCode}";
            var tools = await Task.Run(() => AndroidTools.PrepareAsync(context));
#if DEBUG
            _debugTools = tools;
            if (_smokeRequested) RequestRuntimeSmoke();
#endif
            _output = new AndroidDownloadOutput(context);
            var defaults = new AppSettings { DownloadDirectory = _output.DefaultDirectory };
            var store = new JsonSettingsStore(Path.Combine(context.FilesDir!.AbsolutePath, "settings.json"), defaults);
            var settings = store.Load();
            _http = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(20),
            })
            { Timeout = Timeout.InfiniteTimeSpan };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("LumeFetch/1.0");
            var ffmpeg = new FFmpegService(command: tools.CreateFFmpegCommand(settings.FFmpegPath),
                probeCommand: tools.CreateFFmpegCommand(tools.FFprobePath));
            var extractor = new YtDlpClient(ffmpegPath: ffmpeg.ExecutablePath,
                embedMetadata: settings.EmbedMetadata, embedThumbnail: settings.EmbedThumbnail,
                command: tools.CreateYtDlpCommand(settings.YtDlpPath), javaScriptRuntime: tools.JavaScriptRuntime,
                processingCheck: ffmpeg.EnsureReadyAsync);
            var providers = new ProviderRegistry([
                new YouTubeProvider(extractor), new InstagramProvider(extractor), new TikTokProvider(extractor),
                new TwitterProvider(extractor), new RedditProvider(extractor), new GenericHttpMediaProvider(_http, ffmpeg)]);
            var journal = new JsonDownloadQueueStore(Path.Combine(context.FilesDir.AbsolutePath, "queue.json"));
            _manager = await Task.Run(() => new DownloadManager(providers, settings.MaxParallelDownloads, _output,
                journal, _output.ValidateWorkingDirectory));
            ViewModel = new MainWindowViewModel(new MediaAnalysisService(providers), _manager, ffmpeg,
                store, settings, _http, collections: extractor, platformName: "Android", destinationLabel: _output.GetLabel,
                applicationVersion: packageVersion, processingContext: processingContext);
            _queueLifetime = new AndroidQueueLifetime(context, _manager,
                message => Dispatcher.UIThread.Post(() => ViewModel.ReportPlatformFailure(message)));
            // The application, not an Activity instance, owns the queue across rotation.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var view = new MainView { DataContext = ViewModel, FolderPicker = _output.PickFolderAsync };
                host.Content = view;
                if (_pendingUrl is { } url) { _pendingUrl = null; ReceiveUrl(url); }
            });
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("LumeFetch", "Runtime initialization failed: " + exception);
            _manager?.Dispose();
            _queueLifetime?.Dispose();
            _http?.Dispose();
            _output?.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() => host.Content = new Border
            {
                Background = Brush.Parse("#080C12"),
                Padding = new Avalonia.Thickness(24),
                Child = new TextBlock
                {
                    Text = "LumeFetch could not initialize its bundled runtime.\n\n" + exception.Message,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                },
            });
        }
    }
}
