using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;

namespace LumeFetch.Core.Downloads;

/// <summary>Owns scheduling and state. Pausing stops a transfer and retains resumable files.</summary>
public sealed class DownloadManager : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly ProviderRegistry _providers;
    private readonly List<Job> _jobs = [];
    private readonly List<Task> _tasks = [];
    private int _limit;
    private int _active;
    private bool _disposed;

    public DownloadManager(ProviderRegistry providers, int maxParallelDownloads = 2)
    {
        _providers = providers;
        ValidateLimit(maxParallelDownloads);
        _limit = maxParallelDownloads;
    }

    public event EventHandler<DownloadJobSnapshot>? JobChanged;
    public IReadOnlyList<DownloadJobSnapshot> Jobs
    {
        get { lock (_sync) { return _jobs.AsEnumerable().Reverse().Select(Snapshot).ToArray(); } }
    }

    public int MaxParallelDownloads
    {
        get { lock (_sync) { return _limit; } }
        set
        {
            ValidateLimit(value);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _limit = value;
                Schedule();
            }
        }
    }

    public DownloadJobSnapshot Enqueue(MediaInfo media, DownloadOption option, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!media.Options.Contains(option))
            throw new ArgumentException("The option does not belong to the analyzed media.", nameof(option));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var job = new Job(media, option, _providers.GetById(media.ProviderId), Path.GetFullPath(destinationDirectory));
            _jobs.Add(job);
            Publish(job);
            Schedule();
            return Snapshot(job);
        }
    }

    public bool Pause(Guid jobId) => Change(jobId, job =>
    {
        if (job.Status is not (DownloadStatus.Downloading or DownloadStatus.Queued)) return false;
        job.Status = DownloadStatus.Paused;
        job.Cancellation?.Cancel();
        return true;
    });

    public bool Resume(Guid jobId) => Change(jobId, job =>
    {
        if (job.Status != DownloadStatus.Paused) return false;
        job.Status = DownloadStatus.Queued;
        return true;
    });

    public bool Cancel(Guid jobId) => Change(jobId, job =>
    {
        if (job.Status is DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Failed) return false;
        job.Status = DownloadStatus.Canceled;
        job.Cancellation?.Cancel();
        return true;
    });

    public bool Retry(Guid jobId) => Change(jobId, job =>
    {
        if (job.Status is not (DownloadStatus.Failed or DownloadStatus.Canceled)) return false;
        job.Status = DownloadStatus.Queued;
        job.Error = null;
        job.Progress = new DownloadProgress(0, null, 0, null);
        return true;
    });

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var job in _jobs)
            {
                if (job.Status is DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Failed) continue;
                job.Status = DownloadStatus.Canceled;
                job.Cancellation?.Cancel();
                Publish(job);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending;
        lock (_sync) { pending = _tasks.ToArray(); }
        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    private bool Change(Guid id, Func<Job, bool> action)
    {
        lock (_sync)
        {
            if (_disposed) return false;
            var job = _jobs.Find(item => item.Id == id);
            if (job is null || !action(job)) return false;
            Publish(job);
            Schedule();
            return true;
        }
    }

    // Under _sync: resumed/retried jobs wait until their previous attempt has exited.
    private void Schedule()
    {
        if (_disposed) return;
        foreach (var job in _jobs.Where(item => item.Status == DownloadStatus.Queued && !item.Running))
        {
            if (_active >= _limit) break;
            job.Running = true;
            job.Cancellation = new CancellationTokenSource();
            var token = job.Cancellation.Token;
            job.Status = DownloadStatus.Downloading;
            _active++;
            Publish(job);
            _tasks.Add(Task.Run(() => RunAsync(job, token)));
        }
        _tasks.RemoveAll(task => task.IsCompleted);
    }

    private async Task RunAsync(Job job, CancellationToken token)
    {
        try
        {
            var progress = new InlineProgress(value =>
            {
                lock (_sync)
                {
                    if (token.IsCancellationRequested || job.Status is not (DownloadStatus.Downloading or DownloadStatus.Processing)) return;
                    job.Progress = value;
                    job.Status = value.Stage == "Processing" ? DownloadStatus.Processing : DownloadStatus.Downloading;
                    Publish(job);
                }
            });
            var result = await job.Provider.DownloadAsync(
                new DownloadContext(job.Id, job.Media, job.Option, job.Directory, new PauseController()), progress, token).ConfigureAwait(false);
            lock (_sync)
            {
                // A successful return means the provider atomically committed a complete output.
                job.Output = result.OutputPath;
                job.Progress = new DownloadProgress(result.BytesWritten, result.BytesWritten, 0, TimeSpan.Zero);
                job.Status = DownloadStatus.Completed;
                job.Error = null;
                Publish(job);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Pause/cancel/retry already set the intended next state.
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (!token.IsCancellationRequested)
                {
                    job.Error = exception.Message;
                    job.Status = DownloadStatus.Failed;
                    Publish(job);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                job.Cancellation?.Dispose();
                job.Cancellation = null;
                job.Running = false;
                _active--;
                Schedule();
            }
        }
    }

    private void Publish(Job job) => JobChanged?.Invoke(this, Snapshot(job));
    private static DownloadJobSnapshot Snapshot(Job job) => new(
        job.Id, job.CreatedAt, job.Media.Title, job.Provider.DisplayName, job.Option.Container.ToUpperInvariant() + " · " + job.Option.QualityLabel,
        job.Status, job.Progress.Percentage, job.Progress.BytesReceived, job.Progress.TotalBytes,
        job.Progress.BytesPerSecond, job.Progress.EstimatedRemaining, job.Output, job.Error);

    private static void ValidateLimit(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 6);
    }

    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class Job(MediaInfo media, DownloadOption option, IMediaProvider provider, string directory)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public MediaInfo Media { get; } = media;
        public DownloadOption Option { get; } = option;
        public IMediaProvider Provider { get; } = provider;
        public string Directory { get; } = directory;
        public bool Running { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
        public DownloadProgress Progress { get; set; } = new(0, null, 0, null);
        public string? Output { get; set; }
        public string? Error { get; set; }
    }
}
