using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LumeFetch.Infrastructure.Spotify;

namespace LumeFetch.Screenshot;

internal static class SpotifyOAuthSmoke
{
    public static async Task RunAsync()
    {
        using var handler = new TokenHandler();
        using var http = new HttpClient(handler);
        using var session = new SpotifySession(http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task callback = Task.CompletedTask;
        await session.ConnectAsync(new string('a', 32), uri =>
        {
            var query = ParseForm(uri.Query.TrimStart('?'));
            Program.Require(uri.Host == "accounts.spotify.com" && query["code_challenge_method"] == "S256", "PKCE authorization endpoint");
            handler.Challenge = query["code_challenge"];
            callback = SendCallbackAsync(query["state"], timeout.Token);
            return Task.CompletedTask;
        }, timeout.Token);
        await callback;
        Program.Require(session.IsConnected && handler.Calls == 1, "Loopback OAuth exchanges the code");
        Program.Require(await session.GetAccessTokenAsync(timeout.Token) == "fixture-refreshed", "Expired memory token refreshes");
        Program.Require(handler.Calls == 2, "Refresh request sent exactly once");
        session.Disconnect();
        Program.Require(!session.IsConnected, "Disconnect clears tokens");
        try
        {
            await session.GetAccessTokenAsync(timeout.Token);
            throw new InvalidDataException("Disconnected token was exposed.");
        }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS: real loopback OAuth callback, PKCE verifier, state validation, in-memory token refresh and disconnect (mock token server; no Spotify account).");
    }

    private static async Task SendCallbackAsync(string state, CancellationToken token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, 43821, token);
        await using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes("GET /callback?state=" + Uri.EscapeDataString(state) +
            "&code=fixture-code HTTP/1.1\r\nHost: 127.0.0.1:43821\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, token);
        using var response = new StreamReader(stream);
        var body = await response.ReadToEndAsync(token);
        Program.Require(body.Contains("200 OK", StringComparison.Ordinal), "Browser callback receives a response");
    }

    private static Dictionary<string, string> ParseForm(string form) => form.Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private sealed class TokenHandler : HttpMessageHandler
    {
        public string? Challenge { get; set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Program.Require(request.RequestUri!.AbsoluteUri == "https://accounts.spotify.com/api/token", "Exact token endpoint");
            var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));
            Program.Require(!form.ContainsKey("client_secret"), "Desktop client never needs a secret");
            if (Calls == 1)
            {
                var hash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Program.Require(hash == Challenge && form["code"] == "fixture-code" &&
                    form["redirect_uri"] == SpotifySession.RedirectUri, "Code exchange proves the PKCE verifier");
            }
            else Program.Require(form["grant_type"] == "refresh_token" && form["refresh_token"] == "fixture-refresh", "Refresh uses in-memory credential");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Calls == 1
                    ? """{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":1}"""
                    : """{"access_token":"fixture-refreshed","expires_in":3600}""")
            };
        }
    }
}
