using System.Text;

namespace LumeFetch.Infrastructure.Files;

public static class SafeFileName
{
    private const int MaximumLength = 140;

    public static string Create(string value, string fallback = "download")
    {
        var invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])
            .ToHashSet();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Normalize(NormalizationForm.FormC))
        {
            if (!invalidCharacters.Contains(character) && !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(result))
        {
            result = fallback;
        }

        if (result.Length > MaximumLength)
            result = result[..(char.IsHighSurrogate(result[MaximumLength - 1]) ? MaximumLength - 1 : MaximumLength)].TrimEnd(' ', '.');
        var stem = result.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             stem[3] is >= '1' and <= '9'))
            result = "_" + result;
        return result;
    }
}
