using System.Collections.Concurrent;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;

namespace LumeFetch.Core.Tests;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task PauseReleasesSlotAndResumeWaitsForItsTurn()
    {
        var provider = new ControlledProvider();
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), 1);
        var first = manager.Enqueue(Media, Option, Path.GetTempPath());
        var second = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => provider.Starts.ContainsKey(first.Id));
        Assert.Equal(DownloadStatus.Queued, manager.Jobs.Single(job => job.Id == second.Id).Status);
        Assert.True(manager.Pause(first.Id));
        await UntilAsync(() => provider.Starts.ContainsKey(second.Id));
        Assert.True(manager.Resume(first.Id));
        Assert.Equal(DownloadStatus.Queued, manager.Jobs.Single(job => job.Id == first.Id).Status);
        Assert.True(manager.Cancel(second.Id));
        await UntilAsync(() => provider.Starts[first.Id] == 2);
        provider.Completion.TrySetResult();
        await UntilAsync(() => manager.Jobs.Single(job => job.Id == first.Id).Status == DownloadStatus.Completed);
        Assert.Equal(1, provider.PeakActive);
    }

    [Fact]
    public async Task IncreasingLimitStartsWaitingJobsAndCancelThenRetryDoesNotOverlap()
    {
        var provider = new ControlledProvider();
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), 1);
        var first = manager.Enqueue(Media, Option, Path.GetTempPath());
        var second = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => provider.Starts.ContainsKey(first.Id));
        manager.MaxParallelDownloads = 2;
        await UntilAsync(() => provider.Starts.ContainsKey(second.Id));
        Assert.True(manager.Cancel(first.Id));
        Assert.True(manager.Retry(first.Id));
        await UntilAsync(() => provider.Starts[first.Id] == 2);
        Assert.True(provider.PeakActive <= 2);
        provider.Completion.TrySetResult();
        await UntilAsync(() => manager.Jobs.All(job => job.Status == DownloadStatus.Completed));
        Assert.All(manager.Jobs, job => Assert.Equal(100, job.Percentage));
    }

    [Fact]
    public async Task FailedJobCanRetryAndLateProgressCannotRevertCompletion()
    {
        var provider = new ControlledProvider { FailFirst = true };
        await using var manager = new DownloadManager(new ProviderRegistry([provider]));
        var job = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Failed);
        Assert.True(manager.Retry(job.Id));
        provider.Completion.TrySetResult();
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        provider.LastProgress!.Report(new DownloadProgress(1, 100, 1, null, "Processing"));
        Assert.Equal(DownloadStatus.Completed, manager.Jobs[0].Status);
    }

    [Fact]
    public async Task ShutdownAwaitsWorkersAndRejectsNewJobs()
    {
        var provider = new ControlledProvider();
        var manager = new DownloadManager(new ProviderRegistry([provider]));
        var job = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => provider.Starts.ContainsKey(job.Id));
        await manager.DisposeAsync();
        Assert.Equal(0, provider.Active);
        Assert.Equal(DownloadStatus.Canceled, manager.Jobs[0].Status);
        Assert.Throws<ObjectDisposedException>(() => manager.Enqueue(Media, Option, Path.GetTempPath()));
    }

    [Fact]
    public void EnqueueRejectsForeignOptionsAndInvalidLimits()
    {
        using var manager = new DownloadManager(new ProviderRegistry([new ControlledProvider()]));
        Assert.Throws<ArgumentException>(() => manager.Enqueue(Media, Option with { Id = "foreign" }, Path.GetTempPath()));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.MaxParallelDownloads = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.MaxParallelDownloads = 7);
    }

    private static readonly DownloadOption Option = new("original", "Original", "mp4", MediaKind.Video);

    [Fact]
    public async Task ExportFailureRetainsCompletedTransferForRetry()
    {
        var provider = new ControlledProvider();
        provider.Completion.TrySetResult();
        var output = new ControlledOutput { FailFirst = true };
        output.Completion.TrySetResult();
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), output: output);
        var job = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Failed);
        Assert.Equal(1, provider.Starts[job.Id]);
        Assert.True(manager.Retry(job.Id));
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        Assert.Equal(1, provider.Starts[job.Id]);
        Assert.Equal(2, output.Attempts);
        Assert.Equal("content://test/export.mp4", manager.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task CancelDuringExportDoesNotRedownloadWhenRetried()
    {
        var provider = new ControlledProvider();
        provider.Completion.TrySetResult();
        var output = new ControlledOutput();
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), output: output);
        var job = manager.Enqueue(Media, Option, Path.GetTempPath());
        await UntilAsync(() => output.Attempts == 1);
        Assert.Equal(DownloadStatus.Processing, manager.Jobs[0].Status);
        Assert.True(manager.Cancel(job.Id));
        Assert.True(manager.Retry(job.Id));
        await UntilAsync(() => output.Attempts == 2);
        output.Completion.TrySetResult();
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        Assert.Equal(1, provider.Starts[job.Id]);
    }

    [Fact]
    public void MissingDestinationGrantRejectsJobBeforeTransfer()
    {
        var provider = new ControlledProvider();
        using var manager = new DownloadManager(new ProviderRegistry([provider]), output: new ControlledOutput { RejectDestination = true });
        Assert.Throws<IOException>(() => manager.Enqueue(Media, Option, Path.GetTempPath()));
        Assert.Empty(manager.Jobs);
        Assert.Empty(provider.Starts);
    }

    private sealed class ControlledOutput : IDownloadOutput
    {
        public bool FailFirst { get; init; }
        public bool RejectDestination { get; init; }
        public int Attempts;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ValidateDestination(string destinationDirectory)
        {
            if (RejectDestination) throw new IOException("Folder access missing");
        }
        public async Task<DownloadResult> PublishAsync(DownloadResult localFile, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref Attempts);
            if (FailFirst && attempt == 1) throw new IOException("Temporary export failure");
            await Completion.Task.WaitAsync(cancellationToken);
            return localFile with { OutputPath = "content://test/export.mp4" };
        }
    }
    private static readonly MediaInfo Media = new("test", "Test", new Uri("https://example.org/test.mp4"), "test", null, null, [Option]);

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledProvider : IMediaProvider
    {
        private int _active;
        private int _peak;
        public bool FailFirst { get; init; }
        public int Active => _active;
        public int PeakActive => _peak;
        public ConcurrentDictionary<Guid, int> Starts { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<DownloadProgress>? LastProgress { get; private set; }
        public string Id => "test";
        public string DisplayName => "Test";
        public int Priority => 1;
        public bool CanHandle(Uri uri) => true;
        public Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(Media);
        public async Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress> progress, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Exchange(ref _peak, Math.Max(_peak, active));
            var attempt = Starts.AddOrUpdate(context.JobId, 1, (_, count) => count + 1);
            try
            {
                if (FailFirst && attempt == 1) throw new IOException("Temporary failure");
                LastProgress = progress;
                progress.Report(new DownloadProgress(25, 100, 10, null));
                await Completion.Task.WaitAsync(cancellationToken);
                return new DownloadResult("test.mp4", 100);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
