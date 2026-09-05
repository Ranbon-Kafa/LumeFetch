namespace LumeFetch.Core.Media;

public sealed record DownloadOption(
    string Id,
    string Label,
    string Container,
    MediaKind Kind,
    int? Width = null,
    int? Height = null,
    string? VideoCodec = null,
    string? AudioCodec = null,
    long? EstimatedBytes = null,
    string? ProviderData = null)
{
    public string QualityLabel => Height is > 0 ? $"{Height}p" : Label;
}
