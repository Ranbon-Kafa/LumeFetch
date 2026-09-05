using LumeFetch.Core.Downloads;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Settings;
using LumeFetch.Infrastructure.Files;
using LumeFetch.Infrastructure.Providers;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Core.Tests;

public sealed class SettingsAndRoutingTests
{
    [Theory]
    [InlineData("https://www.instagram.com/reel/example", "instagram")]
    [InlineData("https://vm.tiktok.com/example", "tiktok")]
    [InlineData("https://x.com/creator/status/1", "twitter")]
    [InlineData("https://old.reddit.com/r/test/comments/1/test", "reddit")]
    [InlineData("https://v.redd.it/example", "reddit")]
    public void SocialProvidersRouteExactHosts(string url, string expected)
    {
        var client = new NoNetworkClient();
        var registry = new ProviderRegistry([
            new InstagramProvider(client), new TikTokProvider(client), new TwitterProvider(client), new RedditProvider(client)]);
        Assert.Equal(expected, registry.Resolve(new Uri(url)).Id);
        Assert.All(registry.Providers, provider => Assert.False(provider.CanHandle(new Uri("https://x.com.attacker.org/video"))));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("LPT1", "_LPT1")]
    [InlineData("../hello:*?", "..hello")]
    public void OutputNamesArePortable(string input, string expected) => Assert.Equal(expected, SafeFileName.Create(input));

    [Fact]
    public async Task SettingsRoundTripAndCorruptFileIsPreserved()
    {
        var directory = Directory.CreateTempSubdirectory("lumefetch-settings-").FullName;
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(path);
            var settings = new AppSettings { DownloadDirectory = directory, MaxParallelDownloads = 4, SmartPaste = false };
            await store.SaveAsync(settings);
            Assert.Equal(settings, store.Load());
            await File.WriteAllTextAsync(path, "{broken");
            Assert.Equal(2, store.Load().MaxParallelDownloads);
            Assert.NotNull(store.LoadWarning);
            Assert.Equal("{broken", await File.ReadAllTextAsync(path));
            await File.WriteAllTextAsync(path, "{\"SchemaVersion\":99}");
            Assert.Equal(1, store.Load().SchemaVersion);
            Assert.NotNull(store.LoadWarning);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(settings with { MaxParallelDownloads = 99 }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class NoNetworkClient : IYtDlpClient
    {
        public bool IsAvailable => true;
        public Task<YtDlpMetadata> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DownloadResult> DownloadAsync(YtDlpDownloadRequest request, IDownloadControl control,
            IProgress<DownloadProgress> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
