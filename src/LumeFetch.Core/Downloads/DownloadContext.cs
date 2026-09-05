using LumeFetch.Core.Media;

namespace LumeFetch.Core.Downloads;

public sealed record DownloadContext(
    Guid JobId,
    MediaInfo Media,
    DownloadOption Option,
    string DestinationDirectory,
    IDownloadControl Control);
