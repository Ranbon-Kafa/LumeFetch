using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;
using LumeFetch.Infrastructure.Files;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public abstract class YtDlpMediaProvider : IMediaProvider
{
    private readonly IYtDlpClient _client;
    private readonly HashSet<string> _hosts;

    protected YtDlpMediaProvider(IYtDlpClient client, params string[] hosts)
    {
        _client = client;
        _hosts = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
    }

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public int Priority => 1_000;

    public bool CanHandle(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https" && _hosts.Contains(uri.IdnHost);

    public async Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var metadata = await _client.AnalyzeAsync(uri, cancellationToken).ConfigureAwait(false);
        var options = CreateOptions(metadata.Formats);

        if (options.Count == 0)
        {
            throw new MediaAnalysisException($"{DisplayName} did not expose a downloadable video or audio format for this item.");
        }

        return new MediaInfo(
            Id,
            DisplayName,
            uri,
            metadata.Title,
            metadata.Duration,
            metadata.ThumbnailUri,
            options,
            metadata.Author);
    }

    public Task<DownloadResult> DownloadAsync(
        DownloadContext context,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Option.ProviderData))
        {
            throw new InvalidOperationException("The selected format is missing its download plan.");
        }

        var plan = YtDlpPlanJson.Deserialize(context.Option.ProviderData);
        var request = new YtDlpDownloadRequest(
            context.Media.SourceUri,
            SafeFileName.Create(context.Media.Title),
            context.DestinationDirectory,
            plan,
            context.JobId);

        return _client.DownloadAsync(request, context.Control, progress, cancellationToken);
    }

    private static List<DownloadOption> CreateOptions(IReadOnlyList<YtDlpMediaFormat> formats)
    {
        var options = formats
            .Where(format => format.HasVideo)
            .GroupBy(format => (format.Height, format.Extension))
            .Select(group => group
                .OrderByDescending(format => string.Equals(format.Extension, "mp4", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(format => format.HasAudio)
                .ThenByDescending(format => format.Bitrate ?? 0)
                .First())
            .OrderByDescending(format => format.Height)
            .Take(8)
            .Select(format => CreateVideoOption(format, formats))
            .OfType<DownloadOption>()
            .ToList();

        var audio = formats
            .Where(format => format.HasAudio && !format.HasVideo)
            .OrderByDescending(format => string.Equals(format.Extension, "m4a", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(format => format.Bitrate ?? 0)
            .FirstOrDefault();

        if (audio is not null)
        {
            var container = audio.Extension is "m4a" or "opus" or "mp3" or "flac" ? audio.Extension : "opus";
            var plan = new YtDlpDownloadPlan(audio.Id, container, ExtractAudio: audio.Extension != container);
            options.Add(new DownloadOption(
                $"audio-{audio.Id}",
                "Audio",
                container,
                MediaKind.Audio,
                AudioCodec: audio.AudioCodec,
                EstimatedBytes: audio.EstimatedBytes,
                ProviderData: YtDlpPlanJson.Serialize(plan)));
        }

        if (audio is null && formats.FirstOrDefault(format => format.HasAudio && format.HasVideo) is { } combined)
        {
            options.Add(new DownloadOption("audio-extract", "Audio", "m4a", MediaKind.Audio,
                ProviderData: YtDlpPlanJson.Serialize(new YtDlpDownloadPlan(combined.Id, "m4a", ExtractAudio: true))));
        }
        // MP3 is a real FFmpeg conversion, not a renamed M4A/Opus file.
        var mp3Source = formats.Where(format => format.HasAudio && !format.HasVideo)
            .OrderByDescending(format => format.Bitrate ?? 0).FirstOrDefault()
            ?? formats.Where(format => format.HasAudio && format.HasVideo)
                .OrderByDescending(format => format.Bitrate ?? 0).FirstOrDefault();
        if (mp3Source is not null && !options.Any(option => option.Kind == MediaKind.Audio && option.Container == "mp3"))
        {
            options.Add(new DownloadOption("audio-mp3", "Audio", "mp3", MediaKind.Audio,
                AudioCodec: "MP3", EstimatedBytes: null,
                ProviderData: YtDlpPlanJson.Serialize(new YtDlpDownloadPlan(mp3Source.Id, "mp3", ExtractAudio: true))));
        }
        return options;
    }

    private static DownloadOption? CreateVideoOption(YtDlpMediaFormat format, IReadOnlyList<YtDlpMediaFormat> formats)
    {
        var container = string.Equals(format.Extension, "mp4", StringComparison.OrdinalIgnoreCase)
            ? "mp4"
            : "webm";
        var preferredAudioExtension = container == "mp4" ? "m4a" : "webm";
        var audio = formats.Where(item => !item.HasVideo && item.HasAudio)
            .OrderByDescending(item => item.Extension == preferredAudioExtension)
            .ThenByDescending(item => item.Bitrate).FirstOrDefault();
        if (!format.HasAudio && audio is null) return null;
        if (!format.HasAudio && audio!.Extension != preferredAudioExtension) container = "mkv";
        var selector = format.HasAudio ? format.Id : $"{format.Id}+{audio!.Id}";
        var plan = new YtDlpDownloadPlan(selector, container, ExtractAudio: false);

        return new DownloadOption(
            $"video-{format.Id}",
            format.Height is > 0 ? $"{format.Height}p" : "Original",
            container,
            MediaKind.Video,
            format.Width,
            format.Height,
            format.VideoCodec,
            format.HasAudio ? format.AudioCodec : "best audio",
            format.HasAudio ? format.EstimatedBytes : format.EstimatedBytes + audio!.EstimatedBytes,
            YtDlpPlanJson.Serialize(plan));
    }
}
