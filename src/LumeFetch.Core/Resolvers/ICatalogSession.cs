namespace LumeFetch.Core.Resolvers;

/// <summary>Host-specific catalog authentication. Register only where the resolver is permitted.</summary>
public interface ICatalogSession
{
    bool IsConnected { get; }
    string CallbackUri { get; }
    Task ConnectAsync(string clientId, Func<Uri, Task> openBrowser, CancellationToken cancellationToken = default);
    void Disconnect();
}
