using LumeFetch.Core.Media;

namespace LumeFetch.Core.Collections;

public enum CollectionQuality { BestVideo, Video1080, Video720, Audio, Mp3 }

public static class CollectionOptionSelector
{
    public static DownloadOption Select(MediaInfo media, CollectionQuality preference)
    {
        var choices = media.Options.Where(option => preference is CollectionQuality.Audio or CollectionQuality.Mp3
            ? option.Kind == MediaKind.Audio : option.Kind == MediaKind.Video);
        if (preference == CollectionQuality.Mp3)
            choices = choices.Where(option => option.Container == "mp3");
        if (preference is CollectionQuality.Video1080 or CollectionQuality.Video720)
            choices = choices.Where(option => option.Height is > 0 && option.Height <= (preference == CollectionQuality.Video1080 ? 1080 : 720));
        return choices.OrderByDescending(option => option.Height)
            .ThenByDescending(option => option.Container is "mp4" or "m4a").FirstOrDefault()
            ?? throw new InvalidOperationException("No format satisfies the selected batch quality. Choose another quality.");
    }
}
