#if DEBUG
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
using LumeFetch.Infrastructure.Tools;
using LumeFetch.Infrastructure.YtDlp;
using DownloadManager = LumeFetch.Core.Downloads.DownloadManager;
using DownloadStatus = LumeFetch.Core.Downloads.DownloadStatus;

namespace LumeFetch.Android.Runtime;

/// <summary>Debug-only, opt-in native acceptance with generated media; never included in Release.</summary>
internal static class AndroidRuntimeSmoke
{
    public static async Task RunAsync(AndroidTools tools, string root)
    {
        var directory = Path.Combine(root, "runtime-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var report = Path.Combine(root, "runtime-smoke-result.txt");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        try
        {
            var pythonProbe = Path.Combine(directory, "python-probe.py");
            await File.WriteAllTextAsync(pythonProbe, """
                import json, os, sqlite3, ssl, subprocess, sys
                sys.path.insert(0, sys.argv[1])
                import mutagen, certifi
                import yt_dlp_ejs
                context = ssl.create_default_context()
                assert context.verify_mode == ssl.CERT_REQUIRED and context.check_hostname
                assert context.cert_store_stats()["x509_ca"] > 0
                for directory in os.environ["LD_LIBRARY_PATH"].split(":"):
                    for name in ("libcrypto.so", "libssl.so", "libsqlite.so", "libsqlite3.so", "libsqlite3.so.0"):
                        assert not os.path.exists(os.path.join(directory, name)), "System-library alias: " + name
                with sqlite3.connect(":memory:") as database:
                    assert database.execute("select 6 * 7").fetchone()[0] == 42
                assert os.getpgrp() == os.getpid()
                print(json.dumps({"python": sys.version.split()[0], "mutagen": mutagen.version_string,
                                  "certifi": certifi.__version__, "openssl": ssl.OPENSSL_VERSION,
                                  "sqlite": sqlite3.sqlite_version, "private_library_names": True}))
                """, token);
            var pythonVersion = await RunAsync(tools.CreateYtDlpCommand(pythonProbe), [tools.ExtractorPath], token);
            using var pythonDetails = JsonDocument.Parse(pythonVersion);
            var ffmpeg = tools.CreateFFmpegCommand();
            var probe = tools.CreateFFmpegCommand(tools.FFprobePath);
            var clip = Path.Combine(directory, "fixture.mp4");
            await RunAsync(ffmpeg, ["-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24", "-f", "lavfi", "-i",
                "sine=frequency=440:sample_rate=44100", "-t", "3", "-c:v", "mpeg4", "-c:a", "aac", "-movflags", "+faststart", clip], token);
            var processing = new FFmpegService(command: ffmpeg);
            var opus = Path.Combine(directory, "fixture.opus");
            await processing.ExtractAudioAsync(clip, opus, "libopus", token);
            await RequireCodecAsync(probe, opus, "opus", token);
            var payload = await File.ReadAllBytesAsync(clip, token);
            await using var server = new FixtureServer(payload);
            using var http = new HttpClient();
            var provider = new GenericHttpMediaProvider(http, processing);
            var media = await provider.AnalyzeAsync(server.Uri, token);
            await using var manager = new DownloadManager(new ProviderRegistry([provider]));
            manager.Enqueue(media, media.Options.First(option => option.Id != "mp3-convert"), directory);
            await UntilAsync(() => manager.Jobs[0].Status is DownloadStatus.Completed or DownloadStatus.Failed, token);
            if (manager.Jobs[0].Status != DownloadStatus.Completed) throw new IOException(manager.Jobs[0].ErrorMessage);
            var downloaded = await File.ReadAllBytesAsync(manager.Jobs[0].OutputPath!, token);
            if (!SHA256.HashData(payload).SequenceEqual(SHA256.HashData(downloaded)))
                throw new IOException("HTTP output hash differs.");
            manager.Enqueue(media, media.Options.Single(option => option.Id == "mp3-convert"), directory);
            await UntilAsync(() => manager.Jobs[0].Status is DownloadStatus.Completed or DownloadStatus.Failed, token);
            if (manager.Jobs[0].Status != DownloadStatus.Completed) throw new IOException(manager.Jobs[0].ErrorMessage);
            await RequireCodecAsync(probe, manager.Jobs[0].OutputPath!, "mp3", token);
            var extractor = new YtDlpClient(ffmpegPath: tools.FFmpegPath, command: tools.CreateYtDlpCommand(), javaScriptRuntime: tools.JavaScriptRuntime);
            var metadata = await extractor.AnalyzeAsync(server.Uri, token);
            if (string.IsNullOrWhiteSpace(metadata.Title)) throw new IOException("Missing native extractor title.");
            var processingSeen = false;
            var result = await extractor.DownloadAsync(new YtDlpDownloadRequest(server.Uri, "Authorized generated fixture", directory,
                new YtDlpDownloadPlan("best", "mp3", true), Guid.NewGuid()), new AlwaysRunning(),
                new InlineProgress(value => { if (value.Stage == "Processing") processingSeen = true; }), token);
            if (!processingSeen) throw new IOException("Native post-processing progress missing.");
            await RequireCodecAsync(probe, result.OutputPath, "mp3", token);
            // QuickJS is exercised directly; this does not claim successful live YouTube challenge handling.
            var js = new ToolCommand(tools.JavaScriptRuntime["quickjs:".Length..], environment: ffmpeg.CreateStartInfo([]).Environment
                .Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!));
            if (!(await RunAsync(js, ["-e", "console.log(6 * 7)"], token)).Trim().Equals("42", StringComparison.Ordinal))
                throw new IOException("Bundled JavaScript runtime failed.");
            await File.WriteAllTextAsync(report, "PASS: Android FFmpeg, FFprobe, Opus, generic HTTP SHA-256, queue MP3, Python/yt-dlp analysis and MP3 post-processing, QuickJS, TLS CA store, Mutagen.\n" + pythonVersion + "\n" + directory, token);
            global::Android.Util.Log.Info("LumeFetchSmoke", "PASS");
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(report, "FAIL: " + exception, CancellationToken.None);
            global::Android.Util.Log.Error("LumeFetchSmoke", "FAIL: " + exception);
        }
    }

