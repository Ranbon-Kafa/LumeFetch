using System.Net;
using System.Net.Http.Headers;
using LumeFetch.Core.Media;
using LumeFetch.Infrastructure.Providers;

namespace LumeFetch.Core.Tests;

public sealed class GenericHttpMediaProviderTests
{
    [Fact]
    public async Task AnalyzeAsyncReturnsOriginalMediaOption()
    {
        using var client = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            response.Content.Headers.ContentLength = 73_400_320;
            return response;
        }));
        var provider = new GenericHttpMediaProvider(client);

        var result = await provider.AnalyzeAsync(new Uri("https://cdn.example.com/sample.mp4"));

        Assert.Equal("sample", result.Title);
        var option = Assert.Single(result.Options);
        Assert.Equal(MediaKind.Video, option.Kind);
        Assert.Equal("mp4", option.Container);
        Assert.Equal(73_400_320, option.EstimatedBytes);
    }

    [Fact]
    public async Task AnalyzeAsyncRejectsHtmlPages()
    {
        using var client = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>"),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        }));
        var provider = new GenericHttpMediaProvider(client);

        await Assert.ThrowsAsync<MediaAnalysisException>(
            () => provider.AnalyzeAsync(new Uri("https://example.com/watch/123")));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
