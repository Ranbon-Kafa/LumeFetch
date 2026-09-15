using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Infrastructure.Files;
using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Infrastructure.YtDlp;

public sealed partial class YtDlpClient : IYtDlpClient
{
    private const string OutputMarker = "__LUMEFETCH_FILE__";
    private const string ProgressMarker = "__LUMEFETCH_PROGRESS__";
    private readonly string? _ffmpegPath;
    private readonly string? _javaScriptRuntime;
    private readonly ToolCommand? _command;
    private readonly bool _embedMetadata;
    private readonly bool _embedThumbnail;
    private readonly Func<CancellationToken, Task>? _processingCheck;

    public YtDlpClient(string? executablePath = null, string? ffmpegPath = null, bool embedMetadata = true, bool embedThumbnail = false,
        ToolCommand? command = null, string? javaScriptRuntime = null, Func<CancellationToken, Task>? processingCheck = null)
    {
        ExecutablePath = command?.ExecutablePath ?? executablePath ?? ExternalToolLocator.Find(
            "yt-dlp",
            Path.Combine(Environment.CurrentDirectory, ".tools", "yt-dlp"));
        _ffmpegPath = ffmpegPath;
        _command = command ?? (ExecutablePath is null ? null : new ToolCommand(ExecutablePath));
        var denoPath = javaScriptRuntime is null ? ExternalToolLocator.Find("deno", Path.Combine(Environment.CurrentDirectory, ".tools", "deno")) : null;
        _javaScriptRuntime = javaScriptRuntime ?? (denoPath is null ? null : "deno:" + denoPath);
        _embedMetadata = embedMetadata;
        _embedThumbnail = embedThumbnail;
        _processingCheck = processingCheck;
    }

    public string? ExecutablePath { get; }

    public bool IsAvailable => ExecutablePath is not null;

    public async Task<YtDlpMetadata> AnalyzeAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        var output = await RunCaptureAsync(
            [
                "--dump-single-json",
                "--no-playlist",
                "--playlist-end", "1",
                "--no-warnings",
                "--skip-download",
                "--socket-timeout",
                "20",
                uri.AbsoluteUri,
            ],
            cancellationToken).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (GetString(root, "_type") is "playlist" or "multi_video" || root.TryGetProperty("entries", out _))
                throw new MediaAnalysisException("This provider requires an individual video. Use the collection preview for YouTube playlists; profiles and carousels are unsupported.");
            if (root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True)
                throw new MediaAnalysisException("Live broadcasts are not supported in this preview.");
            if (root.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True)
                throw new MediaAnalysisException("DRM-protected media is not supported.");
            var id = GetString(root, "id") ?? throw new MediaAnalysisException("The source did not return a media id.");
            var title = GetString(root, "title") ?? id;
            var author = GetString(root, "uploader") ?? GetString(root, "channel");
            var durationSeconds = GetDouble(root, "duration");
            TimeSpan? duration = durationSeconds is > 0 and < 315360000 ? TimeSpan.FromSeconds(durationSeconds.Value) : null;
            var thumbnail = GetUri(root, "thumbnail");
            var formats = ReadFormats(root);