    private static async Task RequireCodecAsync(ToolCommand probe, string path, string codec, CancellationToken token)
    {
        using var document = JsonDocument.Parse(await RunAsync(probe, ["-v", "error", "-show_streams", "-of", "json", path], token));
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1 || streams[0].GetProperty("codec_name").GetString() != codec)
            throw new IOException("Unexpected output codec: " + codec);
    }

    private static async Task<string> RunAsync(ToolCommand command, string[] arguments, CancellationToken token)
    {
        using var process = Process.Start(command.CreateStartInfo(arguments)) ?? throw new IOException("Native process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) command.Terminate(process); await process.WaitForExitAsync(CancellationToken.None); throw; }
        var result = await output;
        var errors = await error;
        if (process.ExitCode != 0) throw new IOException(errors);
        return result;
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(50, token);
    }

    private sealed class AlwaysRunning : IDownloadControl
    {
        public bool IsPaused => false;
        public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class FixtureServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _connections = [];
        private readonly byte[] _payload;
        private readonly Task _accept;
        public Uri Uri { get; }
        public FixtureServer(byte[] payload)
        {
            _payload = payload;
            _listener.Start();
            Uri = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/fixture.mp4");
            _accept = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested) _connections.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token)));
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
                    var request = await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: " +
                        _payload.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token);
                    if (!request.StartsWith("HEAD ", StringComparison.Ordinal)) await stream.WriteAsync(_payload, _stop.Token);
                }
                catch (IOException) { }
                catch (OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _accept;
            await Task.WhenAll(_connections);
            _stop.Dispose();
        }
    }
}
#endif
