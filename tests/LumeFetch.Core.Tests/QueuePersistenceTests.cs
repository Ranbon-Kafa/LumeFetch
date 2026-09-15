using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;
using LumeFetch.Infrastructure.Downloads;

namespace LumeFetch.Core.Tests;

public sealed class QueuePersistenceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("lumefetch-queue-").FullName;
    private static readonly DownloadOption Option = new("original", "Original", "mp4", MediaKind.Video,
        ProviderData: "{\"FormatSelector\":\"best\",\"OutputContainer\":\"mp4\",\"ExtractAudio\":false}");
    private static readonly MediaInfo Media = new("test", "Test", new Uri("https://example.org/video.mp4"), "Video", null, null, [Option]);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(DownloadStatus.Queued)]
    [InlineData(DownloadStatus.Downloading)]
    [InlineData(DownloadStatus.Processing)]
    [InlineData(DownloadStatus.Paused)]
    public async Task InterruptedWorkRestoresPausedWithSameIdentityAndNoAutomaticTransfer(DownloadStatus status)
    {
        var saved = Saved(status);
        var store = new MemoryStore(new(1, [saved]));
        var provider = new TestProvider();
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), queueStore: store);
        var row = Assert.Single(manager.Jobs);
        Assert.Equal(saved.Id, row.Id);
        Assert.Equal(saved.CreatedAt, row.CreatedAt);
        Assert.Equal(DownloadStatus.Paused, row.Status);
        Assert.Equal(25, row.BytesReceived);
        Assert.Equal(0, provider.Starts);
        Assert.Equal("QueueRecovered", manager.RecoveryNoticeKey);
        Assert.True(manager.Resume(row.Id));
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        Assert.Equal(row.Id, provider.LastId);
        Assert.Equal(1, provider.Starts);
    }

    [Fact]
    public async Task CompletedHistoryAndProviderPlanRoundTripWithoutReflection()
    {
        var path = Path.Combine(_directory, "queue.json");
        var store = new JsonDownloadQueueStore(path);
        var provider = new TestProvider();
        Guid id;
        await using (var manager = new DownloadManager(new ProviderRegistry([provider]), queueStore: store))
        {
            id = manager.Enqueue(Media, Option, _directory).Id;
            await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        }
        Assert.Equal(Option.ProviderData, store.Load().Jobs.Single().Option.ProviderData);
        await using var reopened = new DownloadManager(new ProviderRegistry([provider]), queueStore: new JsonDownloadQueueStore(path));
        Assert.Equal(id, Assert.Single(reopened.Jobs).Id);
        Assert.Equal(DownloadStatus.Completed, reopened.Jobs[0].Status);
        Assert.NotNull(reopened.Jobs[0].OutputPath);
        Assert.Equal(1, provider.Starts);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task OrderlyShutdownPersistsPausedWithoutChangingDesktopShutdown()
    {
        var store = new MemoryStore(new(1, []));
        var provider = new TestProvider { Wait = true };
        var manager = new DownloadManager(new ProviderRegistry([provider]), queueStore: store);
        var id = manager.Enqueue(Media, Option, _directory).Id;
        await UntilAsync(() => provider.Starts == 1);
        await manager.DisposeAsync();
        Assert.Equal(DownloadStatus.Paused, store.Checkpoint.Jobs.Single().Status);
        using var restored = new DownloadManager(new ProviderRegistry([provider]), queueStore: store);
        Assert.Equal(id, restored.Jobs[0].Id);
        Assert.Equal(1, provider.Starts);
    }

    [Fact]
    public async Task KnownFailedExportRetainsLocalFileAcrossRestart()
    {
        var local = Path.Combine(_directory, "complete.mp4");
        await File.WriteAllTextAsync(local, "test");
        var saved = Saved(DownloadStatus.Failed) with { PendingOutput = new DownloadResult(local, 4) };
        var provider = new TestProvider();
        var output = new TestOutput();
        var store = new MemoryStore(new(1, [saved]));
        await using var manager = new DownloadManager(new ProviderRegistry([provider]), output: output, queueStore: store);
        Assert.True(manager.Retry(saved.Id));
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        Assert.Equal(0, provider.Starts);
        Assert.Equal(1, output.Exports);
        Assert.Equal("Documents/result.mp4", manager.Jobs[0].OutputDisplayPath);
        Assert.Null(store.Checkpoint.Jobs[0].PendingOutput);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbiguousExportOrMissingLocalFileRequiresReviewAndCannotRetry(bool exportInProgress)
    {
        var saved = Saved(DownloadStatus.Processing) with
        {
            ExportInProgress = exportInProgress,
            PendingOutput = new DownloadResult(Path.Combine(_directory, "missing.mp4"), 100),
        };
        var store = new MemoryStore(new(1, [saved]));
        await using var manager = new DownloadManager(new ProviderRegistry([new TestProvider()]), queueStore: store);
        Assert.True(manager.Jobs[0].RequiresReview);
        Assert.Equal(DownloadStatus.Failed, manager.Jobs[0].Status);
        Assert.False(manager.Retry(saved.Id));
        Assert.False(manager.Resume(saved.Id));
        Assert.Equal("QueueExportInterrupted", manager.RecoveryNoticeKey);
        Assert.True(store.Checkpoint.Jobs[0].RequiresReview);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":99,\"Jobs\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Jobs\":[null]}")]
    public void UnreadableJournalIsPreservedAndBlocksNewWork(string content)
    {
        var path = Path.Combine(_directory, "queue.json");
        File.WriteAllText(path, content);
        using var manager = new DownloadManager(new ProviderRegistry([new TestProvider()]), queueStore: new JsonDownloadQueueStore(path));
        Assert.Equal("QueueStorageFailed", manager.RecoveryNoticeKey);
        Assert.Empty(manager.Jobs);
        Assert.Throws<IOException>(() => manager.Enqueue(Media, Option, _directory));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void HostRejectsUnsafeRestoredDirectoryWithoutOverwritingJournal()
    {
        var saved = Saved(DownloadStatus.Queued);
        var store = new MemoryStore(new(1, [saved]));
        using var manager = new DownloadManager(new ProviderRegistry([new TestProvider()]), queueStore: store,
            validateRestoredDirectory: _ => throw new IOException("Outside private transfers"));
        Assert.Empty(manager.Jobs);
        Assert.Equal(0, store.Saves);
        Assert.Equal(saved, store.Checkpoint.Jobs.Single());
    }

    [Fact]
    public void FailedCheckpointPreventsTransferBeforeSideEffects()
    {
        var store = new MemoryStore(new(1, [])) { FailWrites = true };
        var provider = new TestProvider();
        using var manager = new DownloadManager(new ProviderRegistry([provider]), queueStore: store);
        Assert.Throws<IOException>(() => manager.Enqueue(Media, Option, _directory));
        Assert.Equal(0, provider.Starts);
        Assert.Empty(manager.Jobs);
        Assert.Equal("QueueStorageFailed", manager.RecoveryNoticeKey);
    }

    [Fact]
    public async Task ExportIntentIsDurableBeforeExternalWrite()
    {
        var store = new MemoryStore(new(1, []));
        var output = new TestOutput
        {
            BeforeExport = () =>
            {
                var checkpoint = store.Checkpoint.Jobs.Single();
                Assert.True(checkpoint.ExportInProgress);
                Assert.NotNull(checkpoint.PendingOutput);
            },
        };
        await using var manager = new DownloadManager(new ProviderRegistry([new TestProvider()]), output: output, queueStore: store);
        manager.Enqueue(Media, Option, _directory);
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Completed);
        Assert.False(store.Checkpoint.Jobs[0].ExportInProgress);
    }

    private PersistedDownload Saved(DownloadStatus status) => new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1),
        Media, Option, _directory, status, 25, 100, null, null, null, null, false, false);

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class MemoryStore(DownloadQueueCheckpoint checkpoint) : IDownloadQueueStore
    {
        public DownloadQueueCheckpoint Checkpoint { get; private set; } = checkpoint;
        public int Saves { get; private set; }
        public bool FailWrites { get; init; }
        public DownloadQueueCheckpoint Load() => Checkpoint;
        public void Save(DownloadQueueCheckpoint value)
        {
            if (FailWrites) throw new IOException("Disk full");
            Checkpoint = value;
            Saves++;
        }
    }

    private sealed class TestProvider : IMediaProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public int Priority => 1;
        public int Starts;
        public Guid LastId;
        public bool Wait { get; init; }
        public bool CanHandle(Uri uri) => uri.Host == "example.org";
        public Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(Media);
        public async Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress> progress, CancellationToken cancellationToken = default)
        {
            LastId = context.JobId;
            Interlocked.Increment(ref Starts);
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new DownloadResult(Path.Combine(context.DestinationDirectory, "result.mp4"), 100);
        }
    }

    private sealed class TestOutput : IDownloadOutput
    {
        public int Exports;
        public Action? BeforeExport { get; init; }
        public void ValidateDestination(string destinationDirectory) { }
        public Task<DownloadResult> PublishAsync(DownloadResult localFile, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            BeforeExport?.Invoke();
            Interlocked.Increment(ref Exports);
            return Task.FromResult(localFile with { OutputPath = "content://test/result", DisplayPath = "Documents/result.mp4" });
        }
    }
}
