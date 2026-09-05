namespace LumeFetch.Infrastructure.YtDlp;

public sealed record YtDlpMediaFormat(
    string Id,
    string Extension,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? AudioCodec,
    long? EstimatedBytes,
    double? Bitrate)
{
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoCodec) && VideoCodec != "none";

    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec) && AudioCodec != "none";
}

public sealed record YtDlpMetadata(
    string Id,
    string Title,
    string? Author,
    TimeSpan? Duration,
    Uri? ThumbnailUri,
    IReadOnlyList<YtDlpMediaFormat> Formats);

public sealed record YtDlpDownloadPlan(
    string FormatSelector,
    string OutputContainer,
    bool ExtractAudio);

public sealed record YtDlpDownloadRequest(
    Uri SourceUri,
    string FileStem,
    string DestinationDirectory,
    YtDlpDownloadPlan Plan,
    Guid JobId = default);
