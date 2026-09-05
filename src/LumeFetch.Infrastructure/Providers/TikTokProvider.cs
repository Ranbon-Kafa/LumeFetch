using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public sealed class TikTokProvider(IYtDlpClient client) : YtDlpMediaProvider(client,
    "tiktok.com", "www.tiktok.com", "m.tiktok.com", "vm.tiktok.com", "vt.tiktok.com")
{
    public override string Id => "tiktok";
    public override string DisplayName => "TikTok";
}
