using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Providers;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Screenshot;

internal static class NativePipelineSmoke
{
    public static async Task RunAsync()
    {
        var root = Environment.CurrentDirectory;
        var directory = Path.Combine(root, "artifacts", "pipeline-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ffmpeg = new FFmpegService(Path.Combine(root, ".tools", "ffmpeg", "ffmpeg.exe"));
        var clip = Path.Combine(directory, "clip.mp4");
        var video = Path.Combine(directory, "video.mp4");
        var audio = Path.Combine(directory, "audio.m4a");
        var muxed = Path.Combine(directory, "muxed.mkv");
        await RunProcessAsync(ffmpeg.ExecutablePath!, ["-hide_banner", "-nostdin", "-n", "-f", "lavfi", "-i",
            "testsrc2=size=320x180:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", "4", "-c:v", "mpeg4", "-c:a", "aac", "-movflags", "+faststart", clip]);
        await ffmpeg.ExtractAudioAsync(clip, audio, "aac");
        await RunProcessAsync(ffmpeg.ExecutablePath!, ["-hide_banner", "-nostdin", "-n", "-i", clip, "-an", "-c:v", "copy", video]);
        await ffmpeg.MuxAsync(video, audio, muxed);
        var probe = Path.Combine(root, ".tools", "ffmpeg", "ffprobe.exe");
        using (var info = JsonDocument.Parse(await RunProcessAsync(probe, ["-v", "error", "-show_streams", "-of", "json", muxed])))
            Program.Require(info.RootElement.GetProperty("streams").GetArrayLength() == 2, "FFmpeg mux has video and audio");

        var payload = await File.ReadAllBytesAsync(clip);
        await using var server = new LocalMediaServer(payload);
        using var http = new HttpClient();
        var provider = new GenericHttpMediaProvider(http);
        var media = await provider.AnalyzeAsync(server.Uri);
        await using var manager = new DownloadManager(new ProviderRegistry([provider]));
        manager.Enqueue(media, media.Options[0], directory);
        await Program.UntilAsync(() => manager.Jobs[0].Status is DownloadStatus.Completed or DownloadStatus.Failed);
        Program.Require(manager.Jobs[0].Status == DownloadStatus.Completed, "Real HTTP queue transfer: " + manager.Jobs[0].ErrorMessage);
        var downloaded = await File.ReadAllBytesAsync(manager.Jobs[0].OutputPath!);
        Program.Require(SHA256.HashData(payload).SequenceEqual(SHA256.HashData(downloaded)), "HTTP output SHA-256 matches input");

        var ytDlp = new YtDlpClient(Path.Combine(root, ".tools", "yt-dlp", "yt-dlp.exe"), ffmpeg.ExecutablePath);
        var metadata = await ytDlp.AnalyzeAsync(server.Uri);
        Program.Require(!string.IsNullOrWhiteSpace(metadata.Title), "yt-dlp metadata JSON");
        var updates = 0;
        var processing = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await ytDlp.DownloadAsync(new YtDlpDownloadRequest(server.Uri, "Local test 100% & audio", directory,
            new YtDlpDownloadPlan("best", "m4a", true), Guid.NewGuid()), new AlwaysRunning(),
            new InlineProgress(value => { Interlocked.Increment(ref updates); if (value.Stage == "Processing") processing = true; }), timeout.Token);
        Program.Require(updates > 1 && processing, "yt-dlp transfer and FFmpeg progress markers");
        using (var info = JsonDocument.Parse(await RunProcessAsync(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", result.OutputPath])))
        {
            var streams = info.RootElement.GetProperty("streams");
            Program.Require(streams.GetArrayLength() == 1 && streams[0].GetProperty("codec_type").GetString() == "audio", "Audio-only output");
            Program.Require(info.RootElement.GetProperty("format").GetProperty("tags").TryGetProperty("title", out _), "Embedded title metadata");
        }
        var mp3Result = await ytDlp.DownloadAsync(new YtDlpDownloadRequest(server.Uri, "Authorized fixture MP3", directory,
            new YtDlpDownloadPlan("best", "mp3", true), Guid.NewGuid()), new AlwaysRunning(),
            new InlineProgress(_ => { }), timeout.Token);
        await VerifyMp3Async(probe, mp3Result.OutputPath, requireTitle: true);
        var direct = new GenericHttpMediaProvider(http, ffmpeg);
        var directMedia = await direct.AnalyzeAsync(server.Uri);
        await using var mp3Manager = new DownloadManager(new ProviderRegistry([direct]));
        mp3Manager.Enqueue(directMedia, directMedia.Options.Single(option => option.Id == "mp3-convert"), directory);
        await Program.UntilAsync(() => mp3Manager.Jobs[0].Status is DownloadStatus.Completed or DownloadStatus.Failed);
        Program.Require(mp3Manager.Jobs[0].Status == DownloadStatus.Completed, "Direct HTTP MP3 queue: " + mp3Manager.Jobs[0].ErrorMessage);
        await VerifyMp3Async(probe, mp3Manager.Jobs[0].OutputPath!, requireTitle: false);
        Console.WriteLine("PASS: own test media, HTTP SHA-256, queue, yt-dlp progress, FFmpeg mux/M4A/MP3, direct-media MP3 and metadata.");
        Console.WriteLine("Smoke outputs: " + directory);
    }

    private static async Task VerifyMp3Async(string probe, string path, bool requireTitle)
    {
        Program.Require(Path.GetExtension(path) == ".mp3", "MP3 extension");
        using var info = JsonDocument.Parse(await RunProcessAsync(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", path]));
        var streams = info.RootElement.GetProperty("streams");
        Program.Require(streams.GetArrayLength() == 1 && streams[0].GetProperty("codec_type").GetString() == "audio" &&
            streams[0].GetProperty("codec_name").GetString() == "mp3", "Real MP3 audio codec, not renamed media");
        var format = info.RootElement.GetProperty("format");
        Program.Require(format.GetProperty("format_name").GetString() == "mp3", "MP3 container");
        if (requireTitle) Program.Require(format.GetProperty("tags").TryGetProperty("title", out _), "MP3 ID3 title metadata");
    }

    private static async Task<string> RunProcessAsync(string executable, string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var text = await output;
        var errors = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(errors);
        return text;
    }

    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
    private sealed class AlwaysRunning : IDownloadControl
    {
        public bool IsPaused => false;
        public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class LocalMediaServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly List<Task> _connections = [];
        private readonly Task _accept;
        private readonly byte[] _payload;
        public Uri Uri { get; }

        public LocalMediaServer(byte[] payload)
        {
            _payload = payload;
            _listener.Start();
            Uri = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/clip.mp4");
            _accept = AcceptAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            await _accept;
            await Task.WhenAll(_connections);
            _shutdown.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                    _connections.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_shutdown.Token)));
            }
            catch (OperationCanceledException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var request = await reader.ReadLineAsync(_shutdown.Token) ?? string.Empty;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_shutdown.Token))) { }
                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nETag: \"fixture-1\"\r\nContent-Length: " +
                        _payload.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _shutdown.Token);
                    if (!request.StartsWith("HEAD ", StringComparison.Ordinal)) await stream.WriteAsync(_payload, _shutdown.Token);
                }
                catch (IOException) { /* A HEAD/range probe may close its socket early. */ }
                catch (OperationCanceledException) { }
            }
        }
    }
}
