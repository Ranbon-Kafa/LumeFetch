namespace LumeFetch.Core.Resolvers;

public sealed record TrackSearchResult(Uri SourceUri, string Title, string Artist, TimeSpan? Duration, bool IsTrustedSource);

public interface ITrackSearch
{
    Task<IReadOnlyList<TrackSearchResult>> SearchAsync(CatalogTrack track, CancellationToken cancellationToken = default);
}
