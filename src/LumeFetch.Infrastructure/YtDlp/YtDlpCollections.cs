using System.Text.Json;
using LumeFetch.Core.Collections;
using LumeFetch.Core.Media;
using LumeFetch.Core.Resolvers;

namespace LumeFetch.Infrastructure.YtDlp;

public sealed partial class YtDlpClient : IMediaCollectionProvider, ITrackSearch
{
    public bool CanExpand(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https" &&
        uri.IdnHost is "youtube.com" or "www.youtube.com" or "music.youtube.com" or "m.youtube.com" &&
        (uri.AbsolutePath == "/playlist" || uri.Query.Split('&').Any(part => part.TrimStart('?').StartsWith("list=", StringComparison.Ordinal)));

    public async Task<MediaPlaylist> ExpandAsync(Uri uri, int limit, CancellationToken cancellationToken = default)
    {
        if (!CanExpand(uri)) throw new MediaAnalysisException("Only YouTube and YouTube Music playlist URLs are supported.");
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        var output = await RunCaptureAsync(["--dump-single-json", "--flat-playlist", "--yes-playlist", "--ignore-errors",
            "--playlist-end", (limit + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--skip-download", "--socket-timeout", "20", "--", uri.AbsoluteUri], cancellationToken).ConfigureAwait(false);
        return ParseCollection(output, uri, limit);
    }

    public static MediaPlaylist ParseCollection(string json, Uri source, int limit)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new MediaAnalysisException("The source did not return a playlist.");
        var result = new List<CollectionEntry>();
        var index = 0;
        foreach (var entry in entries.EnumerateArray().Take(limit))
        {
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                result.Add(new CollectionEntry(index, "Unavailable item", null, null, "Unavailable"));
                continue;
            }
            var title = GetString(entry, "title") ?? "Unavailable item";
            var url = YouTubeUri(entry);
            var available = GetString(entry, "availability") is not ("private" or "premium_only" or "subscriber_only" or "needs_auth");
            var seconds = GetDouble(entry, "duration");
            result.Add(new CollectionEntry(index, title, url,
                seconds is > 0 and < 315360000 ? TimeSpan.FromSeconds(seconds.Value) : null,
                available && url is not null ? null : "Unavailable"));
        }
        return new MediaPlaylist(GetString(root, "title") ?? "YouTube playlist", source, result, entries.GetArrayLength() > limit);
    }

    public async Task<IReadOnlyList<TrackSearchResult>> SearchAsync(CatalogTrack track, CancellationToken cancellationToken = default)
    {
        var query = string.Join(" ", track.Artists) + " " + track.Title + " official audio";
        var output = await RunCaptureAsync(["--dump-single-json", "--flat-playlist", "--skip-download", "--socket-timeout", "20",
            "--", "ytsearch5:" + query], cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("entries", out var entries)) return [];
        var results = new List<TrackSearchResult>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || YouTubeUri(entry) is not { } uri) continue;
            var seconds = GetDouble(entry, "duration");
            var channel = GetString(entry, "channel") ?? GetString(entry, "uploader") ?? string.Empty;
            var verified = entry.TryGetProperty("channel_is_verified", out var value) && value.ValueKind == JsonValueKind.True;
            results.Add(new TrackSearchResult(uri, GetString(entry, "title") ?? string.Empty, channel,
                seconds is > 0 and < 315360000 ? TimeSpan.FromSeconds(seconds.Value) : null,
                verified || channel.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)));
        }
        return results;
    }

    private static Uri? YouTubeUri(JsonElement entry)
    {
        var id = GetString(entry, "id");
        if (id is { Length: 11 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            return new Uri("https://www.youtube.com/watch?v=" + id);
        var url = GetUri(entry, "webpage_url") ?? GetUri(entry, "url");
        return url is not null && url.Scheme == "https" && url.IdnHost is "youtube.com" or "www.youtube.com" or "music.youtube.com" or "youtu.be" ? url : null;
    }
}
