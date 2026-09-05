using System.Net;
using LumeFetch.Infrastructure.Spotify;

namespace LumeFetch.Core.Tests;

public sealed class SpotifyResolverTests
{
    private const string Id = "0123456789abcdefghijkl";
    private const string TrackJson = """
        {"type":"track","id":"0123456789abcdefghijkl","name":"Northern Light",
         "artists":[{"name":"Aurora"}],"album":{"name":"Sample Album"},
         "duration_ms":180000,"external_ids":{"isrc":"XXTEST000001"}}
        """;

    [Theory]
    [InlineData("https://open.spotify.com/track/0123456789abcdefghijkl?si=test", true)]
    [InlineData("https://open.spotify.com/intl-tr/album/0123456789abcdefghijkl", true)]
    [InlineData("https://open.spotify.com/playlist/0123456789abcdefghijkl", true)]
    [InlineData("https://open.spotify.com.attacker.org/track/0123456789abcdefghijkl", false)]
    [InlineData("https://open.spotify.com:8080/track/0123456789abcdefghijkl", false)]
    [InlineData("https://open.spotify.com/episode/0123456789abcdefghijkl", false)]
    [InlineData("https://open.spotify.com/track/short", false)]
    public void RoutesOnlySupportedCatalogLinks(string url, bool expected) =>
        Assert.Equal(expected, SpotifyResolver.TryParseUrl(new Uri(url), out _, out _));

    [Fact]
    public async Task SingleTrackReturnsMetadataOnly()
    {
        using var handler = new FakeHandler(_ => TrackJson);
        using var http = new HttpClient(handler);
        var result = await new SpotifyResolver(http, new FakeTokens()).ResolveAsync(new Uri("https://open.spotify.com/track/" + Id));
        var track = Assert.Single(result.Tracks);
        Assert.Equal("Northern Light", track.Track.Title);
        Assert.Equal("Aurora", Assert.Single(track.Track.Artists));
        Assert.Equal(TimeSpan.FromSeconds(180), track.Track.Duration);
        Assert.Equal("XXTEST000001", track.Track.Isrc);
        Assert.Equal("Sample Album", track.Track.Album);
        Assert.Null(track.BestMatch);
        Assert.Empty(track.Alternatives);
    }

    [Fact]
    public async Task PlaylistPaginatesNewAndLegacyWrappersAndKeepsUnavailableRows()
    {
        using var handler = new FakeHandler(uri => uri.AbsolutePath.EndsWith("/items", StringComparison.Ordinal)
            ? uri.Query.Contains("offset=", StringComparison.Ordinal)
                ? """{"items":[{"item":null},{"item":{"type":"episode"}},{"item":{"is_local":true}}],"next":null}"""
                : """{"items":[{"item":TRACK},{"track":TRACK}],"next":"https://api.spotify.com/v1/playlists/ID/items?offset=2"}"""
                    .Replace("TRACK", TrackJson, StringComparison.Ordinal).Replace("ID", Id, StringComparison.Ordinal)
            : """{"name":"My playlist"}""");
        using var http = new HttpClient(handler);
        var result = await new SpotifyResolver(http, new FakeTokens()).ResolveAsync(new Uri("https://open.spotify.com/playlist/" + Id));
        Assert.Equal(5, result.Tracks.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(result.Tracks.Take(2), item => Assert.Null(item.UnavailableReason));
        Assert.All(result.Tracks.Skip(2), item => Assert.NotNull(item.UnavailableReason));
    }

    [Fact]
    public async Task AlbumCarriesAlbumNameAndStopsAtLimit()
    {
        using var handler = new FakeHandler(_ => """
            {"name":"Album","tracks":{"items":[{"id":"0123456789abcdefghijkl","name":"One","artists":[{"name":"A"}]},
            {"name":"Two","artists":[]}],"next":"https://api.spotify.com/v1/albums/ID/tracks?offset=2"}}
            """);
        using var http = new HttpClient(handler);
        var result = await new SpotifyResolver(http, new FakeTokens(), 1).ResolveAsync(new Uri("https://open.spotify.com/album/" + Id));
        Assert.True(result.IsTruncated);
        Assert.Equal("Album", Assert.Single(result.Tracks).Track.Album);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UntrustedPaginationNeverReceivesBearer()
    {
        using var handler = new FakeHandler(uri => uri.AbsolutePath.EndsWith("/items", StringComparison.Ordinal)
            ? """{"items":[],"next":"https://attacker.org/v1/stolen"}""" : """{"name":"Playlist"}""");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new SpotifyResolver(http, new FakeTokens())
            .ResolveAsync(new Uri("https://open.spotify.com/playlist/" + Id)));
        Assert.All(handler.Requests, uri => Assert.Equal("api.spotify.com", uri.Host));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ApiRestrictionsAreSurfacedWithoutScrapingFallback(HttpStatusCode status)
    {
        using var handler = new FakeHandler(_ => "{}", status);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SpotifyResolver(http, new FakeTokens())
            .ResolveAsync(new Uri("https://open.spotify.com/track/" + Id)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void OAuthRequiresExactRedirectAndState()
    {
        Assert.Equal("sample-code", SpotifySession.ValidateCallback(new Uri(SpotifySession.RedirectUri + "?state=expected&code=sample-code"), "expected"));
        Assert.Throws<InvalidDataException>(() => SpotifySession.ValidateCallback(new Uri(SpotifySession.RedirectUri + "?state=wrong&code=x"), "expected"));
        Assert.Throws<InvalidDataException>(() => SpotifySession.ValidateCallback(new Uri(SpotifySession.RedirectUri + "?state=expected&state=expected&code=x"), "expected"));
        Assert.Throws<InvalidDataException>(() => SpotifySession.ValidateCallback(new Uri("https://attacker.org/callback?state=expected&code=x"), "expected"));
        Assert.Throws<InvalidOperationException>(() => SpotifySession.ValidateCallback(new Uri(SpotifySession.RedirectUri + "?state=expected&error=access_denied"), "expected"));
    }

    private sealed class FakeTokens : ISpotifyTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("fixture-not-a-real-token");
    }

    private sealed class FakeHandler(Func<Uri, string> response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fixture-not-a-real-token", request.Headers.Authorization?.Parameter);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response(request.RequestUri!)) });
        }
    }
}
