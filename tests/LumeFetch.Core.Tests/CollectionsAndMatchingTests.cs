using LumeFetch.Core.Collections;
using LumeFetch.Core.Localization;
using LumeFetch.Core.Matching;
using LumeFetch.Core.Media;
using LumeFetch.Core.Resolvers;
using LumeFetch.Infrastructure.YtDlp;

namespace LumeFetch.Core.Tests;

public sealed class CollectionsAndMatchingTests
{
    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=example", true)]
    [InlineData("https://music.youtube.com/watch?v=abcdefghijk&list=example", true)]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", false)]
    [InlineData("https://youtube.com.attacker.org/playlist?list=example", false)]
    [InlineData("file:///playlist", false)]
    public void OnlyPlaylistLinksExpand(string url, bool expected)
    {
        var client = new YtDlpClient();
        Assert.Equal(expected, client.CanExpand(new Uri(url)));
    }

    [Fact]
    public void PlaylistPreservesOrderUnavailableRowsAndTruncation()
    {
        const string json = """
            {"title":"Sample playlist","entries":[
              {"id":"abcdefghijk","title":"One","duration":123},
              null,
              {"id":"12345678901","title":"Private","availability":"private"},
              {"title":"Bad source","url":"https://attacker.org/video"},
              {"id":"abcdefghijk","title":"Duplicate intentionally retained"},
              {"id":"ABCDEFGHIJK","title":"Beyond limit"}
            ]}
            """;
        var result = YtDlpClient.ParseCollection(json, new Uri("https://youtube.com/playlist?list=example"), 5);
        Assert.Equal(5, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.Equal(new Uri("https://www.youtube.com/watch?v=abcdefghijk"), result.Entries[0].SourceUri);
        Assert.Equal(TimeSpan.FromSeconds(123), result.Entries[0].Duration);
        Assert.All(result.Entries.Skip(1).Take(3), row => Assert.NotNull(row.UnavailableReason));
        Assert.Equal(result.Entries[0].SourceUri, result.Entries[4].SourceUri);
        Assert.Equal([1, 2, 3, 4, 5], result.Entries.Select(row => row.Index));
    }

    [Theory]
    [InlineData(CollectionQuality.Video1080, "720")]
    [InlineData(CollectionQuality.Video720, "720")]
    [InlineData(CollectionQuality.BestVideo, "4k")]
    [InlineData(CollectionQuality.Audio, "audio")]
    [InlineData(CollectionQuality.Mp3, "mp3")]
    public void BatchQualityUsesActualFormatsWithoutExceedingCap(CollectionQuality quality, string id)
    {
        var media = new MediaInfo("test", "Test", new Uri("https://example.org/video"), "Sample", null, null, [
            new DownloadOption("4k", "2160p", "webm", MediaKind.Video, Height: 2160),
            new DownloadOption("720", "720p", "mp4", MediaKind.Video, Height: 720),
            new DownloadOption("audio", "Audio", "m4a", MediaKind.Audio),
            new DownloadOption("mp3", "Audio", "mp3", MediaKind.Audio)]);
        Assert.Equal(id, CollectionOptionSelector.Select(media, quality).Id);
        Assert.Throws<InvalidOperationException>(() => CollectionOptionSelector.Select(media with { Options = [media.Options[0]] }, CollectionQuality.Video1080));
        Assert.Throws<InvalidOperationException>(() => CollectionOptionSelector.Select(media with { Options = [media.Options[2]] }, CollectionQuality.Mp3));
    }

    [Fact]
    public void MatchingWeightsAndDurationBoundaryAreExplicit()
    {
        var track = Track("Northern Light");
        Assert.Equal(100, TrackMatcher.Score(track, Candidate("Aurora - Northern Light (Official Audio)", 182)).Confidence);
        Assert.Equal(80, TrackMatcher.Score(track, Candidate("Northern Light", 183)).Confidence);
        Assert.Equal(90, TrackMatcher.Score(track, Candidate("Northern Light", 180) with { IsTrustedSource = false }).Confidence);
        Assert.Equal(80, TrackMatcher.Score(track, Candidate("Northern Light", 180) with { Duration = null }).Confidence);
    }

    [Theory]
    [InlineData("Northern Light (live)")]
    [InlineData("Northern Light remix")]
    [InlineData("Northern Light cover")]
    [InlineData("Northern Light sped up")]
    public void DifferentVersionsCannotBeHighConfidence(string title) =>
        Assert.InRange(TrackMatcher.Score(Track("Northern Light"), Candidate(title, 180)).Confidence, 0, 69);

    [Fact]
    public void MatchingHandlesAccentsAndTitlesContainingArtistOrLabelWords()
    {
        Assert.Equal(100, TrackMatcher.Score(Track("Étoile"), Candidate("Etoile", 180)).Confidence);
        Assert.Equal(100, TrackMatcher.Score(Track("Aurora Music"), Candidate("Aurora Music (Official Audio)", 180)).Confidence);
        Assert.InRange(TrackMatcher.Score(Track("Northern Light"), Candidate("Unrelated Recording", 180)).Confidence, 0, 69);
        Assert.Equal(0, TrackMatcher.Score(new CatalogTrack("", [], null, null, null),
            Candidate("", 180) with { IsTrustedSource = false, Artist = "" }).Confidence);
    }

    [Fact]
    public void TranslationKeysAndFormattingMatch()
    {
        var english = new TranslationCatalog("en");
        var turkish = new TranslationCatalog("tr");
        Assert.Equal(english.Keys.Order(), turkish.Keys.Order());
        foreach (var key in english.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(turkish[key]));
            Assert.Equal(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(turkish[key]).MinimumArgumentCount);
            _ = turkish.Format(key, 1, 2, 3);
        }
        Assert.Equal("İndiriliyor", turkish["Downloading"]);
        Assert.Equal(english["Home"], new TranslationCatalog("unsupported")["Home"]);
        Assert.Equal("missing-key", turkish["missing-key"]);
    }

    private static CatalogTrack Track(string title) => new(title, ["Aurora"], "Sample Album", TimeSpan.FromSeconds(180), "XXTEST000001");
    private static TrackSearchResult Candidate(string title, int seconds) =>
        new(new Uri("https://www.youtube.com/watch?v=abcdefghijk"), title, "Aurora - Topic", TimeSpan.FromSeconds(seconds), true);
}
