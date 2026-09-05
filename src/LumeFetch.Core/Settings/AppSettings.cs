namespace LumeFetch.Core.Settings;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string DownloadDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int MaxParallelDownloads { get; init; } = 2;
    public bool SmartPaste { get; init; } = true;
    public string? YtDlpPath { get; init; }
    public string? FFmpegPath { get; init; }
    public bool EmbedMetadata { get; init; } = true;
    public bool EmbedThumbnail { get; init; }
    public string Language { get; init; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    public string? SpotifyClientId { get; init; }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings version.");
        if (Language is not ("tr" or "en")) throw new InvalidDataException("Unsupported language.");
        if (!string.IsNullOrWhiteSpace(SpotifyClientId) && (SpotifyClientId.Length != 32 || !SpotifyClientId.All(char.IsAsciiLetterOrDigit)))
            throw new InvalidDataException("Spotify Client ID must contain 32 letters/digits, not a secret.");
        if (!Path.IsPathFullyQualified(DownloadDirectory)) throw new InvalidDataException("Choose an absolute download folder.");
        if (MaxParallelDownloads is < 1 or > 6) throw new InvalidDataException("Parallel downloads must be between 1 and 6.");
        foreach (var path in new[] { YtDlpPath, FFmpegPath })
            if (!string.IsNullOrWhiteSpace(path) && (!Path.IsPathFullyQualified(path) || !File.Exists(path)))
                throw new InvalidDataException("A configured tool was not found. Choose its full executable path or leave it blank.");
    }
}
