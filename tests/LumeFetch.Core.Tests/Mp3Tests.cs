using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Processing;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Core.Tests;

public sealed class Mp3Tests
{
    [Theory]
    [InlineData("m4a", "none", "aac", true)]
    [InlineData("webm", "none", "opus", true)]
    [InlineData("mp4", "h264", "aac", true)]
    [InlineData("mp3", "none", "mp3", false)]
    public async Task Mp3OptionHasRealConversionPlanOrKeepsNativeMp3(string extension, string video, string audio, bool extract)
    {
        var format = new YtDlpMediaFormat("source", extension, null, null, video, audio, 12345, 128);
        var provider = new YouTubeProvider(new MetadataClient([format]));
        var media = await provider.AnalyzeAsync(new Uri("https://www.youtube.com/watch?v=abcdefghijk"));
        var option = Assert.Single(media.Options, item => item.Container == "mp3");
        Assert.Equal(MediaKind.Audio, option.Kind);
        var plan = JsonSerializer.Deserialize<YtDlpDownloadPlan>(option.ProviderData!)!;
        Assert.Equal("source", plan.FormatSelector);
        Assert.Equal(extract, plan.ExtractAudio);
        Assert.Equal("mp3", plan.OutputContainer);
        if (extract) Assert.Null(option.EstimatedBytes);
    }

    [Fact]
    public async Task SilentVideoDoesNotOfferMp3AndBestAudioStreamIsUsed()
    {
        var silent = new YtDlpMediaFormat("silent", "mp4", 1920, 1080, "h264", "none", 10000, 1000);
        var provider = new YouTubeProvider(new MetadataClient([silent]));
        await Assert.ThrowsAsync<MediaAnalysisException>(() => provider.AnalyzeAsync(new Uri("https://youtu.be/abcdefghijk")));
        provider = new YouTubeProvider(new MetadataClient([
            silent,
            new("low", "m4a", null, null, "none", "aac", 1000, 96),
            new("high", "webm", null, null, "none", "opus", 2000, 160)]));
        var media = await provider.AnalyzeAsync(new Uri("https://youtu.be/abcdefghijk"));
        var mp3 = Assert.Single(media.Options, item => item.Container == "mp3");
        Assert.Equal("high", JsonSerializer.Deserialize<YtDlpDownloadPlan>(mp3.ProviderData!)!.FormatSelector);
    }

    [Theory]
    [InlineData("video/mp4", "sample.mp4", true, true)]
    [InlineData("audio/mp4", "sample.m4a", true, true)]
    [InlineData("audio/mpeg", "sample.mp3", true, false)]
    [InlineData("image/png", "sample.png", true, false)]
    [InlineData("video/mp4", "sample.mp4", false, false)]
    public async Task DirectMediaOffersConversionOnlyWithFfmpeg(string type, string file, bool available, bool conversion)
    {
        using var http = new HttpClient(new MediaHandler(type));
        var media = await new GenericHttpMediaProvider(http, new FakeFfmpeg(available))
            .AnalyzeAsync(new Uri("https://example.org/" + file));
        Assert.Equal(conversion, media.Options.Any(option => option.Id == "mp3-convert"));
        if (conversion) Assert.Null(media.Options.Single(option => option.Id == "mp3-convert").EstimatedBytes);
    }

    [Fact]
    public async Task FailedDirectConversionDoesNotCommitFakeMp3AndRetryWorks()
    {
        var directory = Directory.CreateTempSubdirectory("lumefetch-mp3-").FullName;
        try
        {
            using var http = new HttpClient(new MediaHandler("video/mp4"));
            var ffmpeg = new FakeFfmpeg(true) { Fail = true };
            var provider = new GenericHttpMediaProvider(http, ffmpeg);
            var media = await provider.AnalyzeAsync(new Uri("https://example.org/sample.mp4"));
            var option = media.Options.Single(item => item.Id == "mp3-convert");
            var context = new DownloadContext(Guid.NewGuid(), media, option, directory, new AlwaysRunning());
            var stages = new List<string>();
            var progress = new InlineProgress(value => stages.Add(value.Stage));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DownloadAsync(context, progress));
            Assert.Empty(Directory.GetFiles(directory, "*.mp3"));
            ffmpeg.Fail = false;
            var result = await provider.DownloadAsync(context, progress);
            Assert.Equal(".mp3", Path.GetExtension(result.OutputPath));
            Assert.Contains("Processing", stages);
            Assert.Equal("libmp3lame", ffmpeg.Codec);
            Assert.Equal(new FileInfo(result.OutputPath).Length, result.BytesWritten);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class MetadataClient(IReadOnlyList<YtDlpMediaFormat> formats) : IYtDlpClient
    {
        public bool IsAvailable => true;
        public Task<YtDlpMetadata> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(new YtDlpMetadata("fixture", "Fixture", null, null, null, formats));
        public Task<DownloadResult> DownloadAsync(YtDlpDownloadRequest request, IDownloadControl control,
            IProgress<DownloadProgress> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MediaHandler(string type) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture source bytes") };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
            return Task.FromResult(response);
        }
    }
    private sealed class FakeFfmpeg(bool available) : IFFmpegService
    {
        public string? ExecutablePath => null;
        public bool IsAvailable => available;
        public bool Fail { get; set; }
        public string? Codec { get; private set; }
        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("fixture");
        public Task MuxAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task ExtractAudioAsync(string inputPath, string outputPath, string codec, CancellationToken cancellationToken = default)
        {
            Codec = codec;
            if (Fail) throw new InvalidOperationException("Fixture conversion failure");
            await File.WriteAllTextAsync(outputPath, "fixture MP3 result (not real audio)", cancellationToken);
        }
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
}
