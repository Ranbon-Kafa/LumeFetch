namespace LumeFetch.Core.Providers;

public sealed class ProviderRegistry
{
    private readonly IMediaProvider[] _providers;

    public ProviderRegistry(IEnumerable<IMediaProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.Id, StringComparer.Ordinal)
            .ToArray();

        if (_providers.Select(provider => provider.Id).Distinct(StringComparer.Ordinal).Count() != _providers.Length)
        {
            throw new ArgumentException("Provider ids must be unique.", nameof(providers));
        }
    }

    public IReadOnlyList<IMediaProvider> Providers => _providers;

    public IMediaProvider Resolve(Uri uri) =>
        _providers.FirstOrDefault(provider => provider.CanHandle(uri))
        ?? throw new NotSupportedException($"No media provider can handle '{uri.Host}'.");

    public IMediaProvider GetById(string providerId) =>
        _providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Media provider '{providerId}' is not registered.");
}
