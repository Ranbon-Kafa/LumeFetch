using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LumeFetch.Infrastructure.Spotify;

public interface ISpotifyTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>PKCE public client. Tokens live only in memory and are never written to settings/logs.</summary>
public sealed class SpotifySession(HttpClient httpClient) : ISpotifyTokenSource, IDisposable
{
    public const string RedirectUri = "http://127.0.0.1:43821/callback";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _stateLock = new();
    private string? _accessToken;
    private string? _refreshToken;
    private string? _clientId;
    private DateTimeOffset _expires;
    private int _generation;
    public bool IsConnected { get { lock (_stateLock) return _accessToken is not null; } }

    public async Task ConnectAsync(string clientId, Func<Uri, Task> openBrowser, CancellationToken cancellationToken = default)
    {
        if (clientId.Length != 32 || !clientId.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("Enter your Spotify application's 32-character Client ID in Settings.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var generation = _generation;
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 43821);
            listener.Start();
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["response_type"] = "code",
                    ["redirect_uri"] = RedirectUri,
                    ["code_challenge_method"] = "S256",
                    ["code_challenge"] = challenge,
                    ["state"] = state,
                    ["scope"] = "playlist-read-private playlist-read-collaborative",
                };
                await openBrowser(new Uri("https://accounts.spotify.com/authorize?" + string.Join("&",
                    parameters.Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value))))).ConfigureAwait(false);
                var code = await ReceiveCallbackAsync(listener, state, timeout.Token).ConfigureAwait(false);
                using var response = await httpClient.PostAsync("https://accounts.spotify.com/api/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = RedirectUri,
                        ["client_id"] = clientId,
                        ["code_verifier"] = verifier,
                    }), timeout.Token).ConfigureAwait(false);
                await AcceptTokensAsync(response, generation, timeout.Token, clientId).ConfigureAwait(false);
            }
            finally { listener.Stop(); }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string refreshToken;
            string clientId;
            int generation;
            lock (_stateLock)
            {
                if (_accessToken is null) throw new InvalidOperationException("Connect Spotify in Settings first.");
                if (_expires > DateTimeOffset.UtcNow.AddSeconds(45)) return _accessToken;
                if (_refreshToken is null || _clientId is null) throw new InvalidOperationException("Spotify session expired. Connect again.");
                refreshToken = _refreshToken; clientId = _clientId; generation = _generation;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await httpClient.PostAsync("https://accounts.spotify.com/api/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = clientId,
                }), timeout.Token).ConfigureAwait(false);
            await AcceptTokensAsync(response, generation, timeout.Token).ConfigureAwait(false);
            lock (_stateLock) return _accessToken ?? throw new OperationCanceledException("Spotify was disconnected.");
        }
        finally { _gate.Release(); }
    }

    public void Disconnect()
    {
        lock (_stateLock)
        {
            _generation++;
            _accessToken = null;
            _refreshToken = null;
            _clientId = null;
        }
    }

    public void Dispose() { Disconnect(); _gate.Dispose(); }

    private async Task AcceptTokensAsync(HttpResponseMessage response, int generation, CancellationToken token, string? clientId = null)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Spotify authorization failed. Check Client ID, redirect URI, Premium/development access and allowed users.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = document.RootElement;
        lock (_stateLock)
        {
            if (generation != _generation) throw new OperationCanceledException("Spotify was disconnected.");
            _accessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidDataException("No Spotify token.");
            if (root.TryGetProperty("refresh_token", out var refresh)) _refreshToken = refresh.GetString();
            _expires = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32());
            if (clientId is not null) _clientId = clientId;
        }
    }

    public static string ValidateCallback(Uri callback, string expectedState)
    {
        if (callback.GetLeftPart(UriPartial.Path) != RedirectUri) throw new InvalidDataException("Invalid OAuth redirect.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (!values.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty))
                throw new InvalidDataException("Duplicate OAuth parameter.");
        }
        if (!values.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            throw new InvalidDataException("OAuth state validation failed.");
        if (values.ContainsKey("error")) throw new InvalidOperationException("Spotify authorization was declined.");
        return values.TryGetValue("code", out var code) && code.Length > 0 ? code : throw new InvalidDataException("Missing OAuth code.");
    }

    private static async Task<string> ReceiveCallbackAsync(TcpListener listener, string state, CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[8192];
            var used = 0;
            while (used < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(used), connectionTimeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                used += count;
                if (Encoding.ASCII.GetString(buffer, 0, used).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var line = Encoding.ASCII.GetString(buffer, 0, used).Split("\r\n", StringSplitOptions.None)[0].Split(' ');
            if (line.Length < 2 || line[0] != "GET" || !line[1].StartsWith("/callback?", StringComparison.Ordinal)) continue;
            string message;
            string? code = null;
            bool declined = false;
            try { code = ValidateCallback(new Uri("http://127.0.0.1:43821" + line[1]), state); message = "Login response received. Return to LumeFetch to check the connection."; }
            catch (InvalidDataException) { message = "Invalid login response. Return to LumeFetch and try again."; }
            catch (InvalidOperationException) { message = "Authorization declined. You can close this page."; declined = true; }
            var body = Encoding.UTF8.GetBytes(message);
            var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " +
                body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, token).ConfigureAwait(false);
            await stream.WriteAsync(body, token).ConfigureAwait(false);
            if (declined) throw new InvalidOperationException("Spotify authorization was declined.");
            if (code is not null) return code;
        }
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
