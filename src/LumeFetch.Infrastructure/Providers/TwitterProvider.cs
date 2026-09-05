using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public sealed class TwitterProvider(IYtDlpClient client) : YtDlpMediaProvider(client,
    "x.com", "www.x.com", "mobile.x.com", "twitter.com", "www.twitter.com", "mobile.twitter.com", "t.co")
{
    public override string Id => "twitter";
    public override string DisplayName => "X / Twitter";
}
