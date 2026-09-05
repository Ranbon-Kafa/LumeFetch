namespace LumeFetch.Core.Processing;

public interface IFFmpegService
{
    string? ExecutablePath { get; }

    bool IsAvailable { get; }

    Task<string?> GetVersionAsync(CancellationToken cancellationToken = default);

    Task MuxAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken cancellationToken = default);

    Task ExtractAudioAsync(
        string inputPath,
        string outputPath,
        string codec,
        CancellationToken cancellationToken = default);
}
