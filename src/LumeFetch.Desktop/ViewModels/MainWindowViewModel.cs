using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LumeFetch.Core.Collections;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Processing;
using LumeFetch.Core.Resolvers;
using LumeFetch.Core.Services;
using LumeFetch.Core.Settings;
using LumeFetch.Desktop.Localization;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Infrastructure.Spotify;

namespace LumeFetch.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly MediaAnalysisService _analyzer;
    private readonly DownloadManager _downloadManager;
    private readonly IFFmpegService _ffmpeg;
    private readonly JsonSettingsStore _settingsStore;
    private readonly HttpClient? _httpClient;
    private CancellationTokenSource? _analysisCancellation;
    private int _requestVersion;
    private bool _disposed;
    private string _url = string.Empty;
    private string _destinationDirectory;
    private bool _isAnalyzing;
    private string? _errorMessage;
    private MediaInfo? _media;
    private DownloadOptionViewModel? _selectedOption;
    private Bitmap? _thumbnail;
    private string _ffmpegStatus = "CheckingFFmpeg";
    private bool _isSettingsOpen;
    private bool _smartPaste;
    private int _parallelDownloads;
    private string _ytDlpPath;
    private string _ffmpegPath;
    private bool _embedMetadata;
    private bool _embedThumbnail;
    private string? _settingsMessage;

    public MainWindowViewModel(MediaAnalysisService analyzer, DownloadManager downloadManager,
        IFFmpegService ffmpeg, JsonSettingsStore? settingsStore = null, AppSettings? settings = null, HttpClient? httpClient = null,
        IMediaCollectionProvider? collections = null, IMediaResolver? spotifyResolver = null, ITrackSearch? trackSearch = null, SpotifySession? spotifySession = null)
    {
        _analyzer = analyzer;
        _downloadManager = downloadManager;
        _ffmpeg = ffmpeg;
        _settingsStore = settingsStore ?? JsonSettingsStore.CreateDefault();
        _httpClient = httpClient;
        settings ??= new AppSettings();
        _destinationDirectory = settings.DownloadDirectory;
        _parallelDownloads = settings.MaxParallelDownloads;
        _smartPaste = settings.SmartPaste;
        _ytDlpPath = settings.YtDlpPath ?? string.Empty;
        _ffmpegPath = settings.FFmpegPath ?? string.Empty;
        _embedMetadata = settings.EmbedMetadata;
        _embedThumbnail = settings.EmbedThumbnail;
        _settingsMessage = _settingsStore.LoadWarning;
        _errorMessage = _settingsStore.LoadWarning;
        AnalyzeCommand = new AsyncRelayCommand(() => AnalyzeAsync(), () => !string.IsNullOrWhiteSpace(Url) && !IsAnalyzing);
        EnqueueCommand = new RelayCommand(Enqueue, CanEnqueue);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        ShowSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        ShowHomeCommand = new RelayCommand(() => IsSettingsOpen = false);
        InitializeCollections(settings, collections, spotifyResolver, trackSearch, spotifySession);
        _downloadManager.JobChanged += OnJobChanged;
    }

    public string Url
    {
        get => _url;
        set
        {
            if (!SetProperty(ref _url, value)) return;
            _requestVersion++;
            _analysisCancellation?.Cancel();
            ClearResult();
            IsAnalyzing = false;
            ErrorMessage = null;
            AnalyzeCommand.RaiseCanExecuteChanged();
            if (SmartPaste && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                _ = AnalyzeAsync(600);
        }
    }

    public string DestinationDirectory
    {
        get => _destinationDirectory;
        set { if (SetProperty(ref _destinationDirectory, value)) EnqueueCommand.RaiseCanExecuteChanged(); }
    }
    public bool IsSettingsOpen { get => _isSettingsOpen; set => SetProperty(ref _isSettingsOpen, value); }
    public bool SmartPaste { get => _smartPaste; set => SetProperty(ref _smartPaste, value); }
    public int ParallelDownloads { get => _parallelDownloads; set => SetProperty(ref _parallelDownloads, value); }
    public string ParallelLabel => Localizer.Current.Format("ParallelLabel", _downloadManager.MaxParallelDownloads);
    public string YtDlpPath { get => _ytDlpPath; set => SetProperty(ref _ytDlpPath, value); }
    public string FFmpegPath { get => _ffmpegPath; set => SetProperty(ref _ffmpegPath, value); }
    public bool EmbedMetadata { get => _embedMetadata; set => SetProperty(ref _embedMetadata, value); }
    public bool EmbedThumbnail { get => _embedThumbnail; set => SetProperty(ref _embedThumbnail, value); }
    public string? SettingsMessage { get => _settingsMessage; private set => SetProperty(ref _settingsMessage, value); }
    public string SettingsLocation => _settingsStore.FilePath;

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(ref _isAnalyzing, value)) return;
            OnPropertyChanged(nameof(AnalyzeButtonText));
            AnalyzeCommand.RaiseCanExecuteChanged();
            EnqueueCommand.RaiseCanExecuteChanged();
        }
    }
    public string AnalyzeButtonText => Localizer.Current[IsAnalyzing ? "Analyzing" : "Analyze"];
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasResult => _media is not null;
    public Bitmap? Thumbnail { get => _thumbnail; private set => SetProperty(ref _thumbnail, value); }
    public string MediaTitle => _media?.Title ?? string.Empty;
    public string MediaSource => _media?.SourceName ?? string.Empty;
    public string MediaDuration => _media?.Duration is { } duration ? FormatDuration(duration) : Localizer.Current["DurationUnknown"];
    public string ResultSummary => Localizer.Current.Format("OptionsFound", Options.Count);
    public string FfmpegStatus { get => Localizer.Current[_ffmpegStatus]; private set => SetProperty(ref _ffmpegStatus, value); }
    public ObservableCollection<DownloadOptionViewModel> Options { get; } = [];
    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];
    public bool HasDownloads => Downloads.Count > 0;

    public DownloadOptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set { if (SetProperty(ref _selectedOption, value)) EnqueueCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand AnalyzeCommand { get; }
    public RelayCommand EnqueueCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowHomeCommand { get; }

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var version = await _ffmpeg.GetVersionAsync(timeout.Token);
            FfmpegStatus = version is null ? "FFmpegMissing" : "FFmpegReady";
        }
        catch (Exception) { FfmpegStatus = "FFmpegMissing"; }
    }

    public Task AnalyzeFromPasteAsync(string clipboardText)
    {
        Url = clipboardText.Trim();
        return AnalyzeAsync();
    }

    public void ReportPlatformFailure(string messageKey) => ErrorMessage = Localizer.Current[messageKey];

    public async Task ShutdownAsync()
    {
        Dispose();
        await _downloadManager.DisposeAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestVersion++;
        _downloadManager.JobChanged -= OnJobChanged;
        _analysisCancellation?.Cancel();
        _batchCancellation?.Cancel();
        _connectionCancellation.Cancel();
        Localizer.Current.PropertyChanged -= OnLanguageChanged;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(new AppSettings
            {
                DownloadDirectory = DestinationDirectory,
                MaxParallelDownloads = ParallelDownloads,
                SmartPaste = SmartPaste,
                YtDlpPath = string.IsNullOrWhiteSpace(YtDlpPath) ? null : YtDlpPath.Trim(),
                FFmpegPath = string.IsNullOrWhiteSpace(FFmpegPath) ? null : FFmpegPath.Trim(),
                EmbedMetadata = EmbedMetadata,
                EmbedThumbnail = EmbedThumbnail,
                Language = SelectedLanguage.Code,
                SpotifyClientId = string.IsNullOrWhiteSpace(SpotifyClientId) ? null : SpotifyClientId.Trim(),
            });
            _downloadManager.MaxParallelDownloads = ParallelDownloads;
            OnPropertyChanged(nameof(ParallelLabel));
            SettingsMessage = Localizer.Current["SettingsSaved"];
        }
        catch (Exception exception) { SettingsMessage = Localizer.Current["SaveFailed"] + exception.Message; }
    }

    private async Task AnalyzeAsync(int delayMilliseconds = 0)
    {
        _analysisCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _analysisCancellation = cancellation;
        var version = ++_requestVersion;
        var url = Url.Trim();
        ClearResult();
        ErrorMessage = null;
        try
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cancellation.Token);
            if (_disposed) return;
            IsAnalyzing = true;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && await TryAnalyzeCollectionAsync(uri, version, cancellation.Token)) return;
            var media = await _analyzer.AnalyzeAsync(url, cancellation.Token);
            if (_disposed || version != _requestVersion) return;
            _media = media;
            foreach (var option in media.Options) Options.Add(new DownloadOptionViewModel(option));
            SelectedOption = Options.FirstOrDefault();
            NotifyMedia();
            await LoadThumbnailAsync(media.ThumbnailUri, version, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _requestVersion) ErrorMessage = Localizer.Current["AnalysisCanceled"];
        }
        catch (Exception exception)
        {
            if (!_disposed && version == _requestVersion) { ClearResult(); ErrorMessage = exception.Message; }
        }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation)) _analysisCancellation = null;
            if (!_disposed && version == _requestVersion) IsAnalyzing = false;
        }
    }

    private async Task LoadThumbnailAsync(Uri? uri, int version, CancellationToken token)
    {
        if (_httpClient is null || uri is null || uri.Scheme is not ("http" or "https")) return;
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 4_194_304) return;
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var memory = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await source.ReadAsync(buffer, token)) > 0)
            {
                if (memory.Length + count > 4_194_304) return;
                memory.Write(buffer, 0, count);
            }
            if (_disposed || version != _requestVersion) return;
            memory.Position = 0;
            Thumbnail = Bitmap.DecodeToWidth(memory, 256);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A missing thumbnail must not prevent downloading. */ }
    }

    private void ClearResult()
    {
        ClearCollection();
        _media = null;
        Options.Clear();
        SelectedOption = null;
        Thumbnail?.Dispose();
        Thumbnail = null;
        NotifyMedia();
    }

    private void NotifyMedia()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(MediaTitle));
        OnPropertyChanged(nameof(MediaSource));
        OnPropertyChanged(nameof(MediaDuration));
        OnPropertyChanged(nameof(ResultSummary));
        EnqueueCommand.RaiseCanExecuteChanged();
    }

    private bool CanEnqueue() => !IsAnalyzing && _media is not null && SelectedOption is not null &&
        Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri) && _media.SourceUri == uri &&
        !string.IsNullOrWhiteSpace(DestinationDirectory);

    private void Enqueue()
    {
        if (!CanEnqueue()) return;
        try
        {
            _downloadManager.Enqueue(_media!, SelectedOption!.Model, DestinationDirectory);
            ErrorMessage = null;
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }

    private void OnJobChanged(object? sender, DownloadJobSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            // Read latest state, so delayed dispatcher callbacks cannot revert a completed row.
            var current = _downloadManager.Jobs.FirstOrDefault(job => job.Id == snapshot.Id);
            if (current is null) return;
            var existing = Downloads.FirstOrDefault(item => item.Id == current.Id);
            if (existing is null)
            {
                Downloads.Insert(0, new DownloadItemViewModel(current, _downloadManager));
                OnPropertyChanged(nameof(HasDownloads));
            }
            else existing.Update(current);
        });
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
