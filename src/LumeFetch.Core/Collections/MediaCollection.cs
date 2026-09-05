namespace LumeFetch.Core.Collections;

public sealed record CollectionEntry(int Index, string Title, Uri? SourceUri, TimeSpan? Duration, string? UnavailableReason = null);
public sealed record MediaPlaylist(string Title, Uri SourceUri, IReadOnlyList<CollectionEntry> Entries, bool IsTruncated = false);

public interface IMediaCollectionProvider
{
    bool CanExpand(Uri uri);
    Task<MediaPlaylist> ExpandAsync(Uri uri, int limit, CancellationToken cancellationToken = default);
}
