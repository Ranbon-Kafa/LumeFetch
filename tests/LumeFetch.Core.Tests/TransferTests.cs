using System.Net;
using System.Net.Http.Headers;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Media;
using LumeFetch.Infrastructure.Files;
using LumeFetch.Infrastructure.Providers;

namespace LumeFetch.Core.Tests;

public sealed class TransferTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("lumefetch-tests-").FullName;
    private static readonly byte[] Payload = Enumerable.Range(0, 400000).Select(index => (byte)(index % 251)).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedTransferResumesOrSafelyRestartsWhenEntityChanges(bool changeEntity)
    {
        var resumed = false;
        var generation = 1;
        using var client = new HttpClient(new Handler(request =>
        {
            var offset = request.Headers.Range?.Ranges.Single().From ?? 0;
            if (offset > 0) { resumed = true; Assert.Equal("\"v1\"", request.Headers.IfRange?.EntityTag?.Tag); }
            var rangeAccepted = offset > 0 && generation == 1;
            if (!rangeAccepted) offset = 0;
            var response = Response(Payload[(int)offset..], rangeAccepted ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
            response.Headers.ETag = new EntityTagHeaderValue(generation == 1 ? "\"v1\"" : "\"v2\"");
            if (rangeAccepted) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, Payload.Length - 1, Payload.Length);
            return response;
        }));
        var provider = new GenericHttpMediaProvider(client);
        var context = Context();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DownloadAsync(context,
            new InlineProgress(value => { if (value.BytesReceived > 0) cancellation.Cancel(); }), cancellation.Token));
        if (changeEntity) generation = 2;
        var result = await provider.DownloadAsync(context, new InlineProgress(_ => { }));
        Assert.True(resumed);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(result.OutputPath));
    }

    [Fact]
    public async Task TwoJobsWithSameTitleNeverOverwriteEachOther()
    {
        using var client = new HttpClient(new Handler(_ => Response(Payload)));
        var provider = new GenericHttpMediaProvider(client);
        var results = await Task.WhenAll(
            provider.DownloadAsync(Context(), new InlineProgress(_ => { })),
            provider.DownloadAsync(Context(), new InlineProgress(_ => { })));
        Assert.NotEqual(results[0].OutputPath, results[1].OutputPath);
        foreach (var result in results) Assert.Equal(Payload, await File.ReadAllBytesAsync(result.OutputPath));
    }

    [Fact]
    public async Task MalformedResumeRangeIsRejectedWithoutCommittingOutput()
    {
        var context = Context();
        var work = DownloadFiles.WorkingDirectory(_directory, context.JobId);
        await File.WriteAllBytesAsync(Path.Combine(work, "download.part"), Payload[..100]);
        await File.WriteAllTextAsync(Path.Combine(work, "etag.txt"), "\"v1\"");
        using var client = new HttpClient(new Handler(_ =>
        {
            var response = Response(Payload, HttpStatusCode.PartialContent);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, Payload.Length - 1, Payload.Length);
            return response;
        }));
        await Assert.ThrowsAsync<IOException>(() => new GenericHttpMediaProvider(client).DownloadAsync(context, new InlineProgress(_ => { })));
        Assert.Empty(Directory.GetFiles(_directory, "*.mp4"));
    }

    [Fact]
    public async Task HtmlDisguisedAsMp4IsRejected()
    {
        using var client = new HttpClient(new Handler(_ =>
        {
            var response = Response([]);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        }));
        await Assert.ThrowsAsync<MediaAnalysisException>(() => new GenericHttpMediaProvider(client).AnalyzeAsync(new Uri("https://example.org/login.mp4")));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private DownloadContext Context()
    {
        var option = new DownloadOption("original", "Original", "mp4", MediaKind.Video);
        var media = new MediaInfo("generic-http", "Direct media", new Uri("https://example.org/test.mp4"), "Same title", null, null, [option]);
        return new DownloadContext(Guid.NewGuid(), media, option, _directory, new AlwaysRunning());
    }

    private static HttpResponseMessage Response(byte[] content, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class InlineProgress(Action<DownloadProgress> callback) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => callback(value);
    }
    private sealed class AlwaysRunning : IDownloadControl
    {
        public bool IsPaused => false;
        public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
