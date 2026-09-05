namespace LumeFetch.Core.Downloads;

public interface IDownloadControl
{
    bool IsPaused { get; }

    ValueTask WaitIfPausedAsync(CancellationToken cancellationToken);
}