            return new YtDlpMetadata(id, title, author, duration, thumbnail, formats);
        }
        catch (JsonException exception)
        {
            throw new MediaAnalysisException("The extraction backend returned invalid metadata.", exception);
        }
    }

    public async Task<DownloadResult> DownloadAsync(
        YtDlpDownloadRequest request,
        IDownloadControl control,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        cancellationToken.ThrowIfCancellationRequested();
        var needsProcessing = _embedMetadata || _embedThumbnail || request.Plan.ExtractAudio || request.Plan.FormatSelector.Contains('+');
        if (needsProcessing && _ffmpegPath is null)
            throw new FileNotFoundException("FFmpeg is required for this selection. Configure it in Settings and restart.");
        if (needsProcessing && _processingCheck is not null)
            await _processingCheck(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(request.DestinationDirectory);
        var destination = request.DestinationDirectory;
        var work = DownloadFiles.WorkingDirectory(destination, request.JobId == Guid.Empty ? Guid.NewGuid() : request.JobId);
        request = request with { DestinationDirectory = work };

        while (true)
        {
            await control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            var result = await RunDownloadAttemptAsync(
                request,
                control,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (result.WasPaused)
            {
                continue;
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"yt-dlp failed: {LastUsefulLine(result.StandardError)}");
            }

            var outputPath = ResolveOutputPath(request.DestinationDirectory, result.OutputPath);
            var bytes = new FileInfo(outputPath).Length;
            progress.Report(new DownloadProgress(bytes, bytes, 0, TimeSpan.Zero));
            cancellationToken.ThrowIfCancellationRequested();
            var finalPath = DownloadFiles.Commit(outputPath, destination, request.FileStem + Path.GetExtension(outputPath));
            return new DownloadResult(finalPath, bytes);
        }
    }

    private async Task<DownloadAttemptResult> RunDownloadAttemptAsync(
        YtDlpDownloadRequest request,
        IDownloadControl control,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var outputTemplate = Path.Combine(
            request.DestinationDirectory,
            "media.%(ext)s");
        var arguments = new List<string>
        {
            "--newline",
            "--no-simulate",
            "--progress",
            "--socket-timeout", "20",
            "--retries", "3",
            "--fragment-retries", "3",
            "--no-playlist",
            "--no-warnings",
            "--continue",
            "--no-overwrites",
            "--progress-template",
            $"download:{ProgressMarker}%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s",
            "--progress-template",
            "postprocess:__LUMEFETCH_PROCESSING__",
            "--print",
            $"after_move:{OutputMarker}%(filepath)s",
            "--output",
            outputTemplate,
            "--format",
            request.Plan.FormatSelector,
        };

        if (OperatingSystem.IsWindows())
        {
            arguments.Add("--windows-filenames");
        }

        if (_ffmpegPath is not null)
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(_ffmpegPath);
        }

        if (request.Plan.ExtractAudio)
        {
            arguments.Add("--extract-audio");
            arguments.Add("--audio-format");
            arguments.Add(request.Plan.OutputContainer);
            arguments.Add("--audio-quality");
            arguments.Add("0");
        }
        else if (request.Plan.OutputContainer is "mp4" or "webm" or "mkv")
        {
            arguments.Add("--merge-output-format");
            arguments.Add(request.Plan.OutputContainer);
        }

        if (_embedMetadata) arguments.Add("--embed-metadata");
        if (_embedThumbnail && request.Plan.OutputContainer is "m4a" or "mp3" or "opus" or "flac" or "mp4" or "mkv")
            arguments.Add("--embed-thumbnail");
        arguments.Add("--");
        arguments.Add(request.SourceUri.AbsoluteUri);

        using var process = CreateProcess(arguments);
        process.Start();
        var outputTask = ReadOutputAsync(process.StandardOutput, progress);
        var errorTask = ReadOutputAsync(process.StandardError, progress);
        var wasPaused = false;

        try
        {
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (control.IsPaused)
                {
                    wasPaused = true;
                    _command!.Terminate(process);
                    break;
                }

                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                _command!.Terminate(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new DownloadAttemptResult(process.ExitCode, output.OutputPath, error.Errors, wasPaused);
    }

    private async Task<string> RunCaptureAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        using var process = CreateProcess(arguments);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                _command!.Terminate(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new MediaAnalysisException($"yt-dlp could not analyze this media: {LastUsefulLine(error)}");
        }

        return output;
    }

    private Process CreateProcess(IReadOnlyList<string> arguments)
    {
        EnsureAvailable();
        var startInfo = _command!.CreateStartInfo([]);

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        foreach (var flag in new[] { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-js-runtimes", "--encoding", "utf-8" })
            startInfo.ArgumentList.Add(flag);
        if (_javaScriptRuntime is not null)
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add(_javaScriptRuntime);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private static async Task<OutputReadResult> ReadOutputAsync(
        StreamReader reader,
        IProgress<DownloadProgress> progress)
    {
        string? outputPath = null;
        var errors = new Queue<string>();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(OutputMarker, StringComparison.Ordinal))
            {
                outputPath = line[OutputMarker.Length..].Trim();
                continue;
            }

            var markerIndex = line.IndexOf(ProgressMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
                ReportProgress(line[(markerIndex + ProgressMarker.Length)..], progress);
            else if (line.Contains("__LUMEFETCH_PROCESSING__", StringComparison.Ordinal))
                progress.Report(new DownloadProgress(0, null, 0, null, "Processing"));
            else
            {
                errors.Enqueue(line.Length > 2000 ? line[..2000] : line);
                if (errors.Count > 20) errors.Dequeue();
            }
        }

        return new OutputReadResult(outputPath, string.Join("\n", errors));
    }

    private static void ReportProgress(string value, IProgress<DownloadProgress> progress)
    {
        var parts = value.Split('|');
        if (parts.Length != 5)
        {
            return;
        }

        var received = ParseNumber(parts[0]);
        var total = ParseNumber(parts[1]) ?? ParseNumber(parts[2]);
        var speed = ParseDouble(parts[3]) ?? 0;
        var etaSeconds = ParseDouble(parts[4]);
        TimeSpan? eta = etaSeconds is >= 0 and < 315360000 ? TimeSpan.FromSeconds(etaSeconds.Value) : null;

        progress.Report(new DownloadProgress(received ?? 0, total, speed, eta));
    }

    private static List<YtDlpMediaFormat> ReadFormats(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formatsElement) || formatsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var formats = new List<YtDlpMediaFormat>();
        foreach (var element in formatsElement.EnumerateArray())
        {
            if (element.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True) continue;
            var id = GetString(element, "format_id");
            var extension = GetString(element, "ext");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            formats.Add(new YtDlpMediaFormat(
                id,
                extension,
                GetInteger(element, "width"),
                GetInteger(element, "height"),
                GetString(element, "vcodec"),
                GetString(element, "acodec"),
                GetLong(element, "filesize") ?? GetLong(element, "filesize_approx"),
                GetDouble(element, "tbr") ?? GetDouble(element, "abr")));
        }

        return formats;
    }

    private static string ResolveOutputPath(string directory, string? reportedPath)
    {
        var directoryRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.IsNullOrWhiteSpace(reportedPath))
        {
            var fullPath = Path.GetFullPath(reportedPath);
            if (fullPath.StartsWith(directoryRoot, (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) && File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException("yt-dlp completed but its reported output file was not found: " + (reportedPath ?? "(no output marker)"));
    }

    private void EnsureAvailable()
    {
        if (ExecutablePath is null)
        {
            throw new FileNotFoundException(
                "yt-dlp was not found. Add it to PATH or place it in the application's tools/yt-dlp directory.");
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Uri? GetUri(JsonElement element, string propertyName) =>
        Uri.TryCreate(GetString(element, propertyName), UriKind.Absolute, out var uri) ? uri : null;

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String => ParseDouble(property.GetString()),
            _ => null,
        };
    }

    private static int? GetInteger(JsonElement element, string propertyName)
    {
        var number = GetDouble(element, propertyName);
        return number is >= 0 and <= int.MaxValue ? (int)Math.Round(number.Value) : null;
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        var number = GetDouble(element, propertyName);
        return number is >= 0 and <= long.MaxValue ? (long)Math.Round(number.Value) : null;
    }

    private static long? ParseNumber(string value)
    {
        var number = ParseDouble(value);
        return number is >= 0 and <= long.MaxValue ? (long)Math.Round(number.Value) : null;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result
            : null;

    private static string LastUsefulLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() ?? "Unknown extraction error.";

    private sealed record OutputReadResult(string? OutputPath, string Errors);

    private sealed record DownloadAttemptResult(
        int ExitCode,
        string? OutputPath,
        string StandardError,
        bool WasPaused);
}
