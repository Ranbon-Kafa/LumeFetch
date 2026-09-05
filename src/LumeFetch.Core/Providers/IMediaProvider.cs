using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;

namespace LumeFetch.Core.Providers;

public interface IMediaProvider
{
    string Id { get; }

    string DisplayName { get; }

    int Priority { get; }

    bool CanHandle(Uri uri);

    Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default);

    Task<DownloadResult> DownloadAsync(
        DownloadContext context,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default);
}
