namespace LumeFetch.Core.Downloads;

public sealed record DownloadJobSnapshot(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Title,
    string ProviderName,
    string OptionLabel,
    DownloadStatus Status,
    double? Percentage,
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    string? OutputPath,
    string? ErrorMessage);
