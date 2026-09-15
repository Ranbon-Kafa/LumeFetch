namespace LumeFetch.Core.Processing;

public static class FFmpegValidation
{
    /// <summary>Checks the executable before fetching media that will need post-processing.</summary>
    public static async Task EnsureReadyAsync(this IFFmpegService service, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (string.IsNullOrWhiteSpace(await service.GetVersionAsync(timeout.Token).ConfigureAwait(false)))
                throw new InvalidOperationException("FFmpeg is unavailable. Restart the app and try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("FFmpeg/FFprobe did not respond within 10 seconds. Restart the app and try again.");
        }
    }
}
