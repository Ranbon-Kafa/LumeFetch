using System.Globalization;
using System.Windows.Input;
using LumeFetch.Core.Downloads;
using LumeFetch.Desktop.Localization;

namespace LumeFetch.Desktop.ViewModels;

public sealed class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadManager _manager;
    private DownloadJobSnapshot _snapshot;

    public DownloadItemViewModel(DownloadJobSnapshot snapshot, DownloadManager manager)
    {
        _snapshot = snapshot;
        _manager = manager;
        PrimaryActionCommand = new RelayCommand(ExecutePrimaryAction, () => CanUsePrimaryAction);
        CancelCommand = new RelayCommand(() => _manager.Cancel(Id), () => CanCancel);
    }

    public Guid Id => _snapshot.Id;
    public string Title => _snapshot.Title;
    public string Meta => $"{_snapshot.ProviderName}  •  {_snapshot.OptionLabel}";
    public string Status => Localizer.Current[_snapshot.Status.ToString()];
    public double ProgressValue => _snapshot.Percentage ?? 0;
    public bool IsIndeterminate => _snapshot.Percentage is null && _snapshot.Status is DownloadStatus.Downloading or DownloadStatus.Processing;
    public bool CanCancel => _snapshot.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Processing;
    public bool CanUsePrimaryAction => _snapshot.Status is DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled;
    public string PrimaryActionLabel => _snapshot.Status switch
    {
        DownloadStatus.Paused => Localizer.Current["Resume"],
        DownloadStatus.Failed or DownloadStatus.Canceled => Localizer.Current["Retry"],
        _ => Localizer.Current["Pause"],
    };

    public string ProgressText
    {
        get
        {
            if (_snapshot.Status == DownloadStatus.Completed)
            {
                return _snapshot.OutputPath ?? Localizer.Current["Completed"];
            }

            if (_snapshot.Status == DownloadStatus.Failed)
            {
                return _snapshot.ErrorMessage ?? Localizer.Current["Failed"];
            }

            var received = FormatBytes(_snapshot.BytesReceived);
            var total = _snapshot.TotalBytes is { } totalBytes ? FormatBytes(totalBytes) : "—";
            var speed = _snapshot.Status == DownloadStatus.Downloading && _snapshot.BytesPerSecond > 0 ? $"{FormatBytes((long)_snapshot.BytesPerSecond)}/s" : "—";
            var eta = _snapshot.EstimatedRemaining is { } remaining ? FormatDuration(remaining) : "—";
            var percent = _snapshot.Percentage is { } value ? value.ToString("0", CultureInfo.InvariantCulture) + "%  •  " : string.Empty;
            return $"{percent}{received} / {total}  •  {speed}  •  " + Localizer.Current.Format("Remaining", eta);
        }
    }

    public ICommand PrimaryActionCommand { get; }
    public ICommand CancelCommand { get; }
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

    public void Update(DownloadJobSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanUsePrimaryAction));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(ProgressText));
        ((RelayCommand)PrimaryActionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
    }

    private void ExecutePrimaryAction()
    {
        switch (_snapshot.Status)
        {
            case DownloadStatus.Downloading:
                _manager.Pause(Id);
                break;
            case DownloadStatus.Paused:
                _manager.Resume(Id);
                break;
            case DownloadStatus.Failed:
            case DownloadStatus.Canceled:
                _manager.Retry(Id);
                break;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var amount = (double)bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return $"{amount:0.#} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
