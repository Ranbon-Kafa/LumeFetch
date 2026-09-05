using System.Collections.ObjectModel;
using LumeFetch.Core.Resolvers;
using LumeFetch.Desktop.Localization;

namespace LumeFetch.Desktop.ViewModels;

public sealed class CollectionRowViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isEnqueued;
    private string _statusKey = "Ready";
    private string? _error;
    private CandidateChoice? _selectedCandidate;
    public CollectionRowViewModel(int index, string title, Uri? source, string? unavailable = null, CatalogTrack? track = null)
    {
        Index = index; Title = title; OriginalUri = source; Track = track;
        IsAvailable = unavailable is null;
        _isSelected = IsAvailable;
        _statusKey = unavailable is not null ? "Unavailable" : track is null ? "Ready" : "MatchWaiting";
    }
    public int Index { get; }
    public string Title { get; }
    public Uri? OriginalUri { get; }
    public CatalogTrack? Track { get; }
    public bool IsSpotify => Track is not null;
    public bool IsAvailable { get; }
    public bool IsEnqueued { get => _isEnqueued; set { SetProperty(ref _isEnqueued, value); OnPropertyChanged(nameof(CanSelect)); } }
    public bool CanSelect => IsAvailable && !IsEnqueued;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public string Details => Track is null ? string.Empty : string.Join(", ", Track.Artists) +
        (string.IsNullOrWhiteSpace(Track.Album) ? string.Empty : " · " + Track.Album);
    public string? Error { get => _error; set { SetProperty(ref _error, value); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public string Status => Localizer.Current[_statusKey];
    public string Score => SelectedCandidate is null ? "—" : SelectedCandidate.Model.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/100";
    public Uri? DownloadUri => IsSpotify ? SelectedCandidate?.Model.SourceUri : OriginalUri;
    public bool CanQueue => CanSelect && IsSelected && DownloadUri is not null;
    public ObservableCollection<CandidateChoice> Candidates { get; } = [];
    public CandidateChoice? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (!SetProperty(ref _selectedCandidate, value)) return;
            SetStatus(value is null ? "NoMatch" : value.Model.Confidence >= 90 ? "HighConfidence" : value.Model.Confidence >= 70 ? "ReviewMatch" : "LowConfidence");
            OnPropertyChanged(nameof(Score)); OnPropertyChanged(nameof(DownloadUri));
        }
    }
    public void SetStatus(string key) { _statusKey = key; OnPropertyChanged(nameof(Status)); }
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);
}

public sealed record CandidateChoice(ResolverCandidate Model)
{
    public override string ToString() => $"{Model.Confidence}/100 · {Model.Title} — {Model.Artist}";
}
