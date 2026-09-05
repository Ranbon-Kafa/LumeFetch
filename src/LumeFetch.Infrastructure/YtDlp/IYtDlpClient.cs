using LumeFetch.Core.Downloads;

namespace LumeFetch.Infrastructure.YtDlp;

public interface IYtDlpClient
{
    bool IsAvailable { get; }

    Task<YtDlpMetadata> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default);

    Task<DownloadResult> DownloadAsync(
        YtDlpDownloadRequest request,
        IDownloadControl control,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default);
}
