using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public sealed class RedditProvider(IYtDlpClient client) : YtDlpMediaProvider(client,
    "reddit.com", "www.reddit.com", "old.reddit.com", "new.reddit.com", "m.reddit.com", "redd.it", "v.redd.it")
{
    public override string Id => "reddit";
    public override string DisplayName => "Reddit";
}
