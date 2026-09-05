using LumeFetch.Core.Media;
using LumeFetch.Core.Providers;

namespace LumeFetch.Core.Services;

public sealed class MediaAnalysisService
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
    };

    private readonly ProviderRegistry _providers;

    public MediaAnalysisService(ProviderRegistry providers)
    {
        _providers = providers;
    }

    public async Task<MediaInfo> AnalyzeAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !AllowedSchemes.Contains(uri.Scheme))
        {
            throw new MediaAnalysisException("Enter a complete HTTP or HTTPS address.");
        }

        var provider = _providers.Resolve(uri);

        try
        {
            return await provider.AnalyzeAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MediaAnalysisException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MediaAnalysisException(
                $"{provider.DisplayName} could not analyze this address.",
                exception);
        }
    }
}
