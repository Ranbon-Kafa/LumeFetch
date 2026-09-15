namespace LumeFetch.Core.Downloads;

public sealed partial class DownloadManager
{
    private readonly IDownloadQueueStore? _queueStore;
    private bool _persistenceFailed;
    private long _lastCheckpoint;
    public string? RecoveryNoticeKey { get; private set; }
    public event EventHandler? PersistenceFailed;

    private void RestoreQueue(Action<string>? validateDirectory)
    {
        if (_queueStore is null) return;
        try
        {
            var checkpoint = _queueStore.Load();
            if (checkpoint.SchemaVersion != 1 || checkpoint.Jobs is null || checkpoint.Jobs.Count > 2000)
                throw new InvalidDataException("Unsupported queue checkpoint.");
            var restored = new List<Job>();
            var ids = new HashSet<Guid>();
            foreach (var saved in checkpoint.Jobs)
            {
                if (saved is null || saved.Id == Guid.Empty || !ids.Add(saved.Id) || saved.Media is null || saved.Option is null ||
                    string.IsNullOrWhiteSpace(saved.Media.ProviderId) || string.IsNullOrWhiteSpace(saved.Media.Title) ||
                    string.IsNullOrWhiteSpace(saved.Option.Id) || string.IsNullOrWhiteSpace(saved.Option.Container) || !Enum.IsDefined(saved.Option.Kind) ||
                    saved.Media.SourceUri is not { IsAbsoluteUri: true, Scheme: "http" or "https" } ||
                    saved.Media.Options is null || !saved.Media.Options.Contains(saved.Option) ||
                    !Enum.IsDefined(saved.Status) || saved.BytesReceived < 0 || saved.TotalBytes is < 0 ||
                    string.IsNullOrWhiteSpace(saved.Directory) || !Path.IsPathFullyQualified(saved.Directory))
                    throw new InvalidDataException("Invalid saved download.");
                var directory = Path.GetFullPath(saved.Directory);
                validateDirectory?.Invoke(directory);
                var provider = _providers.GetById(saved.Media.ProviderId);
                if (!provider.CanHandle(saved.Media.SourceUri)) throw new InvalidDataException("Saved source does not match its provider.");
                if (saved.PendingOutput is { } pending &&
                    (!Path.IsPathFullyQualified(pending.OutputPath) || pending.BytesWritten < 0 ||
                     !Path.GetFullPath(pending.OutputPath).StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                         OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new InvalidDataException("Saved output is outside its transfer directory.");
                var review = saved.RequiresReview || saved.ExportInProgress ||
                    (saved.PendingOutput is { } local && !File.Exists(local.OutputPath));
                var interrupted = saved.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing;
                restored.Add(new Job(saved.Media, saved.Option, provider, directory)
                {
                    Id = saved.Id,
                    CreatedAt = saved.CreatedAt,
                    Status = review ? DownloadStatus.Failed : interrupted ? DownloadStatus.Paused : saved.Status,
                    Progress = new DownloadProgress(saved.BytesReceived, saved.TotalBytes, 0, null),
                    Output = saved.OutputPath,
                    OutputDisplayPath = saved.OutputDisplayPath,
                    Error = review ? "QueueExportInterrupted" : saved.Error,
                    PendingOutput = saved.PendingOutput,
                    RequiresReview = review,
                });
            }
            _jobs.AddRange(restored);
            if (_jobs.Any(job => job.RequiresReview)) RecoveryNoticeKey = "QueueExportInterrupted";
            else if (_jobs.Any(job => job.Status == DownloadStatus.Paused)) RecoveryNoticeKey = "QueueRecovered";
            // No scheduling on startup, including after an explicit force-stop.
            if (_jobs.Count > 0) SaveCheckpoint();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException or KeyNotFoundException or NotSupportedException)
        {
            _jobs.Clear();
            FailPersistence();
        }
    }

    // Called under _sync, except during construction. Throttle progress only, never state transitions.
    private bool SaveCheckpoint(bool progressOnly = false)
    {
        if (_queueStore is null) return true;
        if (_persistenceFailed) return false;
        if (progressOnly && Environment.TickCount64 - _lastCheckpoint < 2000) return true;
        try
        {
            _queueStore.Save(new DownloadQueueCheckpoint(1, _jobs.Select(job => new PersistedDownload(
                job.Id, job.CreatedAt, job.Media, job.Option, job.Directory, job.Status,
                job.Progress.BytesReceived, job.Progress.TotalBytes, job.Output, job.OutputDisplayPath,
                job.Error, job.PendingOutput, job.ExportInProgress, job.RequiresReview)).ToArray()));
            _lastCheckpoint = Environment.TickCount64;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            FailPersistence();
            return false;
        }
    }

    private void FailPersistence()
    {
        _persistenceFailed = true;
        RecoveryNoticeKey = "QueueStorageFailed";
        PersistenceFailed?.Invoke(this, EventArgs.Empty);
    }
}
