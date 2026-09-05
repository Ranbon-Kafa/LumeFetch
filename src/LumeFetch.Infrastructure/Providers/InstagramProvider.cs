using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public sealed class InstagramProvider(IYtDlpClient client) : YtDlpMediaProvider(client,
    "instagram.com", "www.instagram.com", "m.instagram.com")
{
    public override string Id => "instagram";
    public override string DisplayName => "Instagram";
}
