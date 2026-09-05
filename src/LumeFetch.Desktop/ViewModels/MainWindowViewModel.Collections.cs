using System.Collections.ObjectModel;
using System.ComponentModel;
using LumeFetch.Core.Collections;
using LumeFetch.Core.Matching;
using LumeFetch.Core.Resolvers;
using LumeFetch.Core.Settings;
using LumeFetch.Desktop.Localization;
using LumeFetch.Infrastructure.Spotify;

namespace LumeFetch.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IMediaCollectionProvider? _collections;
    private IMediaResolver? _spotifyResolver;
    private ITrackSearch? _trackSearch;
    private SpotifySession? _spotifySession;
    private CancellationTokenSource? _batchCancellation;
    private readonly CancellationTokenSource _connectionCancellation = new();
    private bool _isBatchBusy;
    private bool _hasCollection;
    private bool _isSpotifyCollection;
    private bool _rightsConfirmed;
    private string _collectionTitle = string.Empty;
    private string? _batchMessage;
    private bool _collectionTruncated;
    private string _spotifyClientId = string.Empty;
    private string _spotifyStatusKey = "SpotifyDisconnected";
    private bool _isConnectingSpotify;
    private LanguageChoice _selectedLanguage = new("en", "English");
    private QualityChoice _batchQuality = new(CollectionQuality.Video1080, "Video1080");
    public ObservableCollection<CollectionRowViewModel> CollectionRows { get; } = [];
    public IReadOnlyList<LanguageChoice> Languages { get; } = [new("tr", "Türkçe"), new("en", "English")];
    public IReadOnlyList<QualityChoice> BatchQualities { get; } = [
        new(CollectionQuality.BestVideo, "BestVideo"), new(CollectionQuality.Video1080, "Video1080"),
        new(CollectionQuality.Video720, "Video720"), new(CollectionQuality.Audio, "Audio"),
        new(CollectionQuality.Mp3, "Mp3Audio")];
    public LanguageChoice SelectedLanguage
    {
        get => _selectedLanguage;
        set { if (SetProperty(ref _selectedLanguage, value)) Localizer.Current.SetLanguage(value.Code); }
    }
    public QualityChoice BatchQuality { get => _batchQuality; set => SetProperty(ref _batchQuality, value); }
    public string SpotifyClientId { get => _spotifyClientId; set => SetProperty(ref _spotifyClientId, value); }
    public string SpotifyStatus => Localizer.Current[_spotifyStatusKey];
    public string SpotifyRedirectUri { get; } = SpotifySession.RedirectUri;
    public bool IsConnectingSpotify { get => _isConnectingSpotify; private set => SetProperty(ref _isConnectingSpotify, value); }
    public bool HasCollection { get => _hasCollection; private set => SetProperty(ref _hasCollection, value); }
    public bool IsSpotifyCollection { get => _isSpotifyCollection; private set => SetProperty(ref _isSpotifyCollection, value); }
    public string CollectionTitle { get => _collectionTitle; private set => SetProperty(ref _collectionTitle, value); }
    public bool CollectionTruncated { get => _collectionTruncated; private set => SetProperty(ref _collectionTruncated, value); }
    public string CollectionSummary => Localizer.Current.Format("CollectionSummary", CollectionRows.Count, CollectionRows.Count(row => row.IsSelected && !row.IsEnqueued));
    public string? BatchMessage { get => _batchMessage; private set => SetProperty(ref _batchMessage, value); }
    public bool RightsConfirmed { get => _rightsConfirmed; set { SetProperty(ref _rightsConfirmed, value); BatchEnqueueCommand.RaiseCanExecuteChanged(); } }
    public bool IsBatchBusy
    {
        get => _isBatchBusy;
        private set
        {
            SetProperty(ref _isBatchBusy, value);
            BatchEnqueueCommand.RaiseCanExecuteChanged(); MatchTracksCommand.RaiseCanExecuteChanged();
        }
    }
    public AsyncRelayCommand BatchEnqueueCommand { get; private set; } = null!;
    public AsyncRelayCommand MatchTracksCommand { get; private set; } = null!;
    public RelayCommand StopBatchCommand { get; private set; } = null!;
    public RelayCommand SelectAllCommand { get; private set; } = null!;
    public RelayCommand SelectNoneCommand { get; private set; } = null!;
    public RelayCommand DisconnectSpotifyCommand { get; private set; } = null!;

    private void InitializeCollections(AppSettings settings, IMediaCollectionProvider? collections,
        IMediaResolver? spotifyResolver, ITrackSearch? search, SpotifySession? spotifySession)
    {
        _collections = collections; _spotifyResolver = spotifyResolver; _trackSearch = search; _spotifySession = spotifySession;
        _spotifyClientId = settings.SpotifyClientId ?? string.Empty;
        _selectedLanguage = Languages.Single(language => language.Code == settings.Language);
        Localizer.Current.SetLanguage(settings.Language);
        Localizer.Current.PropertyChanged += OnLanguageChanged;
        _batchQuality = BatchQualities[1];
        BatchEnqueueCommand = new AsyncRelayCommand(EnqueueCollectionAsync, () => !IsBatchBusy && RightsConfirmed && CollectionRows.Any(row => row.CanQueue));
        MatchTracksCommand = new AsyncRelayCommand(MatchTracksAsync, () => !IsBatchBusy && IsSpotifyCollection && CollectionRows.Any(row => row.IsSelected && row.CanSelect));
        StopBatchCommand = new RelayCommand(() => _batchCancellation?.Cancel());
        SelectAllCommand = new RelayCommand(() =>
        {
            foreach (var row in CollectionRows.Where(row => row.CanSelect && (!row.IsSpotify || row.SelectedCandidate is null || row.SelectedCandidate.Model.Confidence >= 90))) row.IsSelected = true;
        });
        SelectNoneCommand = new RelayCommand(() => { foreach (var row in CollectionRows) row.IsSelected = false; });
        DisconnectSpotifyCommand = new RelayCommand(() =>
        {
            _spotifySession?.Disconnect();
            _spotifyStatusKey = "SpotifyDisconnected"; OnPropertyChanged(nameof(SpotifyStatus));
            _requestVersion++; _analysisCancellation?.Cancel(); ClearResult(); IsAnalyzing = false;
        });
    }

    public async Task ConnectSpotifyAsync(Func<Uri, Task> openBrowser)
    {
        if (_spotifySession is null || IsConnectingSpotify) return;
        IsConnectingSpotify = true; _spotifyStatusKey = "SpotifyConnecting"; OnPropertyChanged(nameof(SpotifyStatus));
        try
        {
            await _spotifySession.ConnectAsync(SpotifyClientId.Trim(), openBrowser, _connectionCancellation.Token);
            _spotifyStatusKey = "SpotifyConnected";
        }
        catch (OperationCanceledException) { SettingsMessage = Localizer.Current["SpotifyLoginCanceled"]; _spotifyStatusKey = "SpotifyDisconnected"; }
        catch (Exception exception) { SettingsMessage = Localizer.Current["SpotifyFailed"] + exception.Message; _spotifyStatusKey = "SpotifyDisconnected"; }
        finally { IsConnectingSpotify = false; OnPropertyChanged(nameof(SpotifyStatus)); }
    }

    public void ReportBrowserFailure() => ErrorMessage = Localizer.Current["BrowserFailed"];

    private async Task<bool> TryAnalyzeCollectionAsync(Uri uri, int version, CancellationToken token)
    {
        if (_spotifyResolver?.CanResolve(uri) == true)
        {
            if (_spotifySession is { IsConnected: false }) throw new InvalidOperationException(Localizer.Current["SpotifyRequired"]);
            var catalog = await _spotifyResolver.ResolveAsync(uri, token);
            if (_disposed || version != _requestVersion) return true;
            IsSpotifyCollection = true; HasCollection = true; CollectionTitle = catalog.Title; CollectionTruncated = catalog.IsTruncated;
            BatchQuality = BatchQualities[3];
            var index = 0;
            foreach (var track in catalog.Tracks)
                AddRow(new CollectionRowViewModel(++index, track.Track.Title, track.Track.CatalogUri,
                    track.UnavailableReason, track.Track));
            return true;
        }
        if (_collections?.CanExpand(uri) != true) return false;
        var playlist = await _collections.ExpandAsync(uri, 200, token);
        if (_disposed || version != _requestVersion) return true;
        HasCollection = true; CollectionTitle = playlist.Title; CollectionTruncated = playlist.IsTruncated;
        foreach (var entry in playlist.Entries) AddRow(new CollectionRowViewModel(entry.Index, entry.Title, entry.SourceUri, entry.UnavailableReason));
        return true;
    }

    private void AddRow(CollectionRowViewModel row)
    {
        row.PropertyChanged += OnRowChanged;
        CollectionRows.Add(row);
        OnRowChanged(row, new PropertyChangedEventArgs(string.Empty));
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CollectionSummary));
        BatchEnqueueCommand.RaiseCanExecuteChanged(); MatchTracksCommand.RaiseCanExecuteChanged();
    }
    private void ClearCollection()
    {
        _batchCancellation?.Cancel();
        _batchCancellation = null;
        foreach (var row in CollectionRows) row.PropertyChanged -= OnRowChanged;
        CollectionRows.Clear();
        HasCollection = false; IsSpotifyCollection = false; RightsConfirmed = false;
        BatchMessage = null; IsBatchBusy = false;
    }

    private async Task MatchTracksAsync()
    {
        if (_trackSearch is null || IsBatchBusy) return;
        using var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation;
        IsBatchBusy = true; BatchMessage = null;
        var rows = CollectionRows.Where(row => row.IsSelected && row.CanSelect && row.Track is not null).ToArray();
        var matched = 0;
        try
        {
            foreach (var row in rows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                row.SetStatus("Searching"); row.Error = null;
                try
                {
                    using var perTrack = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    perTrack.CancelAfter(TimeSpan.FromSeconds(90));
                    var found = await _trackSearch.SearchAsync(row.Track!, perTrack.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var candidates = found.Select(candidate => TrackMatcher.Score(row.Track!, candidate)).OrderByDescending(candidate => candidate.Confidence).ToArray();
                    row.Candidates.Clear();
                    foreach (var candidate in candidates) row.Candidates.Add(new CandidateChoice(candidate));
                    row.SelectedCandidate = row.Candidates.FirstOrDefault();
                    row.IsSelected = row.SelectedCandidate?.Model.Confidence >= 90;
                    if (row.Candidates.Count == 0) row.SetStatus("NoMatch"); else matched++;
                }
                catch (Exception exception) when (!cancellation.IsCancellationRequested)
                {
                    row.SetStatus("Failed"); row.Error = exception.Message; row.IsSelected = false;
                }
                BatchMessage = Localizer.Current.Format("MatchCount", matched, rows.Length);
            }
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_batchCancellation, cancellation)) BatchMessage = Localizer.Current["BatchStopped"]; }
        finally
        {
            if (ReferenceEquals(_batchCancellation, cancellation)) { _batchCancellation = null; IsBatchBusy = false; }
        }
    }

    private async Task EnqueueCollectionAsync()
    {
        if (!RightsConfirmed || IsBatchBusy) return;
        using var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation; IsBatchBusy = true;
        // Snapshot choices and destination so edits cannot silently retarget work mid-batch.
        var selected = CollectionRows.Where(row => row.CanQueue).Select(row => (Row: row, Uri: row.DownloadUri!)).ToArray();
        var directory = DestinationDirectory;
        var preference = BatchQuality.Value;
        var queued = 0; var failed = 0;
        try
        {
            foreach (var item in selected)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                item.Row.SetStatus("Preparing"); item.Row.Error = null;
                try
                {
                    using var perItem = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    perItem.CancelAfter(TimeSpan.FromMinutes(2));
                    var media = await _analyzer.AnalyzeAsync(item.Uri.AbsoluteUri, perItem.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var option = CollectionOptionSelector.Select(media, preference);
                    _downloadManager.Enqueue(media, option, directory);
                    item.Row.IsEnqueued = true; item.Row.IsSelected = false; item.Row.SetStatus("Queued"); queued++;
                }
                catch (Exception exception) when (!cancellation.IsCancellationRequested)
                {
                    item.Row.SetStatus("Failed"); item.Row.Error = exception.Message; failed++;
                }
                BatchMessage = Localizer.Current.Format("BatchFinished", queued, failed);
            }
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_batchCancellation, cancellation)) BatchMessage = Localizer.Current["BatchStopped"]; }
        finally
        {
            if (ReferenceEquals(_batchCancellation, cancellation)) { _batchCancellation = null; IsBatchBusy = false; }
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) return;
        OnPropertyChanged(string.Empty);
        foreach (var row in CollectionRows) row.RefreshLanguage();
        foreach (var download in Downloads) download.RefreshLanguage();
        foreach (var option in Options) option.RefreshLanguage();
        foreach (var quality in BatchQualities) quality.RefreshLanguage();
    }
}

public sealed class QualityChoice(CollectionQuality value, string key) : ObservableObject
{
    public CollectionQuality Value { get; } = value;
    public string Label => Localizer.Current[key];
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
}
