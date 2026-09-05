using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LumeFetch.Core.Resolvers;

namespace LumeFetch.Infrastructure.Spotify;

public sealed class SpotifyResolver(HttpClient httpClient, ISpotifyTokenSource tokens, int limit = 200) : IMediaResolver
{
    public string Id => "spotify";
    public string DisplayName => "Spotify";
    public bool CanResolve(Uri uri) => TryParseUrl(uri, out _, out _);

    public async Task<ResolvedCatalog> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        if (!TryParseUrl(uri, out var kind, out var id)) throw new InvalidOperationException("Use a Spotify track, album or playlist URL.");
        using var rootDocument = await GetAsync(new Uri("https://api.spotify.com/v1/" + kind + "s/" + id), cancellationToken).ConfigureAwait(false);
        var root = rootDocument.RootElement;
        var title = Text(root, "name") ?? "Spotify";
        var tracks = new List<ResolvedTrack>();
        var truncated = false;
        if (kind == "track") tracks.Add(ReadTrack(root, null));
        else
        {
            JsonDocument? pageDocument = null;
            try
            {
                JsonElement page;
                if (kind == "album") page = root.GetProperty("tracks");
                else
                {
                    pageDocument = await GetAsync(new Uri("https://api.spotify.com/v1/playlists/" + id + "/items?limit=50"), cancellationToken).ConfigureAwait(false);
                    page = pageDocument.RootElement;
                }
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    foreach (var item in page.GetProperty("items").EnumerateArray())
                    {
                        if (tracks.Count >= limit) { truncated = true; break; }
                        var track = item;
                        if (kind == "playlist" && !(item.TryGetProperty("item", out track) || item.TryGetProperty("track", out track)))
                            track = default;
                        tracks.Add(ReadTrack(track, kind == "album" ? title : null));
                    }
                    var next = Text(page, "next");
                    if (next is null || truncated) break;
                    if (tracks.Count >= limit) { truncated = true; break; }
                    if (!visited.Add(next)) throw new InvalidDataException("Spotify returned a repeated page.");
                    var nextUri = new Uri(next);
                    pageDocument?.Dispose();
                    pageDocument = await GetAsync(nextUri, cancellationToken).ConfigureAwait(false);
                    page = pageDocument.RootElement;
                }
            }
            finally { pageDocument?.Dispose(); }
        }
        return new ResolvedCatalog(title, tracks, uri, truncated);
    }

    public static bool TryParseUrl(Uri uri, out string kind, out string id)
    {
        kind = id = string.Empty;
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.IdnHost != "open.spotify.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[0].StartsWith("intl-", StringComparison.Ordinal)) parts = parts[1..];
        if (parts.Length != 2 || parts[0] is not ("track" or "album" or "playlist") ||
            parts[1].Length != 22 || !parts[1].All(char.IsAsciiLetterOrDigit)) return false;
        kind = parts[0]; id = parts[1]; return true;
    }

    private async Task<JsonDocument> GetAsync(Uri uri, CancellationToken token)
    {
        // Never forward a bearer token to a pagination URL outside Spotify's API.
        if (uri.Scheme != "https" || uri.IdnHost != "api.spotify.com" || !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid Spotify API pagination URL.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(timeout.Token).ConfigureAwait(false));
        using var response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Spotify denied access. Playlists must be yours or collaborative; check developer allowlist and Premium requirements.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Spotify quota/rate limit reached. Wait before trying again.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Spotify session expired. Disconnect and connect again.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Spotify could not open this catalog item.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
    }

    private static ResolvedTrack ReadTrack(JsonElement item, string? album)
    {
        if (item.ValueKind != JsonValueKind.Object || Text(item, "type") is "episode" ||
            (item.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True))
            return new ResolvedTrack(new CatalogTrack("Unavailable item", [], null, null, null), null, [], "Unavailable");
        if (item.TryGetProperty("album", out var albumElement)) album = Text(albumElement, "name");
        var artists = item.TryGetProperty("artists", out var artistElements)
            ? artistElements.EnumerateArray().Select(artist => Text(artist, "name")).OfType<string>().ToArray() : [];
        var title = Text(item, "name");
        var duration = item.TryGetProperty("duration_ms", out var milliseconds) && milliseconds.TryGetInt64(out var ms) && ms > 0
            ? (TimeSpan?)TimeSpan.FromMilliseconds(ms) : null;
        var isrc = item.TryGetProperty("external_ids", out var ids) ? Text(ids, "isrc") : null;
        var id = Text(item, "id");
        var source = id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit) ? new Uri("https://open.spotify.com/track/" + id) : null;
        return new ResolvedTrack(new CatalogTrack(title ?? "Unavailable item", artists, album, duration, isrc, source),
            null, [], title is null ? "Unavailable" : null);
    }

    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
