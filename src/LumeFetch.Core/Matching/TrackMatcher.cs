using System.Globalization;
using System.Text;
using LumeFetch.Core.Resolvers;

namespace LumeFetch.Core.Matching;

public static class TrackMatcher
{
    private static readonly string[] PublicationLabels = ["official", "audio", "video", "music", "lyrics", "lyric", "hd", "4k", "visualizer"];
    public static ResolverCandidate Score(CatalogTrack track, TrackSearchResult candidate)
    {
        var title = Tokens(track.Title);
        var candidateTitle = Tokens(candidate.Title);
        var artistTokens = track.Artists.SelectMany(Tokens).ToHashSet(StringComparer.Ordinal);
        var channel = Tokens(candidate.Artist);
        var artistCoverage = Coverage(artistTokens, channel.Concat(candidateTitle).ToHashSet(StringComparer.Ordinal));
        // Remove artist and common publication labels, but preserve remix/live/cover distinctions.
        candidateTitle.ExceptWith(artistTokens.Except(title));
        candidateTitle.ExceptWith(PublicationLabels.Except(title));
        var titleSimilarity = title.Count == 0 ? 0 : 2d * title.Intersect(candidateTitle).Count() / (title.Count + candidateTitle.Count);
        var titlePoints = (int)Math.Round(35 * titleSimilarity);
        var artistPoints = (int)Math.Round(35 * artistCoverage);
        var durationPoints = track.Duration is { } expected && candidate.Duration is { } actual &&
            Math.Abs((expected - actual).TotalSeconds) < 3 ? 20 : 0;
        var trustedPoints = candidate.IsTrustedSource ? 10 : 0;
        var total = titlePoints + artistPoints + durationPoints + trustedPoints;
        string[] versions = ["live", "remix", "cover", "karaoke", "instrumental", "sped", "slowed"];
        if (versions.Any(version => title.Contains(version) != candidateTitle.Contains(version))) total = Math.Min(total, 69);
        return new ResolverCandidate(candidate.SourceUri, "YouTube", candidate.Title, candidate.Artist, candidate.Duration,
            total, [$"title:{titlePoints}/35", $"artist:{artistPoints}/35", $"duration:{durationPoints}/20", $"source:{trustedPoints}/10"]);
    }

    private static double Coverage(HashSet<string> expected, HashSet<string> actual) =>
        expected.Count == 0 ? 0 : (double)expected.Intersect(actual).Count() / expected.Count;

    private static HashSet<string> Tokens(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
}
