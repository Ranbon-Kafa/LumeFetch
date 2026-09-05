namespace LumeFetch.Core.Media;

public sealed record MediaInfo(
    string ProviderId,
    string SourceName,
    Uri SourceUri,
    string Title,
    TimeSpan? Duration,
    Uri? ThumbnailUri,
    IReadOnlyList<DownloadOption> Options,
    string? Author = null,
    string? Album = null,
    string? Isrc = null)
{
    public DownloadOption GetOption(string optionId) =>
        Options.FirstOrDefault(option => option.Id == optionId)
        ?? throw new ArgumentException($"Unknown download option '{optionId}'.", nameof(optionId));
}
