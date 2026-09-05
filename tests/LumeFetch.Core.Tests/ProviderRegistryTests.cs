using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;

namespace LumeFetch.Core.Tests;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void ResolveUsesHighestPriorityMatchingProvider()
    {
        var fallback = new StubProvider("fallback", priority: -100, canHandle: true);
        var specific = new StubProvider("specific", priority: 100, canHandle: true);
        var registry = new ProviderRegistry([fallback, specific]);

        var result = registry.Resolve(new Uri("https://example.com/video"));

        Assert.Same(specific, result);
    }

    [Fact]
    public void ConstructorRejectsDuplicateProviderIds()
    {
        var providers = new IMediaProvider[]
        {
            new StubProvider("duplicate", 1, true),
            new StubProvider("duplicate", 2, true),
        };

        Assert.Throws<ArgumentException>(() => new ProviderRegistry(providers));
    }

    private sealed class StubProvider(string id, int priority, bool canHandle) : IMediaProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public int Priority => priority;
        public bool CanHandle(Uri uri) => canHandle;

        public Task<MediaInfo> AnalyzeAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DownloadResult> DownloadAsync(
            DownloadContext context,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
