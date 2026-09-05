namespace LumeFetch.Core.Resolvers;

public sealed record CatalogTrack(
    string Title,
    IReadOnlyList<string> Artists,
    string? Album,
    TimeSpan? Duration,
    string? Isrc,
    Uri? CatalogUri = null);

public sealed record ResolverCandidate(
    Uri SourceUri,
    string SourceName,
    string Title,
    string? Artist,
    TimeSpan? Duration,
    int Confidence,
    IReadOnlyList<string> Reasons);

public sealed record ResolvedTrack(
    CatalogTrack Track,
    ResolverCandidate? BestMatch,
    IReadOnlyList<ResolverCandidate> Alternatives,
    string? UnavailableReason = null);

public sealed record ResolvedCatalog(
    string Title,
    IReadOnlyList<ResolvedTrack> Tracks,
    Uri? SourceUri = null,
    bool IsTruncated = false);
