using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Processing;
using LumeFetch.Core.Providers;
using LumeFetch.Infrastructure.Files;

namespace LumeFetch.Infrastructure.Providers;

public sealed class GenericHttpMediaProvider : IMediaProvider
{
    private const int BufferSize = 128 * 1024;
    private static readonly HashSet<string> RecognizedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v",
        ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wav",
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
    };

    private readonly HttpClient _httpClient;
    private readonly IFFmpegService? _ffmpeg;

    public GenericHttpMediaProvider(HttpClient httpClient, IFFmpegService? ffmpeg = null)
    {
        _httpClient = httpClient;
        _ffmpeg = ffmpeg;
    }

    public string Id => "generic-http";

    public string DisplayName => "Direct media";

    public int Priority => -1_000;

    public bool CanHandle(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public async Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var response = await SendMetadataRequestAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var extension = GetExtension(uri, contentType);

        if (!IsMediaContent(contentType, extension))
        {
            throw new MediaAnalysisException(
                "This address points to a web page rather than a direct media file. " +
                "A platform-specific provider is required for this source.");
        }

        var fileName = GetFileName(response.Content.Headers, uri, extension);
        var title = Path.GetFileNameWithoutExtension(fileName);
        var totalBytes = response.Content.Headers.ContentRange?.Length
            ?? response.Content.Headers.ContentLength;
        var mediaKind = GetMediaKind(contentType, extension);
        var container = extension.TrimStart('.').ToLowerInvariant();

        var option = new DownloadOption(
            "original",
            "Original",
            string.IsNullOrWhiteSpace(container) ? "bin" : container,
            mediaKind,
            EstimatedBytes: totalBytes);
        List<DownloadOption> options = [option];
        if (_ffmpeg is { IsAvailable: true } && mediaKind is MediaKind.Audio or MediaKind.Video && container != "mp3")
            options.Add(new DownloadOption("mp3-convert", "Audio", "mp3", MediaKind.Audio, AudioCodec: "MP3"));

        return new MediaInfo(
            Id,
            DisplayName,
            uri,
            title,
            null,
            null,
            options);
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadContext context,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var convertToMp3 = context.Option.Id == "mp3-convert";
        if (convertToMp3 && (_ffmpeg is not { IsAvailable: true } || context.Option.Container != "mp3"))
            throw new InvalidOperationException("FFmpeg is required for MP3 conversion.");
        if (convertToMp3) await _ffmpeg!.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(context.DestinationDirectory);

        var extension = NormalizeExtension(context.Option.Container);
        var fileName = SafeFileName.Create(context.Media.Title) + extension;
        var work = DownloadFiles.WorkingDirectory(context.DestinationDirectory, context.JobId);
        var partialPath = Path.Combine(work, "download.part");
        var validatorPath = Path.Combine(work, "etag.txt");
        var validator = File.Exists(validatorPath) ? await File.ReadAllTextAsync(validatorPath, cancellationToken).ConfigureAwait(false) : null;
        var canValidate = EntityTagHeaderValue.TryParse(validator, out var entityTag) && !entityTag.IsWeak;
        var existingBytes = canValidate && File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, context.Media.SourceUri);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            request.Headers.IfRange = new RangeConditionHeaderValue(entityTag!);
        }

        using var response = await SendDownloadRequestAsync(request, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (!IsMediaContent(response.Content.Headers.ContentType?.MediaType, extension))
            throw new MediaAnalysisException("The server returned a web page instead of media.");
        if (response.Content.Headers.ContentEncoding.Any(value => value != "identity"))
            throw new IOException("Encoded HTTP responses cannot be safely resumed.");
        var canResume = response.StatusCode == HttpStatusCode.PartialContent;
        if (canResume && (response.Content.Headers.ContentRange?.From != existingBytes ||
            (existingBytes > 0 && response.Headers.ETag is { } actualTag && !actualTag.Equals(entityTag))))
            throw new IOException("The server returned an inconsistent byte range or changed media. Retry in a new job.");
        await File.WriteAllTextAsync(validatorPath, response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : string.Empty, cancellationToken).ConfigureAwait(false);
        if (existingBytes > 0 && !canResume)
        {
            existingBytes = 0;
        }

        var totalBytes = response.Content.Headers.ContentRange?.Length
            ?? (response.Content.Headers.ContentLength is { } length ? length + existingBytes : null);

        var fileMode = canResume ? FileMode.Append : FileMode.Create;
        await using var destination = new FileStream(
            partialPath,
            fileMode,
            FileAccess.Write,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        var received = existingBytes;
        var receivedThisRun = 0L;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.FromMilliseconds(-150);

        progress.Report(CreateProgress(received, totalBytes, 0));

        while (true)
        {
            await context.Control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var read = await source.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            receivedThisRun += read;

            var speed = stopwatch.Elapsed.TotalSeconds > 0
                ? receivedThisRun / stopwatch.Elapsed.TotalSeconds
                : 0;

            if (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(150) || received == totalBytes)
            {
                progress.Report(CreateProgress(received, totalBytes, speed));
                lastReport = stopwatch.Elapsed;
            }
        }

        if (totalBytes is { } expected && received != expected)
            throw new IOException("The connection ended before the complete media arrived. Retry to resume.");
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Close();
        cancellationToken.ThrowIfCancellationRequested();
        var completedPath = partialPath;
        if (convertToMp3)
        {
            progress.Report(new DownloadProgress(received, received, 0, null, "Processing"));
            // Keep the source partial for retry; a canceled conversion cannot poison the next attempt.
            completedPath = Path.Combine(work, "converted-" + Guid.NewGuid().ToString("N") + ".mp3");
            await _ffmpeg!.ExtractAudioAsync(partialPath, completedPath, "libmp3lame", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        var outputPath = DownloadFiles.Commit(completedPath, context.DestinationDirectory, fileName);
        var outputBytes = new FileInfo(outputPath).Length;
        progress.Report(new DownloadProgress(outputBytes, outputBytes, 0, TimeSpan.Zero));
        return new DownloadResult(outputPath, outputBytes);
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable || request.Headers.Range is null) return response;
        response.Dispose();
        // A fully cached or changed resource may yield 416. Restart rather than commit unverified bytes.
        using var restart = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
        restart.Headers.AcceptEncoding.ParseAdd("identity");
        return await _httpClient.SendAsync(restart, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendMetadataRequestAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
        var response = await _httpClient
            .SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented))
        {
            return response;
        }

        response.Dispose();
        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 0);

        try
        {
            return await _httpClient
                .SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            rangeRequest.Dispose();
        }
    }

    private static DownloadProgress CreateProgress(long received, long? total, double speed)
    {
        TimeSpan? remaining = total is > 0 && speed > 0
            ? TimeSpan.FromSeconds(Math.Max(0, (total.Value - received) / speed))
            : null;

        return new DownloadProgress(received, total, speed, remaining);
    }

    private static string GetFileName(HttpContentHeaders headers, Uri uri, string extension)
    {
        var headerName = headers.ContentDisposition?.FileNameStar
            ?? headers.ContentDisposition?.FileName;
        var value = headerName?.Trim('"');

        if (string.IsNullOrWhiteSpace(value))
        {
            value = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            value = "download" + extension;
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(value)) && !string.IsNullOrWhiteSpace(extension))
        {
            value += extension;
        }

        return SafeFileName.Create(value);
    }

    private static string GetExtension(Uri uri, string? contentType)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (RecognizedExtensions.Contains(extension))
        {
            return extension.ToLowerInvariant();
        }

        return contentType?.ToLowerInvariant() switch
        {
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "audio/opus" => ".opus",
            "audio/flac" => ".flac",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => extension,
        };
    }

    private static bool IsMediaContent(string? contentType, string extension) =>
        contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ||
        contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true ||
        contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
        string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(contentType) && RecognizedExtensions.Contains(extension);

    private static MediaKind GetMediaKind(string? contentType, string extension)
    {
        if (contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return MediaKind.Video;
        }

        if (contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return MediaKind.Audio;
        }

        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return MediaKind.Image;
        }

        return extension.ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" or ".m4v" => MediaKind.Video,
            ".mp3" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".flac" or ".wav" => MediaKind.Audio,
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => MediaKind.Image,
            _ => MediaKind.File,
        };
    }

    private static string NormalizeExtension(string container)
    {
        var normalized = container.Trim().TrimStart('.').ToLowerInvariant();
        if (normalized.Length is < 1 or > 10 || !normalized.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Invalid media extension.", nameof(container));
        return "." + normalized;
    }
}
