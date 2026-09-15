namespace LumeFetch.Core.Downloads;

/// <summary>Publishes a completed local transfer to a host-managed destination (for example Android SAF).</summary>
public interface IDownloadOutput
{
    void ValidateDestination(string destinationDirectory);

    Task<DownloadResult> PublishAsync(DownloadResult localFile, string destinationDirectory,
        CancellationToken cancellationToken = default);
}
