using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Infrastructure.Providers;

public sealed class YouTubeProvider(IYtDlpClient client) : YtDlpMediaProvider(client,
    "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be")
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
}
