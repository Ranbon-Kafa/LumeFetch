using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Core.Tests;

public sealed class YouTubeProviderTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123")]
    [InlineData("https://music.youtube.com/watch?v=abc123")]
    [InlineData("https://youtu.be/abc123")]
    public void CanHandleAcceptsKnownYouTubeHosts(string value)
    {
        var provider = new YouTubeProvider(new StubClient(CreateMetadata()));

        Assert.True(provider.CanHandle(new Uri(value)));
    }

    [Theory]
    [InlineData("https://youtube.com.example.org/watch?v=abc123")]
    [InlineData("https://notyoutube.com/watch?v=abc123")]
    public void CanHandleRejectsLookalikeHosts(string value)
    {
        var provider = new YouTubeProvider(new StubClient(CreateMetadata()));

        Assert.False(provider.CanHandle(new Uri(value)));
    }

    [Fact]
    public async Task AnalyzeAsyncMapsVideoAndAudioFormats()
    {
        var provider = new YouTubeProvider(new StubClient(CreateMetadata()));

        var result = await provider.AnalyzeAsync(new Uri("https://youtu.be/abc123"));

        Assert.Equal("Example video", result.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), result.Duration);
        Assert.Collection(
            result.Options,
            option =>
            {
                Assert.Equal(1080, option.Height);
                Assert.Equal("mp4", option.Container);
                Assert.NotNull(option.ProviderData);
            },
            option =>
            {
                Assert.Equal(720, option.Height);
                Assert.Equal("mp4", option.Container);
            },
            option =>
            {
                Assert.Equal(MediaKind.Audio, option.Kind);
                Assert.Equal("m4a", option.Container);
            },
            option =>
            {
                Assert.Equal(MediaKind.Audio, option.Kind);
                Assert.Equal("mp3", option.Container);
                Assert.Null(option.EstimatedBytes);
            });
    }

    private static YtDlpMetadata CreateMetadata() => new(
        "abc123",
        "Example video",
        "Example creator",
        TimeSpan.FromSeconds(125),
        new Uri("https://i.example.org/thumb.jpg"),
        [
            new YtDlpMediaFormat("137", "mp4", 1920, 1080, "avc1", "none", 200_000_000, 4_500),
            new YtDlpMediaFormat("399", "mp4", 1920, 1080, "av01", "none", 170_000_000, 3_900),
            new YtDlpMediaFormat("22", "mp4", 1280, 720, "avc1", "mp4a", 95_000_000, 2_200),
            new YtDlpMediaFormat("140", "m4a", null, null, "none", "mp4a", 4_200_000, 128),
        ]);

    private sealed class StubClient(YtDlpMetadata metadata) : IYtDlpClient
    {
        public bool IsAvailable => true;

        public Task<YtDlpMetadata> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(metadata);

        public Task<DownloadResult> DownloadAsync(
            YtDlpDownloadRequest request,
            IDownloadControl control,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
