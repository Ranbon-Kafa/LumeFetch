using LumeFetch.Core.Media;

namespace LumeFetch.Core.Downloads;

/// <summary>Private, versioned queue journal. Save must replace the previous checkpoint atomically.</summary>
public interface IDownloadQueueStore
{
    DownloadQueueCheckpoint Load();
    void Save(DownloadQueueCheckpoint checkpoint);
}

public sealed record DownloadQueueCheckpoint(int SchemaVersion, IReadOnlyList<PersistedDownload> Jobs);

public sealed record PersistedDownload(
    Guid Id, DateTimeOffset CreatedAt, MediaInfo Media, DownloadOption Option, string Directory,
    DownloadStatus Status, long BytesReceived, long? TotalBytes, string? OutputPath,
    string? OutputDisplayPath, string? Error, DownloadResult? PendingOutput,
    bool ExportInProgress, bool RequiresReview);
