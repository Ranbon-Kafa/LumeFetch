namespace LumeFetch.Core.Resolvers;

public interface IMediaResolver
{
    string Id { get; }

    string DisplayName { get; }

    bool CanResolve(Uri uri);

    Task<ResolvedCatalog> ResolveAsync(Uri uri, CancellationToken cancellationToken = default);
}
