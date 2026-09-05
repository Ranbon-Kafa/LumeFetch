using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Services;

namespace LumeFetch.Core.Tests;

public sealed class MediaAnalysisServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///private/video.mp4")]
    public async Task AnalyzeAsyncRejectsInvalidOrUnsafeAddresses(string input)
    {
        var service = new MediaAnalysisService(new ProviderRegistry([]));

        await Assert.ThrowsAsync<MediaAnalysisException>(() => service.AnalyzeAsync(input));
    }
}
