using LumeFetch.Core.Downloads;
using LumeFetch.Core.Processing;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Tools;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Core.Tests;

public sealed class ProcessingPreflightTests
{
    [Theory]
    [InlineData(true, false, false, "best")]
    [InlineData(false, true, false, "best")]
    [InlineData(false, false, true, "best")]
    [InlineData(false, false, false, "137+140")]
    public async Task ProcessingFailureStopsBeforeStartingDownloaderOrCreatingFiles(bool metadata, bool thumbnail, bool audio, string format)
    {
        var destination = Path.Combine(Path.GetTempPath(), "lumefetch-preflight-" + Guid.NewGuid().ToString("N"));
        var checkedTools = false;
        var client = new YtDlpClient(command: new ToolCommand("not-started-downloader"), ffmpegPath: "bundled-ffmpeg",
            embedMetadata: metadata, embedThumbnail: thumbnail, javaScriptRuntime: "quickjs:unused",
            processingCheck: _ => { checkedTools = true; throw new InvalidOperationException("FFprobe linker diagnostic"); });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(
            new YtDlpDownloadRequest(new Uri("https://example.org/fixture.mp4"), "Fixture", destination,
                new YtDlpDownloadPlan(format, audio ? "mp3" : "mp4", audio), Guid.NewGuid()),
            new RunningControl(), new Progress<DownloadProgress>()));
        Assert.True(checkedTools);
        Assert.Equal("FFprobe linker diagnostic", error.Message);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task CanceledPreflightDoesNotStartDownloadOrCreateFiles()
    {
        var destination = Path.Combine(Path.GetTempPath(), "lumefetch-preflight-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        var client = new YtDlpClient(command: new ToolCommand("not-started-downloader"), ffmpegPath: "bundled-ffmpeg",
            javaScriptRuntime: "quickjs:unused", processingCheck: token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(
            new YtDlpDownloadRequest(new Uri("https://example.org/fixture.mp4"), "Fixture", destination,
                new YtDlpDownloadPlan("best", "mp3", true), Guid.NewGuid()),
            new RunningControl(), new Progress<DownloadProgress>(), cancellation.Token));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task VersionCheckRequiresBothBundledTools()
    {
        var service = new FFmpegService(command: VersionCommand("ffmpeg"), probeCommand: VersionCommand("ffprobe"));
        var version = await service.GetVersionAsync();
        Assert.Contains("ffmpeg version fixture", version);
        Assert.Contains("ffprobe version fixture", version);
        await service.EnsureReadyAsync();
    }

    [Fact]
    public async Task ProbeLinkerFailureKeepsToolNameExitCodeAndMultilineCause()
    {
        var service = new FFmpegService(command: VersionCommand("ffmpeg"), probeCommand: FailureCommand(127, true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetVersionAsync());
        Assert.Contains("FFprobe exited with code 127", error.Message);
        Assert.Contains("CANNOT LINK EXECUTABLE", error.Message);
        Assert.Contains("libfixture.so not found", error.Message);
    }

    [Fact]
    public async Task SilentNativeFailureStillKeepsExitCode()
    {
        var service = new FFmpegService(command: FailureCommand(132, false));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetVersionAsync());
        Assert.Contains("FFmpeg exited with code 132", error.Message);
        Assert.Contains("No error output", error.Message);
    }

    [Fact]
    public async Task MissingProcessNamesTheToolThatCouldNotStart()
    {
        var service = new FFmpegService(command: new ToolCommand(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetVersionAsync());
        Assert.StartsWith("FFmpeg could not start:", error.Message);
    }

    // Fixed, test-owned child commands exercise actual redirected process streams; no user input or network.
    private static ToolCommand VersionCommand(string name) => ShellCommand(
        $"[Console]::Out.WriteLine('{name} version fixture'); exit 0",
        $"printf '{name} version fixture\\n'; exit 0");

    private static ToolCommand FailureCommand(int code, bool message) => ShellCommand(
        (message ? "[Console]::Error.WriteLine('CANNOT LINK EXECUTABLE'); [Console]::Error.WriteLine('libfixture.so not found'); " : "") + $"exit {code}",
        (message ? "printf 'CANNOT LINK EXECUTABLE\\nlibfixture.so not found\\n' >&2; " : "") + $"exit {code}");

    private static ToolCommand ShellCommand(string windows, string unix) => OperatingSystem.IsWindows()
        ? new ToolCommand(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "& { " + windows + " }"])
        : new ToolCommand("/bin/sh", ["-c", unix]);

    private sealed class RunningControl : IDownloadControl
    {
        public bool IsPaused => false;
        public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
